using UnityEngine;
using Unity.Netcode;
using System.Collections;
using System.Collections.Generic;
using UnityEngine.Video;
using TMPro;

public class RaceManager : MonoBehaviour
{
    // ==================== Inspector / Serialized Fields ====================
    [SerializeField] private List<VideoSet> videoSetsData;
    [SerializeField] private TypingManager typingManager;
    [SerializeField] private VideoPlayer videoPlayer;
    [SerializeField] private GameObject connectionUI;
    [SerializeField] private GameObject gameUI;
    [SerializeField] private GameObject gameOverUI;
    [SerializeField] private TextMeshProUGUI gameOverText;

    // ==================== Runtime State ====================
    public List<string> words;
    public List<VideoClip> videos;
    private CarManager myCar;

    // ==================== Unity Lifecycle ====================
    private void Start()
    {
        gameOverUI.SetActive(false);
        StartCoroutine(FindLocalCar());
        StartCoroutine(LoadVideosWhenReady());
    }

    // ==================== Coroutines / Loading ====================
    private IEnumerator LoadVideosWhenReady()
    {
        while (myCar == null || myCar.wordSet.Value < 0)
        {
            yield return new WaitForSeconds(0.1f);
        }
        

        // get the video set from the list
        VideoSet selectedSet = videoSetsData[myCar.wordSet.Value];
        videos = new List<VideoClip>(selectedSet.videos);
        words = new List<string>(selectedSet.words);

        videoPlayer.clip = videos[myCar.wordsCompleted.Value];

        // Now that words/videos are loaded, ensure all cars compute their distIncrement
        CarManager[] allCars = FindObjectsByType<CarManager>(FindObjectsSortMode.None);
        foreach (CarManager car in allCars)
        {
            car.SetDistIncrement();
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
                    // subscribe to networked wordsCompleted changes so client updates video when server increments
                    myCar.wordsCompleted.OnValueChanged += OnWordsCompletedChanged;
                    yield break;
                    }
            }
            
            yield return new WaitForSeconds(0.2f);
        }
    }

    private void OnWordsCompletedChanged(int oldValue, int newValue)
    {
        // update video clip when the authoritative wordsCompleted changes
        if (videos == null || videos.Count == 0) return;

        int idx = newValue;
        if (idx < 0) idx = 0;
        if (idx >= videos.Count) idx = videos.Count - 1;

        videoPlayer.clip = videos[idx];
    }

    // ==================== Gameplay / Submission ====================
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
            int prev = myCar.wordsCompleted.Value;
            myCar.MoveCarServerRpc();
            StartCoroutine(WaitForMoveAnimation(prev));
            return true;
        }
        else
        {
            StartCoroutine(FailDelay());
            return false;
        }
    }

    private IEnumerator WaitForMoveAnimation(int prevWordsCompleted)
    {
        // wait briefly for server to process the move and update the network variable
        float timeout = 2f;
        float waited = 0f;
        const float step = 0.1f;

        while (myCar.wordsCompleted.Value <= prevWordsCompleted && waited < timeout)
        {
            yield return new WaitForSeconds(step);
            waited += step;
        }

        // reset input UI
        typingManager.Reset();

        // guard against out-of-range indexes
        if (videos == null || videos.Count == 0) yield break;

        int idx = myCar.wordsCompleted.Value;
        if (idx < 0) idx = 0;
        if (idx >= videos.Count) idx = videos.Count - 1;

        videoPlayer.clip = videos[idx];
    }

    private IEnumerator FailDelay()
    {
        yield return new WaitForSeconds(1);
        typingManager.Reset();
    }

    // ==================== UI / Game Flow ====================
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
        // server-side restart: reset UI/server state and ask server to reset authoritative state
        gameUI.SetActive(true);
        gameOverUI.SetActive(false);
        connectionUI.SetActive(false);
        typingManager.Reset();

        if (videos != null && videos.Count > 0)
        {
            videoPlayer.clip = videos[0];
        }

        myCar.RestartGameServerRpc();
    }

    // Client-only UI/state reset after server restarts the game. Does not call server RPCs.
    public void ApplyClientRestart()
    {
        // show game UI after restart
        gameUI.SetActive(true);
        gameOverUI.SetActive(false);
        connectionUI.SetActive(false);
        typingManager.Reset();

        if (videos != null && videos.Count > 0)
        {
            videoPlayer.clip = videos[0];
        }
    }

    public void MainMenu()
    {
        gameUI.SetActive(false);
        gameOverUI.SetActive(false);
        connectionUI.SetActive(true);
        
        typingManager.Reset();
        
        if (myCar != null)
        {
            myCar.RestartGameServerRpc();
        }
    }

    public void DisconnectMainMenu()
    {
        gameUI.SetActive(false);
        gameOverUI.SetActive(false);
        connectionUI.SetActive(true);
        
        if (NetworkManager.Singleton != null)
        {
            NetworkManager.Singleton.Shutdown();
        }
        
        ConnectionManager connectionManager = FindFirstObjectByType<ConnectionManager>();
        if (connectionManager != null)
        {
            connectionManager.CleanupLobby();
        }
    }

    // ==================== Helpers ====================
    public int GetWordCount()
    {
        return words.Count;
    }
}

[System.Serializable]
public class VideoSet
{
    public string setName;
    public List<VideoClip> videos = new List<VideoClip>();
    public List<string> words = new List<string>();
}