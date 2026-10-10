using System;
using System.Collections.Generic;
using Steamworks;
using UnityEngine;

namespace AwesomeBalls.Networking
{
    /// <summary>
    /// Friends-only Steam lobby (GDD 9.2): create, join from an invite / friends list / launch
    /// argument, list members, leave. The lobby only carries the host's SteamID; the game
    /// connection itself goes through FishNet + FishySteamworks.
    /// </summary>
    public sealed class SteamLobby : MonoBehaviour
    {
        const string HostKey = "host_steam_id";
        const string ConnectArg = "+connect_lobby";

        [SerializeField] int maxPlayers = 4;

        NetworkSession session;
        CallResult<LobbyCreated_t> lobbyCreated;
        Callback<LobbyEnter_t> lobbyEntered;
        Callback<GameLobbyJoinRequested_t> joinRequested;
        Callback<GameRichPresenceJoinRequested_t> richPresenceJoinRequested;
        Callback<LobbyChatUpdate_t> chatUpdated;
        CSteamID lobbyId = CSteamID.Nil;
        bool creating;

        public bool Available => SteamBootstrap.Initialized;
        public bool InLobby => lobbyId != CSteamID.Nil;
        public bool IsBusy => creating;
        public string LocalName => Available ? SteamFriends.GetPersonaName() : "Player";

        /// <summary>Raised when the lobby is entered, left or its members change.</summary>
        public event Action Changed;
        public event Action<string> Failed;

        void Awake() => session = GetComponent<NetworkSession>();

        void Start()
        {
            if (!Available) return;

            lobbyCreated = CallResult<LobbyCreated_t>.Create(OnLobbyCreated);
            lobbyEntered = Callback<LobbyEnter_t>.Create(OnLobbyEntered);
            joinRequested = Callback<GameLobbyJoinRequested_t>.Create(r => Join(r.m_steamIDLobby));
            richPresenceJoinRequested = Callback<GameRichPresenceJoinRequested_t>.Create(r => TryJoinFromConnectString(r.m_rgchConnect));
            chatUpdated = Callback<LobbyChatUpdate_t>.Create(_ => Changed?.Invoke());

            // Launched by accepting an invite while the game was closed.
            TryJoinFromConnectString(string.Join(" ", Environment.GetCommandLineArgs()));
        }

        public void Create()
        {
            if (!Available || InLobby || creating) return;
            creating = true;
            lobbyCreated.Set(SteamMatchmaking.CreateLobby(ELobbyType.k_ELobbyTypeFriendsOnly, maxPlayers));
        }

        public void Join(CSteamID id)
        {
            if (!Available || id == lobbyId) return;
            if (InLobby || session.IsActive) session.Leave(loadMenu: false);
            SteamMatchmaking.JoinLobby(id);
        }

        public void Leave()
        {
            if (!InLobby) return;
            SteamMatchmaking.LeaveLobby(lobbyId);
            SteamFriends.ClearRichPresence();
            lobbyId = CSteamID.Nil;
            Changed?.Invoke();
        }

        public void OpenInviteOverlay()
        {
            if (InLobby) SteamFriends.ActivateGameOverlayInviteDialog(lobbyId);
        }

        public List<string> GetMemberNames()
        {
            var names = new List<string>();
            if (!InLobby) return names;
            int count = SteamMatchmaking.GetNumLobbyMembers(lobbyId);
            CSteamID owner = SteamMatchmaking.GetLobbyOwner(lobbyId);
            for (int i = 0; i < count; i++)
            {
                CSteamID member = SteamMatchmaking.GetLobbyMemberByIndex(lobbyId, i);
                string name = SteamFriends.GetFriendPersonaName(member);
                names.Add(member == owner ? $"{name}  (хост)" : name);
            }
            return names;
        }

        void OnLobbyCreated(LobbyCreated_t result, bool ioFailure)
        {
            creating = false;
            if (ioFailure || result.m_eResult != EResult.k_EResultOK)
            {
                Failed?.Invoke($"Не удалось создать лобби ({result.m_eResult}).");
                return;
            }

            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            SteamMatchmaking.SetLobbyData(lobbyId, HostKey, SteamUser.GetSteamID().m_SteamID.ToString());
            SteamFriends.SetRichPresence("connect", $"{ConnectArg} {lobbyId.m_SteamID}");
            session.StartSteamHost();
            Changed?.Invoke();
        }

        void OnLobbyEntered(LobbyEnter_t result)
        {
            if ((EChatRoomEnterResponse)result.m_EChatRoomEnterResponse != EChatRoomEnterResponse.k_EChatRoomEnterResponseSuccess)
            {
                Failed?.Invoke("Не удалось войти в лобби.");
                return;
            }

            lobbyId = new CSteamID(result.m_ulSteamIDLobby);
            SteamFriends.SetRichPresence("connect", $"{ConnectArg} {lobbyId.m_SteamID}");

            // The host also receives LobbyEnter for its own lobby; it is already running the server.
            if (!session.IsHost)
            {
                string host = SteamMatchmaking.GetLobbyData(lobbyId, HostKey);
                if (ulong.TryParse(host, out ulong hostId)) session.StartSteamClient(hostId);
                else Failed?.Invoke("В лобби нет адреса хоста.");
            }
            Changed?.Invoke();
        }

        void TryJoinFromConnectString(string text)
        {
            if (string.IsNullOrEmpty(text)) return;
            int index = text.IndexOf(ConnectArg, StringComparison.Ordinal);
            if (index < 0) return;
            string rest = text.Substring(index + ConnectArg.Length).Trim();
            string idText = rest.Split(' ')[0];
            if (ulong.TryParse(idText, out ulong id)) Join(new CSteamID(id));
        }
    }
}
