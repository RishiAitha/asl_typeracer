using System.Collections;
using UnityEngine;
using Unity.Netcode;

// NetworkBehaviour allows this object to exist across the network
public class CarManager : NetworkBehaviour
{
    // ==================== Inspector / Serialized Fields ====================
    // Different color car sprites
    [SerializeField] Sprite[] carSprites;

    // ==================== Networked State / Private Fields ====================
    private float distIncrement;
    private RaceManager raceManager;

    // NetworkVariables automatically sync from server to all clients
    public NetworkVariable<int> wordsCompleted = new NetworkVariable<int>(0);
    public NetworkVariable<int> spriteIndex = new NetworkVariable<int>(0);
    public NetworkVariable<int> wordSet = new NetworkVariable<int>(-1);

    // ==================== Unity / Network Lifecycle ====================
    // called when network object spawns after start
    public override void OnNetworkSpawn()
    {
        // Set up initial game state
        if (IsServer)
        {
            SetSpawnPosition(); // car spawn positions
            spriteIndex.Value = (int) OwnerClientId % 3;

            if (wordSet.Value == 0)
            {
                wordSet.Value = -1;
            }
        }

        // when sprite value changes, set a different sprite
        wordSet.OnValueChanged += OnWordSetChanged;

        // set initial sprite
        SetSprite(spriteIndex.Value);

        // cache RaceManager if available to avoid repeated lookups
        if (raceManager == null)
        {
            var rms = FindObjectsByType<RaceManager>(FindObjectsSortMode.None);
            if (rms != null && rms.Length > 0)
            {
                raceManager = rms[0];
            }
        }

        // set distance increment for cars to travel based on word set size
        if (wordSet.Value >= 0)
        {
            SetDistIncrement();
        }
    }

    public override void OnNetworkDespawn()
    {
        wordSet.OnValueChanged -= OnWordSetChanged;
    }

    // ==================== Public API / Helpers ====================
    // Set car spawn positions
    private void SetSpawnPosition()
    {
        Vector3[] spawnPositions = new Vector3[]
        {
            new Vector3(-2f, 1.15f, 0f),
            new Vector3(-2f, 0.4f, 0f),
            new Vector3(-2f, -0.35f, 0f),
        };

        transform.position = spawnPositions[(int) OwnerClientId % 3];
    }

    private void SetSprite(int index)
    {
        // Set sprite of car
        GetComponentsInChildren<SpriteRenderer>()[0].sprite = carSprites[index];
    }

    private void OnWordSetChanged(int oldValue, int newValue)
    {
        if (newValue >= 0)
        {
            SetDistIncrement();
        }
    }

    // Compute distance each car moves per correct word
    public void SetDistIncrement()
    {
        // set up distance for cars to travel as they reach finish line
        var finishObj = GameObject.Find("Finish Line");
        if (finishObj == null)
        {
            Debug.LogError("SetDistIncrement: 'Finish Line' object not found.");
            return;
        }

        Transform finishLine = finishObj.transform;
        if (raceManager == null)
        {
            var raceManagers = FindObjectsByType<RaceManager>(FindObjectsSortMode.None);
            raceManager = raceManagers[0];
        }

        int wordCount = raceManager.GetWordCount();

        // sets distance increment for car to travel on successful guess
        distIncrement = (finishLine.position.x - transform.position.x) / wordCount;
    }

    // ==================== Server RPCs / Client RPCs ====================
    // client calls server rpc, but it is executed on the server
    [ServerRpc]
    public void MoveCarServerRpc()
    {
        // Move cars server side so they move for everyone
        StartCoroutine(MoveCoroutine());
    }

    // serverrpc that anyone can call
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RestartGameServerRpc()
    {
        // server selects a new random word set different from current
        CarManager[] allCars = FindObjectsByType<CarManager>(FindObjectsSortMode.None);
        int currentSet = -1;
        if (allCars != null && allCars.Length > 0)
        {
            currentSet = allCars[0].wordSet.Value;
        }

        int newSet = Random.Range(0, 5);
        int attempts = 0;
        while (newSet == currentSet && attempts < 10)
        {
            newSet = Random.Range(0, 5);
            attempts++;
        }

        // apply new set and reset all cars
        foreach (CarManager car in allCars)
        {
            car.wordSet.Value = newSet; // set same new word set for everyone
            car.wordsCompleted.Value = 0; // reset progress
            car.SetSpawnPosition(); // reset positions
        }

        // notify clients to update UI/state
        RestartGameClientRpc();
    }

    [ClientRpc]
    private void RestartGameClientRpc()
    {
        if (raceManager == null)
        {
            var raceManagers = FindObjectsByType<RaceManager>(FindObjectsSortMode.None);
            if (raceManagers == null || raceManagers.Length == 0)
            {
                return;
            }
            raceManager = raceManagers[0];
        }

        // Apply only the client-side reset to avoid invoking server RPCs again.
        raceManager.ApplyClientRestart();
    }

    [ClientRpc]
    private void GameOverClientRpc(ulong winnerClientId)
    {
        if (raceManager == null)
        {
            var raceManagers = FindObjectsByType<RaceManager>(FindObjectsSortMode.None);
            raceManager = raceManagers[0];
        }

        raceManager.GameOver(winnerClientId);
    }

    // ==================== Movement / Coroutines ====================
    public IEnumerator MoveCoroutine()
    {
        Vector3 start = transform.position;
        // ensure distIncrement is valid
        if (float.IsInfinity(distIncrement) || float.IsNaN(distIncrement) || distIncrement == 0f)
        {
            // try to recompute for up to 2 seconds
            float tryDuration = 2f;
            float waited = 0f;
            const float step = 0.1f;
            while ((float.IsInfinity(distIncrement) || float.IsNaN(distIncrement) || distIncrement == 0f) && waited < tryDuration)
            {
                SetDistIncrement();
                yield return new WaitForSeconds(step);
                waited += step;
            }

            if (float.IsInfinity(distIncrement) || float.IsNaN(distIncrement) || distIncrement == 0f)
            {
                Debug.LogError($"Abort MoveCoroutine: distIncrement still invalid ({distIncrement}) for {gameObject.name} after waiting.");
                yield break;
            }
        }

        // move car
        Vector3 target = new Vector3(transform.position.x + distIncrement, transform.position.y, transform.position.z);
        float deltaTime = 0f;
        float totalTime = 1f;
        while (deltaTime < totalTime)
        {
            transform.position = Vector3.Lerp(start, target, deltaTime / totalTime);
            deltaTime += Time.deltaTime;
            yield return null;
        }

        transform.position = target;
        wordsCompleted.Value++;

        CheckFinished(); // check if a car finished their words
    }

    // ==================== Finish / Game Flow Helpers ====================
    private void CheckFinished()
    {
        if (!IsServer) return;

        // ensure we have a RaceManager to query word count
        if (raceManager == null)
        {
            var rms = FindObjectsByType<RaceManager>(FindObjectsSortMode.None);
            if (rms == null || rms.Length == 0)
            {
                Debug.LogWarning("CheckFinished: RaceManager not found.");
                return;
            }
            raceManager = rms[0];
        }

        int wordCount = raceManager.GetWordCount();
        if (wordCount <= 0)
        {
            Debug.LogWarning($"CheckFinished: invalid wordCount={wordCount}. Delaying finish check.");
            return;
        }

        // If this car has completed all words, announce the winner
        if (wordsCompleted.Value >= wordCount)
        {
            GameOverClientRpc(OwnerClientId);
        }
    }
}