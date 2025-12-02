using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Video;
using UnityEditor;
using TMPro;

public class RaceManager : MonoBehaviour
{
    [SerializeField] private List<DefaultAsset> videoSets;
    public List<string> words;
    public List<VideoClip> videos;
    [SerializeField] private TypingManager typingManager;
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private GameObject connectionUI;
    [SerializeField] private GameObject gameUI;
    [SerializeField] private GameObject gameOverUI;
    [SerializeField] private TextMeshProUGUI gameOverText;
    private CarManager myCar;

    private void Start()
    {
        gameOverUI.SetActive(false);
        StartCoroutine(FindLocalCar());
        StartCoroutine(LoadVideosWhenReady());
    }

    private IEnumerator LoadVideosWhenReady()
    {
        while (myCar == null || myCar.wordSet.Value < 0)
        {
            yield return new WaitForSeconds(0.1f);
        }

        videos.Clear();
        
        DefaultAsset folder = videoSets[myCar.wordSet.Value];
        string folderPath = AssetDatabase.GetAssetPath(folder);
        string[] guids = AssetDatabase.FindAssets("t:VideoClip", new[] { folderPath });
        foreach (string guid in guids)
        {
            string path = AssetDatabase.GUIDToAssetPath(guid);
            VideoClip clip = AssetDatabase.LoadAssetAtPath<VideoClip>(path);
            if (clip != null)
            {
                videos.Add(clip);
            }
        }

        videoPlayer.clip = videos[myCar.wordsCompleted.Value];

        words.Clear();
        string wordListPath = folderPath + "/wordlist.txt";
        if (System.IO.File.Exists(wordListPath))
        {
            string fileContent = System.IO.File.ReadAllText(wordListPath);
            string[] loadedWords = fileContent.Split(new[] { '\n', '\r' }, System.StringSplitOptions.RemoveEmptyEntries);
            words.AddRange(loadedWords);
        }
    }

    private IEnumerator FindLocalCar()
    {
        while (myCar == null)
        {
            CarManager[] allCars = FindObjectsByType<CarManager>(FindObjectsSortMode.None);
            
            foreach (CarManager car in allCars)
            {
                if (car.IsOwner)
                {
                    myCar = car;
                    Debug.Log("Found car");
                    yield break;
                }
            }
            
            yield return new WaitForSeconds(0.2f);
        }
    }

    public bool SubmitWord(string word)
    {
        if (myCar == null)
        {
            Debug.LogWarning("car not ready");
            StartCoroutine(FailDelay());
            return false;
        }

        if (word == words[myCar.wordsCompleted.Value])
        {
            myCar.MoveCarServerRpc();
            StartCoroutine(WaitForMoveAnimation());
            return true;
        }
        else
        {
            StartCoroutine(FailDelay());
            return false;
        }
    }

    private IEnumerator WaitForMoveAnimation()
    {
        yield return new WaitForSeconds(1f);
        typingManager.Reset();
        videoPlayer.clip = videos[myCar.wordsCompleted.Value];
    }

    private IEnumerator FailDelay()
    {
        yield return new WaitForSeconds(1);
        typingManager.Reset();
    }

    public void GameOver(ulong winnerClientId)
    {
        connectionUI.SetActive(false);
        gameUI.SetActive(false);
        gameOverText.text = "Winner: \nPlayer " + (winnerClientId + 1);
        gameOverUI.SetActive(true);
        typingManager.Reset();
    }

    public void Restart()
    {
        gameUI.SetActive(false);
        gameOverUI.SetActive(false);
        connectionUI.SetActive(false);
        typingManager.Reset();
        
        videoPlayer.clip = videos[0];
        myCar.RestartGameServerRpc();
    }

    public int GetWordCount()
    {
        return words.Count;
    }
}