using System;
using FishNet;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using FishNet.Transporting.Multipass;
using FishNet.Transporting.Tugboat;
using UnityEngine;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace AwesomeBalls.Networking
{
    public enum SessionMode
    {
        None,
        Steam,
        Local,
    }

    /// <summary>
    /// Entry point for starting and leaving a match. Host-client topology (GDD 9.1): Steam P2P through
    /// FishySteamworks, or Tugboat on localhost for testing two instances on one machine.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public sealed class NetworkSession : MonoBehaviour
    {
        const string LocalAddress = "localhost";

        [SerializeField] string menuScene = "MainMenu";
        [Tooltip("Free-play sandbox scene (movement testing, no rounds).")]
        [SerializeField] string gameplayScene = "Playground";
        [Tooltip("Global network object that runs the match (rounds, cards, arenas).")]
        [SerializeField] NetworkObject matchPrefab;

        NetworkManager networkManager;
        Multipass multipass;
        FishySteamworks.FishySteamworks steamTransport;
        Tugboat localTransport;
        bool leaving;
        Action autoStart;

        public static NetworkSession Instance { get; private set; }

        public SteamLobby Lobby { get; private set; }
        public SessionMode Mode { get; private set; }
        public bool IsHost { get; private set; }
        public bool IsActive => Mode != SessionMode.None;
        public bool IsConnected => networkManager.IsClientStarted;
        public bool InGame => UnitySceneManager.GetActiveScene().name != menuScene;
        /// <summary>Set by in-game UI (card pick) that needs the cursor; the pause menu stays hidden.</summary>
        public static bool UiOwnsCursor { get; set; }
        public int ConnectedPlayers => networkManager.IsServerStarted ? networkManager.ServerManager.Clients.Count : 0;
        public string GameplayScene => gameplayScene;

        public event Action Changed;

        void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            multipass = GetComponent<Multipass>();
            steamTransport = GetComponent<FishySteamworks.FishySteamworks>();
            localTransport = GetComponent<Tugboat>();
            Lobby = GetComponent<SteamLobby>();

            // Reloading the menu scene creates a duplicate NetworkManager that FishNet destroys.
            if (Instance == null) Instance = this;
        }

        void Start()
        {
            if (Instance != this) return;
            networkManager.ClientManager.OnClientConnectionState += OnClientConnectionState;
            networkManager.ServerManager.OnRemoteConnectionState += (_, _) => Changed?.Invoke();

            // Dev shortcuts for testing two copies on one machine:
            // "-local-host [-start | -sandbox]" (host and start a match / the free-play sandbox), "-local-join".
            string[] args = Environment.GetCommandLineArgs();
            if (Array.IndexOf(args, "-local-host") >= 0)
            {
                if (Array.IndexOf(args, "-start") >= 0) autoStart = StartGame;
                else if (Array.IndexOf(args, "-sandbox") >= 0) autoStart = StartFreePlay;
                if (autoStart != null) networkManager.ServerManager.OnServerConnectionState += StartGameOnceStarted;
                StartLocalHost();
            }
            else if (Array.IndexOf(args, "-local-join") >= 0)
            {
                JoinLocal();
            }
        }

        void StartGameOnceStarted(ServerConnectionStateArgs args)
        {
            if (args.ConnectionState != LocalConnectionState.Started) return;
            networkManager.ServerManager.OnServerConnectionState -= StartGameOnceStarted;
            autoStart();
        }

        void OnDestroy()
        {
            if (Instance == this) Instance = null;
        }

        public void StartSteamHost()
        {
            Mode = SessionMode.Steam;
            IsHost = true;
            multipass.SetClientTransport(steamTransport);
            multipass.StartConnection(true, IndexOf(steamTransport));
            networkManager.ClientManager.StartConnection();
            Changed?.Invoke();
        }

        public void StartSteamClient(ulong hostSteamId)
        {
            Mode = SessionMode.Steam;
            IsHost = false;
            multipass.SetClientTransport(steamTransport);
            steamTransport.SetClientAddress(hostSteamId.ToString());
            networkManager.ClientManager.StartConnection();
            Changed?.Invoke();
        }

        public void StartLocalHost()
        {
            Mode = SessionMode.Local;
            IsHost = true;
            multipass.SetClientTransport(localTransport);
            multipass.StartConnection(true, IndexOf(localTransport));
            localTransport.SetClientAddress(LocalAddress);
            networkManager.ClientManager.StartConnection();
            Changed?.Invoke();
        }

        public void JoinLocal()
        {
            Mode = SessionMode.Local;
            IsHost = false;
            multipass.SetClientTransport(localTransport);
            localTransport.SetClientAddress(LocalAddress);
            networkManager.ClientManager.StartConnection();
            Changed?.Invoke();
        }

        /// <summary>Host only: starts a match (GDD 4). The match object loads arenas round by round.</summary>
        public void StartGame()
        {
            if (!IsHost || !networkManager.IsServerStarted) return;
            NetworkObject match = Instantiate(matchPrefab);
            // FishNet moves global objects to DontDestroyOnLoad in Start(), but the match loads its
            // first arena in the same frame and would be unloaded together with the menu scene.
            DontDestroyOnLoad(match.gameObject);
            networkManager.ServerManager.Spawn(match);
        }

        /// <summary>Host only: everyone (including later joiners) goes to the free-play sandbox.</summary>
        public void StartFreePlay()
        {
            if (!IsHost || !networkManager.IsServerStarted) return;
            var load = new SceneLoadData(gameplayScene) { ReplaceScenes = ReplaceOption.All };
            networkManager.SceneManager.LoadGlobalScenes(load);
        }

        public void Leave(bool loadMenu = true)
        {
            leaving = true;
            if (networkManager.IsServerStarted) networkManager.ServerManager.StopConnection(true);
            if (networkManager.IsClientStarted) networkManager.ClientManager.StopConnection();
            Lobby.Leave();
            Mode = SessionMode.None;
            IsHost = false;
            UiOwnsCursor = false;
            leaving = false;
            Changed?.Invoke();

            if (loadMenu && UnitySceneManager.GetActiveScene().name != menuScene)
                UnitySceneManager.LoadScene(menuScene);
        }

        void OnClientConnectionState(ClientConnectionStateArgs args)
        {
            // Lost the host (it quit or the connection failed): back to the menu.
            if (args.ConnectionState == LocalConnectionState.Stopped && !leaving && IsActive)
            {
                Debug.Log("[Session] Disconnected from host.");
                Leave();
                return;
            }
            Changed?.Invoke();
        }

        int IndexOf(Transport transport)
        {
            for (int i = 0; i < multipass.Transports.Count; i++)
                if (multipass.Transports[i] == transport) return i;
            throw new InvalidOperationException($"{transport.GetType().Name} is not in the Multipass transport list.");
        }

        public static NetworkSession FindOrNull() => Instance != null ? Instance : InstanceFinder.NetworkManager?.GetComponent<NetworkSession>();
    }
}
