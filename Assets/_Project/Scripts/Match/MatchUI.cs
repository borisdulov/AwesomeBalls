using System.Collections.Generic;
using System.Linq;
using System.Text;
using AwesomeBalls.Cards;
using AwesomeBalls.Combat;
using AwesomeBalls.Networking;
using AwesomeBalls.Player;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace AwesomeBalls.Match
{
    /// <summary>
    /// Client-side match UI (GDD 10.1/10.2): score, round banner, countdown, card pick (opponents see
    /// the picker's cards and hover, like Rounds), match result, and builds on Tab. Also owns the
    /// round flow on the client: freezes input outside the fight and frees the cursor to pick.
    /// </summary>
    [RequireComponent(typeof(UIDocument), typeof(MatchManager))]
    public sealed class MatchUI : MonoBehaviour
    {
        static readonly string[] RarityNames = { "Обычная", "Необычная", "Редкая", "Легендарная" };

        [SerializeField] StyleSheet styleSheet;

        MatchManager match;
        VisualElement root, scoreBar, center, pickOverlay, cardRow, endButtons, builds;
        Label roundInfo, bigTitle, subTitle, pickHeader, pickTimer;
        Button rematchButton;
        readonly List<VisualElement> cardElements = new();
        string scoreKey, cardsKey, buildsKey;
        bool ownsCursor;

        int LocalId => match.IsClientStarted ? match.LocalConnection.ClientId : -1;

        void Awake() => match = GetComponent<MatchManager>();

        void OnEnable()
        {
            root = GetComponent<UIDocument>().rootVisualElement;
            root.Clear();
            root.styleSheets.Add(styleSheet);
            root.pickingMode = PickingMode.Ignore;

            var main = Add(root, "match-root");
            scoreBar = Add(main, "score-bar");
            roundInfo = AddLabel(main, "round-info");

            center = Add(main, "center");
            bigTitle = AddLabel(center, "big-title");
            subTitle = AddLabel(center, "sub-title");
            endButtons = Add(center, "end-buttons");
            rematchButton = new Button(() => match.StartMatch()) { text = "Реванш" };
            rematchButton.AddToClassList("end-button");
            rematchButton.AddToClassList("end-button-primary");
            endButtons.Add(rematchButton);
            var leave = new Button(() => NetworkSession.Instance?.Leave()) { text = "В меню" };
            leave.AddToClassList("end-button");
            endButtons.Add(leave);

            pickOverlay = Add(root, "pick-overlay");
            pickHeader = AddLabel(pickOverlay, "pick-header");
            pickTimer = AddLabel(pickOverlay, "pick-timer");
            cardRow = Add(pickOverlay, "card-row");

            builds = Add(root, "builds");
        }

        void OnDisable()
        {
            if (ownsCursor) NetworkSession.UiOwnsCursor = false;
            ownsCursor = false;
            CombatHud.Suppressed = false;
            MovementDebugHud.Suppressed = false;
        }

        void Update()
        {
            if (!match.IsClientStarted) return;
            MatchPhase phase = match.Phase;

            UpdateLocalPlayer(phase);
            UpdateScore();
            UpdateCenter(phase);
            UpdateCardPick(phase);
            UpdateBuilds();
        }

        // ---------------- Local player control ----------------

        void UpdateLocalPlayer(MatchPhase phase)
        {
            PlayerNetwork local = PlayerNetwork.Local;
            if (local != null) local.Controller.InputLocked = phase != MatchPhase.Fighting;

            bool fullScreenUi = phase is MatchPhase.CardPick or MatchPhase.MatchEnd or MatchPhase.Loading;
            CombatHud.Suppressed = fullScreenUi;
            MovementDebugHud.Suppressed = fullScreenUi;

            bool needsCursor = phase == MatchPhase.MatchEnd ||
                               (phase == MatchPhase.CardPick && match.Pickers.Contains(LocalId));
            if (needsCursor && !ownsCursor)
            {
                ownsCursor = true;
                NetworkSession.UiOwnsCursor = true;
                PlayerController.LockCursor(false);
            }
            else if (!needsCursor && ownsCursor)
            {
                ownsCursor = false;
                NetworkSession.UiOwnsCursor = false;
                if (local != null && !local.GetComponent<Health>().IsDead) PlayerController.LockCursor(true);
            }
        }

        // ---------------- Score ----------------

        void UpdateScore()
        {
            string key = string.Join(";", match.Wins.OrderBy(w => w.Key).Select(w => $"{w.Key}:{w.Value}:{match.NameOf(w.Key)}"));
            if (key != scoreKey)
            {
                scoreKey = key;
                scoreBar.Clear();
                foreach (KeyValuePair<int, int> entry in match.Wins.OrderBy(w => w.Key))
                {
                    Color color = PlayerNetwork.ColorFor(entry.Key);
                    var chip = Add(scoreBar, "score-chip");
                    chip.style.borderLeftColor = color;
                    AddLabel(chip, "score-name").text = match.NameOf(entry.Key);
                    for (int i = 0; i < match.WinsToWin; i++)
                    {
                        var pip = Add(chip, "score-pip");
                        if (i < entry.Value) pip.style.backgroundColor = color;
                    }
                }
            }
            roundInfo.text = match.Round > 0 ? $"Раунд {match.Round} · {match.ArenaName} · до {match.WinsToWin} побед" : "";
        }

        // ---------------- Center banner ----------------

        void UpdateCenter(MatchPhase phase)
        {
            string title = "", sub = "";
            bool showEnd = false;
            float left = match.TimeLeft;

            switch (phase)
            {
                case MatchPhase.Loading:
                    title = "";
                    sub = $"Загрузка: {match.ArenaName}…";
                    break;
                case MatchPhase.Countdown:
                    title = Mathf.CeilToInt(left).ToString();
                    sub = match.ArenaName;
                    break;
                case MatchPhase.Fighting:
                    if (match.TimeLeft <= 0f && Time.time - fightStart < 0.8f) title = "В БОЙ!";
                    break;
                case MatchPhase.RoundEnd:
                    title = match.RoundWinner switch
                    {
                        MatchManager.Draw => "Ничья",
                        MatchManager.NoWinner => "Раунд проигран",
                        int id => $"{match.NameOf(id)} берёт раунд",
                    };
                    break;
                case MatchPhase.MatchEnd:
                    title = $"{match.NameOf(match.MatchWinner)} победил!";
                    sub = match.IsServerStarted ? "" : "Ждём, пока хост решит, что дальше…";
                    showEnd = true;
                    break;
            }

            if (phase == MatchPhase.Fighting && lastPhase != MatchPhase.Fighting) fightStart = Time.time;
            lastPhase = phase;

            bigTitle.text = title;
            subTitle.text = sub;
            bigTitle.EnableInClassList("hidden", string.IsNullOrEmpty(title));
            subTitle.EnableInClassList("hidden", string.IsNullOrEmpty(sub));
            endButtons.EnableInClassList("hidden", !showEnd);
            rematchButton.EnableInClassList("hidden", !match.IsServerStarted);
        }

        MatchPhase lastPhase;
        float fightStart;

        // ---------------- Card pick ----------------

        void UpdateCardPick(MatchPhase phase)
        {
            bool picking = phase == MatchPhase.CardPick && (match.Pickers.Count > 0 || match.Chosen.Count > 0);
            pickOverlay.EnableInClassList("hidden", !picking);
            if (!picking)
            {
                cardsKey = null;
                return;
            }

            // Show our own offer if we pick, otherwise the first player still picking (or the last one who did).
            int shown = match.Offers.Any(o => o.ClientId == LocalId) ? LocalId
                : match.Pickers.Count > 0 ? match.Pickers[0]
                : match.Chosen.Keys.FirstOrDefault();
            bool mine = shown == LocalId;
            bool stillPicking = match.Pickers.Contains(shown);
            List<int> offer = match.Offers.Where(o => o.ClientId == shown).Select(o => o.CardId).ToList();

            pickHeader.text = mine
                ? stillPicking ? "Выберите карту" : "Карта выбрана"
                : stillPicking ? $"{match.NameOf(shown)} выбирает карту…" : $"{match.NameOf(shown)} выбрал карту";
            pickTimer.text = stillPicking ? $"{Mathf.CeilToInt(match.TimeLeft)} с" : "";

            string key = shown + ":" + string.Join(",", offer);
            if (key != cardsKey)
            {
                cardsKey = key;
                BuildCards(offer, shown, mine);
            }

            match.Hovered.TryGetValue(shown, out int hover);
            bool hasHover = match.Hovered.ContainsKey(shown);
            bool hasChoice = match.Chosen.TryGetValue(shown, out int choice);
            for (int i = 0; i < cardElements.Count; i++)
            {
                cardElements[i].EnableInClassList("hovered", !hasChoice && hasHover && hover == i);
                cardElements[i].EnableInClassList("chosen", hasChoice && choice == i);
                cardElements[i].EnableInClassList("faded", hasChoice && choice != i);
            }
        }

        void BuildCards(List<int> offer, int owner, bool interactive)
        {
            cardRow.Clear();
            cardElements.Clear();
            List<int> owned = match.CardsOf(owner).ToList();

            for (int slot = 0; slot < offer.Count; slot++)
            {
                CardDefinition card = match.Database.Get(offer[slot]);
                if (card == null) continue;
                var element = Add(cardRow, "card");
                element.AddToClassList("rarity-" + card.rarity.ToString().ToLowerInvariant());
                AddLabel(element, "card-name").text = card.displayName;
                var rarity = AddLabel(element, "card-rarity");
                rarity.text = RarityNames[(int)card.rarity];
                rarity.AddToClassList("rarity-label-" + card.rarity.ToString().ToLowerInvariant());
                AddLabel(element, "card-description").text = card.description;
                int stacks = owned.Count(id => id == offer[slot]);
                if (stacks > 0) AddLabel(element, "card-owned").text = $"Уже есть: ×{stacks}";

                if (interactive)
                {
                    int captured = slot;
                    element.RegisterCallback<PointerEnterEvent>(_ => match.ServerHover(captured));
                    element.RegisterCallback<ClickEvent>(_ => match.ServerChoose(captured));
                }
                cardElements.Add(element);
            }
        }

        // ---------------- Builds (Tab) ----------------

        void UpdateBuilds()
        {
            bool show = Keyboard.current != null && Keyboard.current.tabKey.isPressed;
            builds.EnableInClassList("hidden", !show);
            if (!show) return;

            string key = string.Join(";", match.Cards.Select(c => $"{c.ClientId}:{c.CardId}")) + "|" + scoreKey;
            if (key == buildsKey) return;
            buildsKey = key;
            builds.Clear();

            foreach (int id in match.Wins.Keys.OrderBy(k => k))
            {
                var name = AddLabel(builds, "builds-player");
                name.text = $"{match.NameOf(id)} — побед: {match.Wins[id]}";
                name.style.color = PlayerNetwork.ColorFor(id);

                var groups = match.CardsOf(id).GroupBy(c => c).Select(g =>
                {
                    string cardName = match.Database.Get(g.Key)?.displayName ?? "?";
                    return g.Count() > 1 ? $"{cardName} ×{g.Count()}" : cardName;
                });
                var text = new StringBuilder(string.Join(" · ", groups));
                AddLabel(builds, "builds-cards").text = text.Length > 0 ? text.ToString() : "без карт";
            }
        }

        // ---------------- Helpers ----------------

        static VisualElement Add(VisualElement parent, string className)
        {
            var element = new VisualElement();
            element.AddToClassList(className);
            element.pickingMode = className is "card" or "end-buttons" ? PickingMode.Position : PickingMode.Ignore;
            parent.Add(element);
            return element;
        }

        static Label AddLabel(VisualElement parent, string className)
        {
            var label = new Label();
            label.AddToClassList(className);
            label.pickingMode = PickingMode.Ignore;
            parent.Add(label);
            return label;
        }
    }
}
