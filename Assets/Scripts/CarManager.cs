using System.Collections;
using UnityEngine;
using Unity.Netcode;

// NetworkBehaviour allows this object to exist across the network
public class CarManager : NetworkBehaviour
{
    private float distIncrement;
    private RaceManager raceManager;
    // NetworkVariables automatically sync from server to all clients
    public NetworkVariable<int> wordsCompleted = new NetworkVariable<int>(0);
    public NetworkVariable<int> spriteIndex = new NetworkVariable<int>(0);
    public NetworkVariable<int> wordSet = new NetworkVariable<int>(0);

    [SerializeField] Sprite[] carSprites;
    // called when network object spawns after start
    public override void OnNetworkSpawn()
    {
        // server side logic
        // owner client ID is which player owns this specific car
        if (IsServer && OwnerClientId == 0)
        {
            wordSet.Value = Random.Range(0, 5);
        }

        if (IsServer)
        {
            SetSpawnPosition();
            spriteIndex.Value = (int) OwnerClientId % 3;
        }

        // when sprite value changes, set a different sprite
        spriteIndex.OnValueChanged += OnSpriteChanged;

        // set initial sprite
        SetSprite(spriteIndex.Value);
        SetDistIncrement();
    }

    public void SetDistIncrement()
    {
        Transform finishLine = GameObject.Find("Finish Line").transform;
        RaceManager raceManager = FindObjectsByType<RaceManager>(FindObjectsSortMode.None)[0];
        distIncrement = (finishLine.position.x - transform.position.x) / 5;
    }

    public override void OnNetworkDespawn()
    {
        spriteIndex.OnValueChanged -= OnSpriteChanged;
    }

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

    private void OnSpriteChanged(int oldValue, int newValue)
    {
        SetSprite(newValue);
    }

    private void SetSprite(int index)
    {
        GetComponentsInChildren<SpriteRenderer>()[0].sprite = carSprites[index];
    }

    // client calls server rpc, but it is executed on the server
    [ServerRpc]
    public void MoveCarServerRpc()
    {
        StartCoroutine(MoveCoroutine());
    }

    public IEnumerator MoveCoroutine()
    {
        Vector3 start = transform.position;
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

        CheckFinishLine();
    }

    private void CheckFinishLine()
    {
        if (!IsServer) return;
        Transform finishLine = GameObject.Find("Finish Line").transform;
        if (transform.position.x >= finishLine.position.x)
        {
            GameOverClientRpc(OwnerClientId);
        }
    }

    // server calls clientrpc and it gets run on all clients
    [ClientRpc]
    private void GameOverClientRpc(ulong winnerClientId)
    {
        RaceManager raceManager = FindObjectsByType<RaceManager>(FindObjectsSortMode.None)[0];
        raceManager.GameOver(winnerClientId);
    }

    // serverrpc that anyone can call
    [Rpc(SendTo.Server, InvokePermission = RpcInvokePermission.Everyone)]
    public void RestartGameServerRpc()
    {
        // server selects new random word set
        wordSet.Value = Random.Range(0, 5);
        // all cars get reset
        CarManager[] allCars = FindObjectsByType<CarManager>(FindObjectsSortMode.None);
        foreach (CarManager car in allCars)
        {
            car.wordsCompleted.Value = 0; // reset all client words completed values
            car.SetSpawnPosition(); // sets car spawn positions back to 0
        }

        // reset client side
        RestartGameClientRpc();
    }

    [ClientRpc]
    private void RestartGameClientRpc()
    {
        RaceManager raceManager = FindObjectsByType<RaceManager>(FindObjectsSortMode.None)[0];
        raceManager.Restart();
    }
}