using TMPro;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
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
    [SerializeField] private TextMeshProUGUI statusText;
    [SerializeField] private TMP_InputField ipInput;
    [SerializeField] private GameObject gameUI;
    [SerializeField] private GameObject connectionUI;
    private string hostCode = "";
    private Lobby currentLobby;
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

    public async void StartMatchmaking()
    {
        try
        {
            statusText.text = "Finding match...";
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

            Debug.Log(ticketResponse.Id);

            await PollTicketStatus(ticketResponse.Id);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Matchmaking Error: {e.Message}");
            errorDisplaying = true;
            statusText.text = $"Matchmaking Error: {e.Message}";
        }
    }

    private async Task PollTicketStatus(string ticketId)
    {
        MultiplayAssignment assignment = null;
        bool gotAssignment = false;
        float timeout = 60f;
        float elapsed = 0f;

        do
        {
            // rate limit delay
            await Task.Delay(TimeSpan.FromSeconds(1f));
            elapsed += 1f;

            // check for timeout
            if (elapsed >= timeout)
            {
                statusText.text = "Matchmaking timed out. Try again?";
                await MatchmakerService.Instance.DeleteTicketAsync(ticketId);
                errorDisplaying = false;
                return;
            }

            // poll ticket
            var ticketStatus = await MatchmakerService.Instance.GetTicketAsync(ticketId);
            if (ticketStatus == null)
            {
                continue;
            }

            statusText.text = $"Finding match... {(int) (timeout - elapsed)}s";

            if (ticketStatus.Type == typeof(MultiplayAssignment))
            {
                assignment = ticketStatus.Value as MultiplayAssignment;
            }

            switch (assignment?.Status)
            {
                case MultiplayAssignment.StatusOptions.Found:
                    gotAssignment = true;
                    Debug.Log("Match Found");
                    statusText.text = "Match Found";

                    await HandleMatchAssignment(assignment);
                    break;
                case MultiplayAssignment.StatusOptions.InProgress:
                    break;
                case MultiplayAssignment.StatusOptions.Failed:
                    gotAssignment = true;
                    Debug.LogError("Failed to get ticket status. Error: " + assignment.Message);
                    statusText.text = $"Matchmaking failed: {assignment.Message}";
                    errorDisplaying = true;
                    break;
                case MultiplayAssignment.StatusOptions.Timeout:
                    gotAssignment = true;
                    Debug.LogError("Failed to get ticket status. Ticket timed out.");
                    statusText.text = "Matchmaking timed out. Try again?";
                    errorDisplaying = false;
                    break;
                default:
                    throw new InvalidOperationException();
            }
        } while (!gotAssignment);
    }

    private async Task HandleMatchAssignment(MultiplayAssignment assignment) {
        try
        {
            // get match ID from assignment
            string matchId = assignment.MatchId;
            Debug.Log($"Match ID: {matchId}");

            statusText.text = "Joining lobby...";

            var joinRequest = new JoinLobbyByIdOptions {};

            try
            {
                // join lobby if it exists
                currentLobby = await LobbyService.Instance.JoinLobbyByIdAsync(matchId, joinRequest);
                Debug.Log($"Joined lobby: {currentLobby.Id}");
            }
            catch
            {
                // create a lobby if it doesn't exist already
                var createRequest = new CreateLobbyOptions
                {
                    IsPrivate = false,
                    Data = new Dictionary<string, DataObject>()
                };

                currentLobby = await LobbyService.Instance.CreateLobbyAsync(
                    lobbyName: $"Match_{matchId}",
                    maxPlayers: 3,
                    createRequest
                );
                Debug.Log($"Created lobby: {currentLobby.Id}");
            }

            if (currentLobby.HostId == AuthenticationService.Instance.PlayerId)
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
            Debug.LogError($"Match assignment error: {e.Message}");
            statusText.text = $"Connection error: {e.Message}";
            errorDisplaying = true;
        }
    }

    private async Task SetupAsHost()
    {
        Debug.Log("Setting up host");
        statusText.text = "Setting up as host...";

        try
        {
            // get relay code
            hostCode = await StartHostWithRelay(2, "dtls");
            Debug.Log($"Relay join code: {hostCode}");

            // share relay codes in lobby
            var updateOptions = new UpdateLobbyOptions
            {
                Data = new Dictionary<string, DataObject>
                {
                    { "RelayJoinCode", new DataObject(DataObject.VisibilityOptions.Member, hostCode) }
                }
            };

            currentLobby = await LobbyService.Instance.UpdateLobbyAsync(currentLobby.Id, updateOptions);
            Debug.Log("Shared relay join code in lobby");

            StartCoroutine(LobbyHeartbeat());
            statusText.text = "Hosting game...";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Host setup error: {e.Message}");
            statusText.text = $"Host error: {e.Message}";
            errorDisplaying = true;
        }
    }

    private async Task SetupAsClient()
    {
        Debug.Log("Setting up client");
        statusText.text = "Setting up as client...";

        try
        {
            // poll lobby until host shares the Relay join code
            string relayJoinCode = null;
            int maxAttempts = 30;
            int attempts = 0;

            while (string.IsNullOrEmpty(relayJoinCode) && attempts < maxAttempts)
            {
                await Task.Delay(1000);
                attempts++;

                // refresh lobby data
                currentLobby = await LobbyService.Instance.GetLobbyAsync(currentLobby.Id);

                // check if host has shared the Relay join code
                if (currentLobby.Data != null && currentLobby.Data.ContainsKey("RelayJoinCode"))
                {
                    relayJoinCode = currentLobby.Data["RelayJoinCode"].Value;
                    Debug.Log($"Got Relay join code: {relayJoinCode}");
                }
                else
                {
                    statusText.text = $"Waiting for host... {maxAttempts - attempts}s";
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
            
            statusText.text = "Connected";
        }
        catch (System.Exception e)
        {
            Debug.LogError($"Client setup error: {e.Message}");
            statusText.text = $"Connection error: {e.Message}";
            errorDisplaying = true;
        }
    }

    private System.Collections.IEnumerator LobbyHeartbeat()
    {
        while (currentLobby != null && NetworkManager.Singleton.IsHost)
        {
            LobbyService.Instance.SendHeartbeatPingAsync(currentLobby.Id);
            yield return new WaitForSeconds(15f);
        }
    }
    
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
                currentLobby = null;
            }
            catch (System.Exception e)
            {
                Debug.LogError($"Cleanup error: {e.Message}");
            }
        }
    }

    private void StartGame()
    {
        connectionUI.SetActive(false);
        gameUI.SetActive(true);
    }
}