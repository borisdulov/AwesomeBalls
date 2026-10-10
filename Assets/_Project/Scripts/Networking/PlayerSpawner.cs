using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using Scene = UnityEngine.SceneManagement.Scene;
using UnitySceneManager = UnityEngine.SceneManagement.SceneManager;

namespace AwesomeBalls.Networking
{
    /// <summary>
    /// Server only: spawns one player per connection once that client is in the gameplay scene,
    /// both for players moved there by the host and for friends joining mid-game.
    /// </summary>
    [RequireComponent(typeof(NetworkManager))]
    public sealed class PlayerSpawner : MonoBehaviour
    {
        [SerializeField] NetworkObject playerPrefab;

        NetworkManager networkManager;
        NetworkSession session;
        readonly Dictionary<NetworkConnection, NetworkObject> players = new();

        void Awake()
        {
            networkManager = GetComponent<NetworkManager>();
            session = GetComponent<NetworkSession>();
        }

        void Start()
        {
            if (NetworkSession.Instance != session) return;
            networkManager.SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChanged;
            networkManager.SceneManager.OnClientLoadedStartScenes += OnClientLoadedStartScenes;
            networkManager.ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            networkManager.ServerManager.OnServerConnectionState += args =>
            {
                if (args.ConnectionState == LocalConnectionState.Stopped) players.Clear();
            };
        }

        void OnClientPresenceChanged(ClientPresenceChangeEventArgs args)
        {
            // Late joiners report presence before finishing their start scenes; OnClientLoadedStartScenes spawns them.
            if (args.Added && args.Scene.name == session.GameplayScene && args.Connection.LoadedStartScenes(true))
                Spawn(args.Connection, args.Scene);
        }

        // Covers late joiners (they load the global gameplay scene on connect) and a host started
        // directly from the gameplay scene, where no scene load event happens.
        void OnClientLoadedStartScenes(NetworkConnection connection, bool asServer)
        {
            if (!asServer) return;
            Scene active = UnitySceneManager.GetActiveScene();
            if (active.name == session.GameplayScene) Spawn(connection, active);
        }

        void OnRemoteConnectionState(NetworkConnection connection, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState == RemoteConnectionState.Stopped) players.Remove(connection);
        }

        void Spawn(NetworkConnection connection, Scene scene)
        {
            if (!networkManager.IsServerStarted) return;
            if (players.TryGetValue(connection, out NetworkObject existing) && existing != null) return;

            Transform point = SpawnPoint.Pick(scene, players.Count);
            Vector3 position = point != null ? point.position : Vector3.zero;
            Quaternion rotation = point != null ? point.rotation : Quaternion.identity;
            NetworkObject player = Instantiate(playerPrefab, position, rotation);
            networkManager.ServerManager.Spawn(player, connection, scene);
            players[connection] = player;
        }
    }
}
