using System.Collections.Generic;
using AwesomeBalls.Networking;
using UnityEngine;
using UnityEngine.UIElements;

namespace AwesomeBalls.UI
{
    /// <summary>
    /// Main menu and pre-game lobby (GDD 10.1, minimal version): create a Steam lobby, invite friends,
    /// start the match; plus local host/join for testing on one machine.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class MainMenuController : MonoBehaviour
    {
        const float RefreshInterval = 0.25f;

        VisualElement mainPanel, lobbyPanel, playerList;
        Button createLobby, invite, startGame, freePlay;
        Label steamHint, lobbyTitle, waiting, status;
        NetworkSession session;
        float refreshTimer;
        readonly List<string> lastNames = new();

        void OnEnable()
        {
            UnityEngine.Cursor.lockState = CursorLockMode.None;
            UnityEngine.Cursor.visible = true;

            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            mainPanel = root.Q("main-panel");
            lobbyPanel = root.Q("lobby-panel");
            playerList = root.Q("player-list");
            createLobby = root.Q<Button>("create-lobby");
            invite = root.Q<Button>("invite");
            startGame = root.Q<Button>("start-game");
            freePlay = root.Q<Button>("free-play");
            steamHint = root.Q<Label>("steam-hint");
            lobbyTitle = root.Q<Label>("lobby-title");
            waiting = root.Q<Label>("waiting");
            status = root.Q<Label>("status");

            createLobby.clicked += () => { SetStatus(""); session.Lobby.Create(); };
            root.Q<Button>("local-host").clicked += () => { SetStatus(""); session.StartLocalHost(); };
            root.Q<Button>("local-join").clicked += () => { SetStatus("Подключение к localhost…"); session.JoinLocal(); };
            root.Q<Button>("quit").clicked += Quit;
            invite.clicked += () => session.Lobby.OpenInviteOverlay();
            startGame.clicked += () => session.StartGame();
            freePlay.clicked += () => session.StartFreePlay();
            root.Q<Button>("leave").clicked += () => session.Leave(loadMenu: false);
        }

        void Start()
        {
            session = NetworkSession.FindOrNull();
            if (session == null)
            {
                SetStatus("NetworkManager не найден в сцене.");
                mainPanel.SetEnabled(false);
                return;
            }
            session.Lobby.Failed += SetStatus;
            Refresh();
        }

        void OnDestroy()
        {
            if (session != null) session.Lobby.Failed -= SetStatus;
        }

        void Update()
        {
            if (session == null) return;
            refreshTimer -= Time.unscaledDeltaTime;
            if (refreshTimer > 0f) return;
            refreshTimer = RefreshInterval;
            Refresh();
        }

        void Refresh()
        {
            SteamLobby lobby = session.Lobby;
            bool inSession = session.IsActive || lobby.InLobby || lobby.IsBusy;
            mainPanel.EnableInClassList("hidden", inSession);
            lobbyPanel.EnableInClassList("hidden", !inSession);

            createLobby.SetEnabled(lobby.Available && !lobby.IsBusy);
            steamHint.text = lobby.Available
                ? $"Steam: {lobby.LocalName}. Лобби только для друзей."
                : "Steam не запущен — доступен только локальный тест.";

            if (!inSession) return;

            bool steam = session.Mode == SessionMode.Steam || lobby.InLobby;
            lobbyTitle.text = steam ? "Лобби Steam" : "Локальный тест";
            invite.EnableInClassList("hidden", !steam);
            startGame.EnableInClassList("hidden", !session.IsHost);
            startGame.SetEnabled(session.IsConnected);
            freePlay.EnableInClassList("hidden", !session.IsHost);
            freePlay.SetEnabled(session.IsConnected);
            waiting.EnableInClassList("hidden", session.IsHost);
            waiting.text = session.IsConnected ? "Ждём, пока хост начнёт игру…" : "Подключение…";

            var names = new List<string>();
            if (steam) names.AddRange(lobby.GetMemberNames());
            else if (session.IsHost) names.Add($"Подключено игроков: {session.ConnectedPlayers}");
            else names.Add(session.IsConnected ? "Подключено к хосту" : "Подключение…");
            if (lobby.IsBusy) names.Add("Создаём лобби…");
            UpdatePlayerList(names);
        }

        void UpdatePlayerList(List<string> names)
        {
            if (names.Count == lastNames.Count && names.TrueForAll(lastNames.Contains)) return;
            lastNames.Clear();
            lastNames.AddRange(names);
            playerList.Clear();
            foreach (string name in names)
            {
                var label = new Label(name);
                label.AddToClassList("player");
                playerList.Add(label);
            }
        }

        void SetStatus(string message)
        {
            if (status != null) status.text = message;
        }

        static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
    }
}
