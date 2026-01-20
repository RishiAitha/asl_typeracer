using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Authentication;
using System.Threading.Tasks;

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
            // start unity services for relay
            await UnityServices.InitializeAsync();

            // authenticate to unity relay services
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
            hostCode = await StartHostWithRelay(2, "dtls");
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Relay Error: {e.Message}");
            errorDisplaying = true;
            statusText.text = $"Error: {e.Message}";
        }
    }

    private async Task<string> StartHostWithRelay(int maxConnections, string connectionType)
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }
        
        // create two relay allocations for other players
        var allocation = await RelayService.Instance.CreateAllocationAsync(maxConnections);
        
        // set up netcode transport for relay
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, connectionType)
        );
        
        // get join code
        var joinCode = await RelayService.Instance.GetJoinCodeAsync(allocation.AllocationId);
        
        return NetworkManager.Singleton.StartHost() ? joinCode : null;
    }
    
    public async void StartClient()
    {
        try
        {
            string joinCode = ipInput.text;
            bool success = await StartClientWithRelay(joinCode, "dtls");
            
            if (!success)
            {
                throw new System.Exception("Failed to join game");
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Join Error: {e.Message}");
            errorDisplaying = true;
            statusText.text = $"Error: {e.Message}";
        }
    }

    private async Task<bool> StartClientWithRelay(string joinCode, string connectionType)
    {
        await UnityServices.InitializeAsync();
        if (!AuthenticationService.Instance.IsSignedIn)
        {
            await AuthenticationService.Instance.SignInAnonymouslyAsync();
        }

        // join host relay with code
        var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);
        
        // set up netcode transport to use relay
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, connectionType)
        );
        
        return !string.IsNullOrEmpty(joinCode) && NetworkManager.Singleton.StartClient();
    }
    
    private void StartGame()
    {
        connectionUI.SetActive(false);
        gameUI.SetActive(true);
    }
}