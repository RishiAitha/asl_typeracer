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

    private void OnEnable()
    {
        StartCoroutine(WatchNetworkManager());
    }

    private void OnDisable()
    {
        // try to unsubscribe from NetworkManager callbacks if possible
        if (NetworkManager.Singleton != null)
        {
            try
            {
                NetworkManager.Singleton.OnClientConnectedCallback -= OnClientConnected;
                NetworkManager.Singleton.OnClientDisconnectCallback -= OnClientDisconnected;
            }
            catch { }
        }
    }

    private System.Collections.IEnumerator WatchNetworkManager()
    {
        // wait until NetworkManager exists, then subscribe to connect/disconnect
        while (NetworkManager.Singleton == null)
        {
            yield return null;
        }

        NetworkManager.Singleton.OnClientConnectedCallback += OnClientConnected;
        NetworkManager.Singleton.OnClientDisconnectCallback += OnClientDisconnected;
    }

    // ==================== Coroutines / Loading ====================
    private IEnumerator LoadVideosWhenReady()
    {
        // wait until local car exists and has a wordSet assigned
        while (myCar == null || myCar.wordSet.Value < 0)
        {
            yield return new WaitForSeconds(0.1f);
        }

        // load initial set for current value
        LoadVideoSet(myCar.wordSet.Value);
    }

    // load videos/words for given word set index and notify cars to recompute distIncrement
    private void LoadVideoSet(int setIndex)
    {
        if (setIndex < 0 || setIndex >= videoSetsData.Count)
        {
            Debug.LogWarning($"LoadVideoSet: invalid setIndex={setIndex}");
            return;
        }

        VideoSet selectedSet = videoSetsData[setIndex];
        videos = new List<VideoClip>(selectedSet.videos);
        words = new List<string>(selectedSet.words);

        // clamp wordsCompleted and update clip
        if (myCar != null)
        {
            int idx = myCar.wordsCompleted.Value;
            if (videos == null || videos.Count == 0)
            {
                videoPlayer.clip = null;
            }
            else
            {
                if (idx < 0) idx = 0;
                if (idx >= videos.Count) idx = videos.Count - 1;
                videoPlayer.clip = videos[idx];
            }
        }

        // ensure all cars recompute their movement increment now that word count changed
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
                    // subscribe to wordSet changes so clients reload videos when server picks a new set on restart
                    myCar.wordSet.OnValueChanged += OnWordSetValueChanged;

                    // if a set is already assigned, load it immediately
                    if (myCar.wordSet.Value >= 0)
                    {
                        LoadVideoSet(myCar.wordSet.Value);
                    }

                    yield break;
                    }
            }
            
            yield return new WaitForSeconds(0.2f);
        }
    }

    private void OnClientConnected(ulong clientId)
    {
        // when the local client connects, restart the local car lookup and loading flow
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        // clear any previous state and restart coroutines
        ClearLocalState();
        StartCoroutine(FindLocalCar());
        StartCoroutine(LoadVideosWhenReady());
    }

    private void OnClientDisconnected(ulong clientId)
    {
        if (NetworkManager.Singleton == null) return;
        if (clientId != NetworkManager.Singleton.LocalClientId) return;

        // clear local references so UI/state can reset cleanly
        ClearLocalState();
    }

    private void ClearLocalState()
    {
        if (myCar != null)
        {
            try
            {
                myCar.wordsCompleted.OnValueChanged -= OnWordsCompletedChanged;
                myCar.wordSet.OnValueChanged -= OnWordSetValueChanged;
            }
            catch { }
        }

        myCar = null;
        videos = null;
        words = null;
        if (videoPlayer != null) videoPlayer.clip = null;
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

    private void OnWordSetValueChanged(int oldValue, int newValue)
    {
        // reload videos/words when authoritative wordSet changes
        LoadVideoSet(newValue);
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
        // reload clip to match current wordsCompleted/state. If videos were reloaded
        // by the wordSet change subscription, this will be correct; otherwise, clear clip.
        if (videos != null && videos.Count > 0)
        {
            int idx = 0;
            if (myCar != null)
            {
                idx = myCar.wordsCompleted.Value;
                if (idx < 0) idx = 0;
                if (idx >= videos.Count) idx = videos.Count - 1;
            }
            videoPlayer.clip = videos[idx];
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

    private void OnDestroy()
    {
        if (myCar != null)
        {
            myCar.wordsCompleted.OnValueChanged -= OnWordsCompletedChanged;
            myCar.wordSet.OnValueChanged -= OnWordSetValueChanged;
        }
    }

    // ==================== Helpers ====================
    public int GetWordCount()
    {
        return (words == null) ? 0 : words.Count;
    }

    public int GetVideoSetCount()
    {
        return (videoSetsData == null) ? 0 : videoSetsData.Count;
    }
}

[System.Serializable]
public class VideoSet
{
    public string setName;
    public List<VideoClip> videos = new List<VideoClip>();
    public List<string> words = new List<string>();
}