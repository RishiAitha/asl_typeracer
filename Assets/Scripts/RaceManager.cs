using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Video;
using TMPro;

public class RaceManager : MonoBehaviour
{
    [SerializeField] private List<string> words;
    [SerializeField] private List<VideoClip> videos;
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
                    videoPlayer.clip = videos[myCar.wordsCompleted.Value];
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