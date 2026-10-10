using System.Collections;
using System.Collections.Generic;
using System.Linq;
using AwesomeBalls.Cards;
using AwesomeBalls.Combat;
using AwesomeBalls.Networking;
using FishNet.Connection;
using FishNet.Managing.Scened;
using FishNet.Object;
using FishNet.Object.Synchronizing;
using FishNet.Transporting;
using UnityEngine;
using Scene = UnityEngine.SceneManagement.Scene;

namespace AwesomeBalls.Match
{
    public enum MatchPhase : byte
    {
        Loading,
        Countdown,
        Fighting,
        RoundEnd,
        CardPick,
        MatchEnd,
    }

    /// <summary>A card owned by (or offered to) a player.</summary>
    public struct PlayerCard
    {
        public int ClientId;
        public int CardId;
    }

    /// <summary>
    /// Runs a match (GDD 4, state machine 9.4): random arena each round → countdown → fight to one
    /// life → round result → losers pick 1 of N cards → next arena … until someone reaches the win
    /// target. A global network object: it survives the arena scene swaps and late joiners get it.
    /// </summary>
    public sealed class MatchManager : NetworkBehaviour
    {
        public const int NoWinner = -1;
        public const int Draw = -2;

        [SerializeField] CardDatabase database;
        [SerializeField] NetworkObject playerPrefab;
        [SerializeField] string[] arenaScenes;
        [SerializeField] string[] arenaNames;

        [Header("Rules (GDD 4.1)")]
        [SerializeField] int winsToWin = 5;
        [SerializeField] int cardsOffered = 5;
        [SerializeField] float countdownTime = 3f;
        [SerializeField] float roundEndTime = 3f;
        [SerializeField] float pickTime = 20f;
        [SerializeField] float afterPickTime = 1.5f;
        [SerializeField] float loadTimeout = 15f;

        readonly SyncVar<MatchPhase> phase = new(MatchPhase.Loading);
        readonly SyncVar<uint> phaseEndTick = new();
        readonly SyncVar<int> round = new();
        readonly SyncVar<int> arenaIndex = new(-1);
        readonly SyncVar<int> roundWinner = new(NoWinner);
        readonly SyncVar<int> matchWinner = new(NoWinner);
        readonly SyncDictionary<int, int> wins = new();
        readonly SyncDictionary<int, string> names = new();
        readonly SyncList<PlayerCard> cards = new();
        readonly SyncList<PlayerCard> offers = new();
        readonly SyncList<int> pickers = new();
        readonly SyncDictionary<int, int> hovered = new();
        readonly SyncDictionary<int, int> chosen = new();

        // Server only.
        readonly List<NetworkConnection> participants = new();
        readonly HashSet<NetworkConnection> loaded = new();
        readonly List<Health> fighters = new();
        readonly System.Random rng = new();
        Coroutine phaseRoutine;

        public static MatchManager Instance { get; private set; }

        public CardDatabase Database => database;
        public MatchPhase Phase => phase.Value;
        public int Round => round.Value;
        public int WinsToWin => winsToWin;
        public int RoundWinner => roundWinner.Value;
        public int MatchWinner => matchWinner.Value;
        public string ArenaName => arenaIndex.Value >= 0 && arenaIndex.Value < arenaNames.Length ? arenaNames[arenaIndex.Value] : "";
        public IReadOnlyDictionary<int, int> Wins => wins;
        public IReadOnlyList<PlayerCard> Cards => cards;
        public IReadOnlyList<PlayerCard> Offers => offers;
        public IReadOnlyList<int> Pickers => pickers;
        public IReadOnlyDictionary<int, int> Hovered => hovered;
        public IReadOnlyDictionary<int, int> Chosen => chosen;

        public float TimeLeft
        {
            get
            {
                long ticks = (long)phaseEndTick.Value - TimeManager.Tick;
                return Mathf.Max(0f, (float)(ticks * TimeManager.TickDelta));
            }
        }

        public string NameOf(int clientId) =>
            names.TryGetValue(clientId, out string n) && !string.IsNullOrEmpty(n) ? n : $"Игрок {clientId + 1}";

        public IEnumerable<int> CardsOf(int clientId) => cards.Where(c => c.ClientId == clientId).Select(c => c.CardId);

        // ---------------- Lifecycle ----------------

        public override void OnStartNetwork()
        {
            base.OnStartNetwork();
            Instance = this;
            Health.AutoRespawn = false;
        }

        public override void OnStopNetwork()
        {
            base.OnStopNetwork();
            if (Instance == this) Instance = null;
            Health.AutoRespawn = true;
        }

        public override void OnStartServer()
        {
            base.OnStartServer();
            Health.ServerDied += OnServerDied;
            SceneManager.OnClientPresenceChangeEnd += OnClientPresenceChanged;
            ServerManager.OnRemoteConnectionState += OnRemoteConnectionState;
            StartMatch();
        }

        public override void OnStopServer()
        {
            base.OnStopServer();
            Health.ServerDied -= OnServerDied;
            SceneManager.OnClientPresenceChangeEnd -= OnClientPresenceChanged;
            ServerManager.OnRemoteConnectionState -= OnRemoteConnectionState;
        }

        public override void OnStartClient()
        {
            base.OnStartClient();
            ServerRegisterName(PlayerNetwork.LocalPlayerName());
        }

        [ServerRpc(RequireOwnership = false)]
        void ServerRegisterName(string playerName, NetworkConnection caller = null)
        {
            if (caller == null) return;
            if (playerName.Length > 32) playerName = playerName.Substring(0, 32);
            names[caller.ClientId] = playerName;
        }

        // ---------------- Match flow (server) ----------------

        /// <summary>Server: (re)start the match with everyone connected. Also used for "Rematch".</summary>
        public void StartMatch()
        {
            if (!IsServerStarted) return;
            participants.Clear();
            wins.Clear();
            cards.Clear();
            matchWinner.Value = NoWinner;
            round.Value = 0;
            BeginRound();
        }

        void BeginRound()
        {
            // Friends who joined mid-match enter at the next round.
            foreach (NetworkConnection conn in ServerManager.Clients.Values)
                if (conn.IsActive && !participants.Contains(conn)) participants.Add(conn);
            foreach (NetworkConnection conn in participants)
                if (!wins.ContainsKey(conn.ClientId)) wins[conn.ClientId] = 0;

            round.Value++;
            roundWinner.Value = NoWinner;
            offers.Clear();
            pickers.Clear();
            hovered.Clear();
            chosen.Clear();
            fighters.Clear();
            loaded.Clear();

            arenaIndex.Value = PickArena();
            SetPhase(MatchPhase.Loading, loadTimeout);
            var load = new SceneLoadData(arenaScenes[arenaIndex.Value]) { ReplaceScenes = ReplaceOption.All };
            SceneManager.LoadGlobalScenes(load);
            Restart(LoadTimeout());
        }

        int PickArena()
        {
            if (arenaScenes.Length <= 1) return 0;
            int next;
            do next = rng.Next(arenaScenes.Length);
            while (next == arenaIndex.Value);
            return next;
        }

        void OnClientPresenceChanged(ClientPresenceChangeEventArgs args)
        {
            if (phase.Value != MatchPhase.Loading || !args.Added) return;
            if (arenaIndex.Value < 0 || args.Scene.name != arenaScenes[arenaIndex.Value]) return;
            loaded.Add(args.Connection);
            if (participants.All(loaded.Contains)) SpawnFighters(args.Scene);
        }

        IEnumerator LoadTimeout()
        {
            yield return new WaitForSeconds(loadTimeout);
            // Start with whoever made it; the rest join next round.
            Scene arena = UnityEngine.SceneManagement.SceneManager.GetSceneByName(arenaScenes[arenaIndex.Value]);
            if (phase.Value == MatchPhase.Loading && arena.IsValid()) SpawnFighters(arena);
        }

        void SpawnFighters(Scene arena)
        {
            int offset = rng.Next(16);
            int index = 0;
            foreach (NetworkConnection conn in participants)
            {
                if (!loaded.Contains(conn)) continue;
                Transform point = SpawnPoint.Pick(arena, offset + index++);
                Vector3 position = point != null ? point.position : Vector3.zero;
                Quaternion rotation = point != null ? point.rotation : Quaternion.identity;

                NetworkObject player = Instantiate(playerPrefab, position, rotation);
                ServerManager.Spawn(player, conn, arena);
                player.GetComponent<PlayerStats>().ServerSetCards(CardsOf(conn.ClientId).ToList());
                fighters.Add(player.GetComponent<Health>());
            }

            SetPhase(MatchPhase.Countdown, countdownTime);
            Restart(After(countdownTime, () => SetPhase(MatchPhase.Fighting, 0f)));
        }

        void OnServerDied(Health victim, Health killer)
        {
            if (phase.Value == MatchPhase.Fighting || phase.Value == MatchPhase.Countdown) CheckRoundOver();
        }

        void CheckRoundOver()
        {
            fighters.RemoveAll(f => f == null);
            List<Health> alive = fighters.Where(f => !f.IsDead).ToList();

            if (fighters.Count >= 2)
            {
                if (alive.Count == 1) EndRound(alive[0].OwnerId);
                else if (alive.Count == 0) EndRound(Draw);
            }
            else if (alive.Count == 0)
            {
                // Solo (testing): dying loses the round, which still grants a card.
                EndRound(NoWinner);
            }
        }

        void EndRound(int winner)
        {
            roundWinner.Value = winner;
            if (winner >= 0) wins[winner] = wins.TryGetValue(winner, out int w) ? w + 1 : 1;

            SetPhase(MatchPhase.RoundEnd, roundEndTime);
            bool matchOver = winner >= 0 && wins[winner] >= winsToWin;
            Restart(After(roundEndTime, () =>
            {
                if (matchOver) EndMatch(winner);
                else StartCardPick(winner);
            }));
        }

        void StartCardPick(int winner)
        {
            // GDD 4.4: everyone except the round winner picks; a draw gives no cards.
            if (winner == Draw)
            {
                BeginRound();
                return;
            }

            foreach (NetworkConnection conn in participants)
            {
                int id = conn.ClientId;
                if (id == winner) continue;
                foreach (int cardId in database.GenerateOffer(rng, CardsOf(id).ToList(), cardsOffered))
                    offers.Add(new PlayerCard { ClientId = id, CardId = cardId });
                pickers.Add(id);
            }

            if (pickers.Count == 0)
            {
                BeginRound();
                return;
            }

            SetPhase(MatchPhase.CardPick, pickTime);
            Restart(PickTimeout());
        }

        IEnumerator PickTimeout()
        {
            yield return new WaitForSeconds(pickTime);
            foreach (int id in pickers.ToList())
            {
                List<PlayerCard> mine = offers.Where(o => o.ClientId == id).ToList();
                if (mine.Count > 0) Choose(id, rng.Next(mine.Count));
            }
        }

        [ServerRpc(RequireOwnership = false)]
        public void ServerHover(int slot, NetworkConnection caller = null)
        {
            if (caller == null || phase.Value != MatchPhase.CardPick || !pickers.Contains(caller.ClientId)) return;
            hovered[caller.ClientId] = slot;
        }

        [ServerRpc(RequireOwnership = false)]
        public void ServerChoose(int slot, NetworkConnection caller = null)
        {
            if (caller == null || phase.Value != MatchPhase.CardPick) return;
            Choose(caller.ClientId, slot);
        }

        void Choose(int clientId, int slot)
        {
            if (!pickers.Contains(clientId)) return;
            List<PlayerCard> mine = offers.Where(o => o.ClientId == clientId).ToList();
            if (slot < 0 || slot >= mine.Count) return;

            cards.Add(mine[slot]);
            chosen[clientId] = slot;
            pickers.Remove(clientId);

            if (pickers.Count == 0)
            {
                SetPhase(MatchPhase.CardPick, afterPickTime);
                Restart(After(afterPickTime, BeginRound));
            }
        }

        void EndMatch(int winner)
        {
            matchWinner.Value = winner;
            SetPhase(MatchPhase.MatchEnd, 0f);
        }

        void OnRemoteConnectionState(NetworkConnection conn, RemoteConnectionStateArgs args)
        {
            if (args.ConnectionState != RemoteConnectionState.Stopped) return;
            participants.Remove(conn);
            loaded.Remove(conn);

            switch (phase.Value)
            {
                case MatchPhase.Loading when participants.Count > 0 && participants.All(loaded.Contains):
                    SpawnFighters(UnityEngine.SceneManagement.SceneManager.GetSceneByName(arenaScenes[arenaIndex.Value]));
                    break;
                case MatchPhase.Countdown:
                case MatchPhase.Fighting:
                    // Their player object is despawned with the connection.
                    StartCoroutine(After(0.1f, CheckRoundOver));
                    break;
                case MatchPhase.CardPick when pickers.Contains(conn.ClientId):
                    pickers.Remove(conn.ClientId);
                    if (pickers.Count == 0) Restart(After(afterPickTime, BeginRound));
                    break;
            }
        }

        // ---------------- Helpers ----------------

        void SetPhase(MatchPhase next, float duration)
        {
            phase.Value = next;
            phaseEndTick.Value = TimeManager.Tick + TimeManager.TimeToTicks(duration);
        }

        void Restart(IEnumerator routine)
        {
            if (phaseRoutine != null) StopCoroutine(phaseRoutine);
            phaseRoutine = StartCoroutine(routine);
        }

        static IEnumerator After(float seconds, System.Action action)
        {
            yield return new WaitForSeconds(seconds);
            action();
        }
    }
}
