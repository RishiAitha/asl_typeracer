using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.UI;
using Unity.Services.Core;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Services.Matchmaker;
using Unity.Services.Matchmaker.Models;
using Unity.Services.Lobbies;
using Unity.Services.Lobbies.Models;
using System.Collections.Generic;
using Unity.Services.Authentication;
using System.Threading.Tasks;
using System;


public class ConnectionManager : MonoBehaviour
{
    // ==================== Inspector / Serialized Fields ====================
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TMP_InputField joinCodeInput;
    [SerializeField] private GameObject gameUI;
    [SerializeField] private GameObject connectionUI;
    [SerializeField] private Button quickPlayButton;

    // ==================== Runtime State ====================
    private bool servicesReady = false;
    private bool isMatchmaking = false;
    private string hostCode = "";
    private Lobby currentLobby;
    private bool errorDisplaying = false;
    private bool gameStarted = false;
    private Coroutine heartbeatCoroutine = null;

    // ==================== Unity Lifecycle ====================
    private async void Start()
    {
        if (connectionUI != null) connectionUI.SetActive(true);
        if (gameUI != null) gameUI.SetActive(false);
        if (quickPlayButton != null) quickPlayButton.interactable = false;

        try
        {
            if (statusText != null) statusText.text = "Initializing...";
            
            string profileName = "Main";
            
            // weird profile id stuff for parrelsync and built versions
            #if UNITY_EDITOR
            string projectPath = UnityEngine.Application.dataPath;
            
            if (projectPath.Contains("_clone_"))
            {
                int cloneIndex = projectPath.IndexOf("_clone_");
                if (cloneIndex >= 0 && projectPath.Length > cloneIndex + 7)
                {
                    profileName = "Clone" + projectPath.Substring(cloneIndex + 7, 1);
                }
            }
            #else
            
            if (!PlayerPrefs.HasKey("UniqueInstanceID"))
            {
                string shortId = System.Guid.NewGuid().ToString("N").Substring(0, 12);
                PlayerPrefs.SetString("UniqueInstanceID", shortId);
                PlayerPrefs.Save();
            }
            profileName = PlayerPrefs.GetString("UniqueInstanceID");
            #endif
            
            
            var options = new InitializationOptions();
            options.SetProfile(profileName);
            await UnityServices.InitializeAsync(options);

            if (!AuthenticationService.Instance.IsSignedIn)
            {
                await AuthenticationService.Instance.SignInAnonymouslyAsync();
                
            }

            servicesReady = true;
            if (statusText != null) statusText.text = "Ready";
            if (quickPlayButton != null) quickPlayButton.interactable = true;
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Authentication Error: {e.Message}");
            errorDisplaying = true;
            if (statusText != null) statusText.text = $"Auth Error: {e.Message}";
        }
    }

    private void Update()
    {
        if (statusText == null) return;

        if (NetworkManager.Singleton != null && !errorDisplaying && servicesReady)
        {
            int playerCount = NetworkManager.Singleton.ConnectedClientsIds.Count;

            if (NetworkManager.Singleton.IsHost)
            {
                statusText.text = $"Connected as Host\nJoinCode: {hostCode}\nPlayers: {playerCount}/3";
            }
            else if (NetworkManager.Singleton.IsClient && NetworkManager.Singleton.IsConnectedClient)
            {
                statusText.text = $"Connected as Client";
            }

            if (playerCount == 3 && !gameStarted)
            {
                gameStarted = true;
                StartGame();
            }
        }
    }

    // ==================== Host / Client Startup ====================
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
            if (statusText != null) statusText.text = $"Error: {e.Message}";
        }
    }

    private async Task<string> StartHostWithRelay(int maxConnections, string connectionType)
    {
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
            string joinCode = joinCodeInput.text;
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
            if (statusText != null) statusText.text = $"Error: {e.Message}";
        }
    }

    private async Task<bool> StartClientWithRelay(string joinCode, string connectionType)
    {
        // join host relay with code
        var allocation = await RelayService.Instance.JoinAllocationAsync(joinCode: joinCode);
        
        // set up netcode transport to use relay
        NetworkManager.Singleton.GetComponent<UnityTransport>().SetRelayServerData(
            AllocationUtils.ToRelayServerData(allocation, connectionType)
        );
        
        return !string.IsNullOrEmpty(joinCode) && NetworkManager.Singleton.StartClient();
    }

    // ==================== Matchmaking ====================
    public async void StartMatchmaking()
    {
        if (!servicesReady)
        {
            statusText.text = "Services not ready, please wait...";
            return;
        }

        if (isMatchmaking)
        {
            return;
        }

        try
        {
            isMatchmaking = true;
            if (quickPlayButton != null) quickPlayButton.interactable = false;

            
            if (statusText != null) statusText.text = "Finding match...";
            var players = new List<Unity.Services.Matchmaker.Models.Player>
            {
                new (AuthenticationService.Instance.PlayerId, new Dictionary<string, object>())
            };

            // set matchmaking options
            var options = new CreateTicketOptions(
                "RacerQueue0", // The name of the queue defined in the previous step,
                new Dictionary<string, object>());

            // create ticket
            var ticketResponse = await MatchmakerService.Instance.CreateTicketAsync(players, options);


            await PollTicketStatus(ticketResponse.Id);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Matchmaking Error: {e.Message}");
            errorDisplaying = true;
            if (statusText != null) statusText.text = $"Matchmaking Error: {e.Message}";
        }
        finally
        {
            isMatchmaking = false;
            if (quickPlayButton != null) quickPlayButton.interactable = true;
        }
    }

    private async Task PollTicketStatus(string ticketId)
    {
        float timeout = 60f;
        float elapsed = 0f;

        // polling ticket status

        while (elapsed < timeout)
        {
            var ticketStatus = await MatchmakerService.Instance.GetTicketAsync(ticketId);

            if (ticketStatus == null)
            {
                Debug.LogWarning("[Matchmaking] Ticket status is null");
            }
            else
            {
                // Some queues return a full MultiplayAssignment, others a lightweight MatchIdAssignment.
                if (ticketStatus.Type == typeof(MultiplayAssignment))
                {
                    var assignment = ticketStatus.Value as MultiplayAssignment;
                    if (assignment != null && assignment.Status == MultiplayAssignment.StatusOptions.Found && !string.IsNullOrEmpty(assignment.MatchId))
                    {
                        if (statusText != null) statusText.text = "Match found! Connecting...";
                        await HandleMatchAssignment(assignment.MatchId);
                        return;
                    }
                }
                else if (ticketStatus.Type == typeof(MatchIdAssignment))
                {
                    var matchIdAssign = ticketStatus.Value as MatchIdAssignment;
                    if (!string.IsNullOrEmpty(matchIdAssign?.MatchId))
                    {
                        if (statusText != null) statusText.text = "Match found! Connecting...";
                        await HandleMatchAssignment(matchIdAssign.MatchId);
                        return;
                    }
                }
            }

            // wait and update countdown
            for (int i = 0; i < 6; i++)
            {
                if (statusText != null) statusText.text = $"Finding match... {(int)(timeout - elapsed - (i * 0.5f))}s";
                await Task.Delay(500);
            }
            elapsed += 3f;
        }

        Debug.LogWarning($"[Matchmaking] Ticket {ticketId} timed out after {timeout}s");
        if (statusText != null) statusText.text = "Matchmaking timed out. Try again?";
        try { await MatchmakerService.Instance.DeleteTicketAsync(ticketId); } catch { }
        errorDisplaying = false;
    }

    // ==================== Lobby / Match Handling ====================
     // Handle matchmaker responses that return only a match id
    private async Task HandleMatchAssignment(string matchId)
    {
        try
        {
            if (statusText != null) statusText.text = "Creating/joining lobby...";

            var createOptions = new CreateLobbyOptions
            {
                IsPrivate = false,
                Data = new Dictionary<string, DataObject>()
            };

            // create lobby if it doesn't exist, join it otherwise
            currentLobby = await LobbyService.Instance.CreateOrJoinLobbyAsync(
                lobbyId: matchId,
                lobbyName: $"Race_{matchId.Substring(0, 8)}",
                maxPlayers: 3,
                options: createOptions
            );


            // check if you are host
            bool isHost = currentLobby.HostId == AuthenticationService.Instance.PlayerId;

            if (isHost)
            {
                await SetupAsHost();
            }
            else
            {
                await SetupAsClient();
            }
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Lobby] Match assignment error: {e.Message}");
            Debug.LogError($"[Lobby] Stack trace: {e.StackTrace}");
            if (statusText != null) statusText.text = $"Connection error: {e.Message}";
            errorDisplaying = true;
        }
    }

    private async Task SetupAsHost()
    {
    if (statusText != null) statusText.text = "Setting up as host...";

        try
        {
            // get relay code
            hostCode = await StartHostWithRelay(2, "dtls");

            // share relay codes in lobby
            var updateOptions = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, hostCode) }
                }
            };

            currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, updateOptions);
            

            heartbeatCoroutine = StartCoroutine(LobbyHeartbeat());
            if (statusText != null) statusText.text = "Hosting game...";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Host setup error: {e.Message}");
            if (statusText != null) statusText.text = $"Host error: {e.Message}";
            errorDisplaying = true;
        }
    }

    private async Task SetupAsClient()
    {
        if (statusText != null) statusText.text = "Setting up as client...";

        try
        {
            // poll lobby until host shares the Relay join code
            string relayJoinCode = null;
            int maxAttempts = 30;
            int attempts = 0;

            while (string.IsNullOrEmpty(relayJoinCode) && attempts < maxAttempts)
            {
                await Task.Delay(2000);
                attempts++;

                // refresh lobby data
                currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);

                // check if host has shared the Relay join code
                if (currentLobby.Data != null && currentLobby.Data.ContainsKey("RelayJoinCode"))
                {
                    relayJoinCode = currentLobby.Data["RelayJoinCode"].Value;
                    
                }
                else
                {
                    if (statusText != null) statusText.text = $"Waiting for host... {(maxAttempts - attempts) * 2}s";
                }
            }

            if (string.IsNullOrEmpty(relayJoinCode))
            {
                throw new System.Exception("Timeout waiting for host");
            }

            // start client with relay code
            bool success = await StartClientWithRelay(relayJoinCode, "dtls");
            
            if (!success)
            {
                throw new System.Exception("Failed to connect to host");
            }
            
            if (statusText != null) statusText.text = "Connected";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Client setup error: {e.Message}");
            if (e.Message != null && e.Message.ToLower().Contains("not found"))
            {
                // lobby disappeared; make sure we clean up local state
                CleanupLobby();
                if (statusText != null) statusText.text = "Host left or lobby not found.";
            }
            else
            {
                if (statusText != null) statusText.text = $"Connection error: {e.Message}";
            }
            errorDisplaying = true;
        }
    }

    private System.Collections.IEnumerator LobbyHeartbeat()
    {
        while (currentLobby != null && NetworkManager.Singleton != null && NetworkManager.Singleton.IsHost)
        {
            try { LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id); } catch { }
            yield return new WaitForSeconds(15f);
        }
    }

    // ==================== Cleanup / Lifecycle ====================
    private void OnDestroy()
    {
        // clean up lobby when leaving
        if (currentLobby != null)
        {
            CleanupLobby();
        }
    }

    public async void CleanupLobby()
    {
        if (heartbeatCoroutine != null)
        {
            try { StopCoroutine(heartbeatCoroutine); } catch { }
            heartbeatCoroutine = null;
        }

        if (currentLobby != null)
        {
            try
            {
                if (currentLobby.HostId == AuthenticationService.Instance.PlayerId)
                {
                    await LobbyService.Instance.DeleteLobbyAsync(currentLobby.Id);
                }
                else
                {
                    await LobbyService.Instance.RemovePlayerAsync(currentLobby.Id, AuthenticationService.Instance.PlayerId);
                }
            }
            catch (System.Exception e)
            {
                // ignore lobby-not-found during cleanup, but log other issues
                if (e.Message != null && e.Message.ToLower().Contains("not found"))
                {
                    Debug.LogWarning($"CleanupLobby: lobby not found (may have been deleted): {e.Message}");
                }
                else
                {
                    Debug.LogError($"Cleanup error: {e.Message}");
                }
            }
            finally
            {
                currentLobby = null;
                gameStarted = false;
                isMatchmaking = false;
                errorDisplaying = false;
                if (statusText != null) statusText.text = "Disconnected";
                if (quickPlayButton != null) quickPlayButton.interactable = servicesReady;
            }
        }
    }

    // ==================== Game Start / UI ====================
    private void StartGame()
    {
        Debug.Log("start game");
        if (NetworkManager.Singleton.IsServer)
        {
            CarManager[] allCars = FindObjectsByType<CarManager>(FindObjectsSortMode.None);
            Debug.Log("Starting game, word set value is " + allCars[0].wordSet.Value);
            if (allCars.Length == 3 && allCars[0].wordSet.Value == -1)
            {
                int randomSet = UnityEngine.Random.Range(0, 5);
                foreach (CarManager car in allCars)
                {
                    car.wordSet.Value = randomSet;
                }
            }
        }

        if (connectionUI != null) connectionUI.SetActive(false);
        if (gameUI != null) gameUI.SetActive(true);
    }
}