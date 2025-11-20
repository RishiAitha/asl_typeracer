using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Authentication;

public class ConnectionManager : MonoBehaviour
{
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TMP_InputField ipInput;
    [SerializeField] private GameObject gameUI;
    [SerializeField] private GameObject connectionUI;
    private string hostCode = "";
    private bool errorDisplaying = false;
    
    private async void Start()
    {
        connectionUI.SetActive(true);
        gameUI.SetActive(false);

        try
        {
            await UnityServices.InitializeAsync();

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                Debug.Log("Signed in anonymously");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Authentication Error: {e.Message}");
            errorDisplaying = true;
            statusText.text = $"Auth Error: {e.Message}";
        }
    }
    
    private void Update()
    {
        if (NetworkManager.Singleton != null && !errorDisplaying)
        {   
            int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;
            
            if (NetworkManager.Singleton.IsHost)
            {
                statusText.text = $"Connected as Host\nJoinCode: {hostCode}\nPlayers: {playerCount}/3";
            }
            else if (NetworkManager.Singleton.IsClient)
            {
                statusText.text = $"Connected as Client";
            }
            else
            {
                statusText.text = "Not Connected";
            }
            
            if (playerCount == 3)
            {
                StartGame();
            }
        }
    }
    
    public async void StartHost()
    {
        try
        {
            Allocation allocation = await RelayService.Instance.CreateAllocationAsync(2);

            hostCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
            
            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetHostRelayData(
                allocation.RelayServer.IpV4,
                (ushort) allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData
            );

            NetworkManager.Singleton.StartHost();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Relay Error: {e.Message}");
            errorDisplaying = true;
            statusText.text = $"Error: {e.Message}";
        }
    }
    
    public async void StartClient()
    {
        try
        {
            string joinCode = ipInput.text;

            JoinAllocation allocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

            var transport = NetworkManager.Singleton.GetComponent<UnityTransport>();
            transport.SetClientRelayData(
                allocation.RelayServer.IpV4,
                (ushort) allocation.RelayServer.Port,
                allocation.AllocationIdBytes,
                allocation.Key,
                allocation.ConnectionData,
                allocation.HostConnectionData
            );

            NetworkManager.Singleton.StartClient();
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Join Error: {e.Message}");
            errorDisplaying = true;
            statusText.text = $"Error: {e.Message}";
        }
    }
    
    private void StartGame()
    {
        connectionUI.SetActive(false);
        gameUI.SetActive(true);
    }
}