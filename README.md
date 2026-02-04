# ASL TypeRacer

## TL;DR
- This is a demo project showing how the fingerspelling dataset can be combined with Unity multiplayer architecture to create an interesting learning game.
- It uses Unity NetCode for GameObjects, Matchmaker, and Relay to connect players.
- Players can guess words based on fingerspelling videos (taken from the training set) to learn words.
- If anything isn't working for you (this is relatively likely), please please please contact me at rishi.aitha@gmail.com!
- Also, the project is currently registered under my Unity account for matchmaker. If you'd like to have more control, refer to the matchmaker setup specified below.

## Resources
- Unity version: 6000.0.48f1
- Packages used: Netcode for GameObjects, Unity Multiplayer Services (Relay, Matchmaker, Lobbies, Authentication), TextMeshPro, and ParrelSync (for testing)
- To test this project:
    - In editor, use ParrelSync clones to test multiple players (this is easiest).
    - For builds, if they are run on the same computer they will have the same player ID, so quick play won't work.
        - You can still manually connect on three builds on the same computer though.
- Useful links:
  - Netcode for GameObjects: https://docs-multiplayer.unity3d.com
  - Matchmaker: https://docs.unity.com/en-us/matchmaker
  - Relay: https://docs.unity.com/en-us/relay/

## Code Reference

### ConnectionManager
  - [Assets/Scripts/ConnectionManager.cs](Assets/Scripts/ConnectionManager.cs)
  - Handles initial matchmaker/relay connection.
    - Provides two options: quick play, which automatically creates games of three with matchmaker and relay services, and manual connection, which uses join codes to connect through relay alone.
  - Instance variables
    - `statusText` : TextMeshProUGUI
      - displays current connection status
    - `joinCodeInput` : TMP_InputField
      - takes join code input for manual connection
    - `gameUI` : GameObject
      - gameplay UI holder
    - `connectionUI` : GameObject
      - connection UI holder
    - `quickPlayButton` : Button
      - button to start matchmaking
    - `servicesReady` : bool
      - checks if Unity services are ready for matchmaking/relay
    - `isMatchmaking` : bool
      - if client is currently in matchmaking process
    - `hostCode` : string
      - code used to host game through relay
    - `currentLobby` : Lobby
      - lobby joined via matchmaking
    - `errorDisplaying` : bool
      - if an error is displaying to user
    - `gameStarted` : bool
      - if game has started
  - Methods
    - `StartHost()`
      - start hosting via relay
    - `StartHostWithRelay(int maxConnections, string connectionType)`
      - set up relay allocations and get join code
    - `StartClient()`
      - start client game with given join code
    - `StartClientWithRelay(string joinCode, string connectionType)`
      - use code to join relay service
    - `StartMatchmaking()`
      - send ticket to matchmaker to enter queue and find lobby
    - `PollTicketStatus(string ticketId)`
      - check for updates on matchmaking progress
    - `HandleMatchAssignment(string matchId)`
      - given match id, join lobby and start setup
    - `SetupAsHost()`
      - get relay code when hosting lobby and start lobby heartbeat
    - `SetupAsClient()`
      - use join code to join existing lobby after finding match
    - `LobbyHeartbeat()`
      - send heartbeat to keep lobby running
    - `OnDestroy()`
      - start lobby cleanup
    - `CleanupLobby()`
      - delete lobby and remove lobby players
    - `StartGame()`
      - start game and set up cars and word set

### CarManager
  - [Assets/Scripts/CarManager.cs](Assets/Scripts/CarManager.cs)
  - Runs on each player's car object, managing movement, scoring, and winning. Deals heavily with networked variables and objects.
  - Instance variables
    - `carSprites` : Sprite[]
      - set of three sprites for cars
    - `distIncrement` : float
      - distance car needs to move after solving a word; based on total words in set
    - `raceManager` : RaceManager
      - race manager
    - `wordsCompleted` : NetworkVariable<int>
      - number of words completed for this car
    - `spriteIndex` : NetworkVariable<int>
      - this car's index in list of sprites
    - `wordSet` : NetworkVariable<int>
      - current chosen word set for the game; five currently available
  - Methods
    - `OnNetworkSpawn()`
      - runs when car joins game, sets up word set, sprite, and distance increment
    - `OnNetworkDespawn()`
      - clears up car function setup when despawning
    - `SetSpawnPosition()`
      - sets initial positions of player cars
    - `SetSprite(int index)`
      - sets sprite of car based on current index
    - `OnWordSetChanged(int oldValue, int newValue)`
      - handles change in word set to update dist increment
    - `SetDistIncrement()`
      - sets distance increment based on current word set
    - `MoveCarServerRpc()`
      - moves car after solving a word for all clients
    - `RestartGameServerRpc()`
      - restarts game on server side by resetting cars and word set
    - `RestartGameClientRpc()`
      - restarts game client side by resetting race manager
    - `GameOverClientRpc(ulong winnerClientId)`
      - ends game when one client wins and displays game over screen
    - `MoveCoroutine()`
      - moves car by distance increment
    - `CheckFinished()`
      - check if current player is finished their word set

### RaceManager
  - [Assets/Scripts/RaceManager.cs](Assets/Scripts/RaceManager.cs)
  - Handles overall race logic, primarily on server client. Manages word set selection and shared logic.
  - Instance variables
    - `videoSetsData` : List<VideoSet>
      - organized info for all video sets
    - `words` : List<string>
      - current list of words in race
    - `videos` : List<VideoClip>
      - current list of video clips for race
    - `typingManager` : TypingManager
      - typing input manager
    - `videoPlayer` : VideoPlayer
      - video player for fingerspelling videos
    - `connectionUI` : GameObject
      - connection user interface object
    - `gameUI` : GameObject
      - game user interface object
    - `gameOverUI` : GameObject
      - game over user interface object
    - `gameOverText` : TextMeshProUGUI
      - text displaying game over and winning player
    - `myCar` : CarManager
      - this client's car object
  - Methods
    - `Start()`
      - sets up race, finding this client's car and loading videos
    - `LoadVideosWhenReady()`
      - loads videos once car is found based on current word set
    - `FindLocalCar()`
      - finds current player's car
    - `SubmitWord(string word)`
      - takes submitted word and checks if it is correct and moves car when needed
    - `WaitForMoveAnimation()`
      - delays for car move animation to finish
    - `FailDelay()`
      - adds delay when submitting incorrect guess
    - `GameOver(ulong winnerClientId)`
      - displays game over ui and winning player
    - `Restart()`
      - restarts game and resets ui
    - `ApplyClientRestart()`
      - restarts on client side and displays game ui
    - `MainMenu()`
      - displays main menu
    - `DisconnectMainMenu()`
      - disconnects and goes back to main menu and cleans up lobby
    - `GetWordCount()`
      - gets current race word count

### TypingManager
  - [Assets/Scripts/TypingManager.cs](Assets/Scripts/TypingManager.cs)
  - Handles keyboard input with keyboard button package. Sends relevant input information to RaceManager.
  - Instance variables
    - `textBox` : TextMeshProUGUI
      - typing display
    - `raceManager` : RaceManager
      - race manager
    - `editable` : bool
      - checks if typing text is currently allowed to account for failing and success delays
  - Methods
    - `AddLetter(string letter)`
      - adds inputted letter to text box
    - `DeleteLetter()`
      - deletes last inputted letter from text box
    - `SubmitWord()`
      - submits typed word to race manager
    - `Reset()`
      - resets text box

## Matchmaker Setup
- Queue: RacerQueue0
    - Max players on a ticket: 3
    - Pools: 1
        - RacerPool0
            - Hosting Type: Client Hosting

Pool JSON:

{
  "Name": "ASL TypeRacer Race",
  "MatchDefinition": {
    "Teams": [
      {
        "Name": "Players",
        "TeamCount": {
          "Min": 1,
          "Max": 1
        },
        "PlayerCount": {
          "Min": 3,
          "Max": 3
        }
      }
    ],
    "MatchRules": []
  },
  "BackfillEnabled": false
}


-----

Contact me at rishi.aitha@gmail.com
