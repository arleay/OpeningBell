using System.Collections.Generic;
using OpeningBell.Casino;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// Mini-baccarat (CASINO_SPEC §28): put chips on Player, Banker or Tie and deal. The coup is settled in one step by
    /// <see cref="BaccaratTable"/>; the dealer then turns the cards over in the order they came out, third cards last,
    /// and the chips pay when the last card is down.
    /// </summary>
    public sealed class BaccaratTableView : MonoBehaviour
    {
        private const float CardInterval = 0.45f;

        private GameBootstrap _game;
        private InteractionHud _hud;
        private Kit _kit;
        private StaffNpc _dealer;
        private SeatView _view;
        private string _name;
        private Transform _felt;
        private int _seat;

        private decimal _chip = 25m, _onPlayer, _onBanker, _onTie;
        private int _coup, _shownCoup = -1, _shown, _layout = -1;
        private float _nextCard;
        private string _message;

        private VisualElement _panel;
        private Label _title, _chips, _status, _bets, _hands;
        private readonly List<CasinoUi.Pill> _pills = new List<CasinoUi.Pill>();

        public BaccaratTable Table { get; private set; }
        public bool Seated => _view.Seated;
        public bool Revealed => !Table.Dealt || (_shownCoup == _coup && _shown >= Table.Order.Count);
        public string Message => _message;
        public decimal OnPlayer => _onPlayer;
        public decimal OnBanker => _onBanker;
        public decimal OnTie => _onTie;

        public static BaccaratTableView Build(CityContext c, Transform live, string name, BaccaratRules rules, StaffNpc dealer)
        {
            var v = live.gameObject.AddComponent<BaccaratTableView>();
            v._game = c.Game;
            v._hud = c.Hud;
            v._kit = c.Kit;
            v._dealer = dealer;
            v._name = name;
            v._view = new SeatView(c.Player);
            v._felt = Kit.Group(live, "On the felt");
            v._chip = rules.MinBet;
            v.Table = new BaccaratTable(rules, new Shoe(rules.Decks, 0.8, new SeededRandom(CasinoRandom.FreshSeed())), c.Game.Casino.Account, () => c.Game.Clock.Now);
            for (int s = 0; s < CasinoProps.Seats; s++)
            {
                var seat = new GameObject($"Seat {s + 1}");
                seat.transform.SetParent(live, false);
                seat.transform.localPosition = CasinoProps.StoolSpot(s) + Vector3.up * 0.55f;
                var box = seat.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.5f, 0.6f, 0.5f);
                seat.AddComponent<BaccaratSeat>().Configure(v, s);
            }
            return v;
        }

        public string Prompt => $"Play baccarat · {CasinoMoney.Whole(Table.Rules.MinBet)}–{CasinoMoney.Whole(Table.Rules.MaxBet)}";
        public bool CanSit => !_view.Seated && !_view.Gliding;

        public void Sit(int seat)
        {
            if (!CanSit) return;
            _seat = seat;
            Vector3 stool = CasinoProps.StoolSpot(seat);
            Vector3 toDealer = (new Vector3(0f, 0f, CasinoProps.DealerEdge) - stool).normalized;
            Vector3 eye = stool + toDealer * 0.3f + Vector3.up * 1.36f;
            _view.Sit(this, transform.TransformPoint(stool - toDealer * 0.35f), transform.eulerAngles.y + CasinoProps.SeatYaw(seat),
                transform.TransformPoint(eye), transform.TransformPoint(eye + toDealer * 1.2f + Vector3.down * 0.46f));
            _message = "Bet on Player, Banker or Tie, then Deal.";
            ShowPanel(true);
            if (_game.Casino.FirstTime("baccarat"))
                _hud.ShowToast("Baccarat: bet on which hand ends closer to 9 (tens and faces count 0). The dealer draws by fixed rules; Banker wins pay 95 cents on the dollar, Tie pays 8 to 1.", 9f);
        }

        public void Leave()
        {
            if (!Seated || !Revealed) return;
            ShowPanel(false);
            _view.Stand(this);
        }

        public void SetChip(decimal v) => _chip = v;

        public void Add(BaccaratOutcome spot)
        {
            if (!Revealed) return;
            if (spot == BaccaratOutcome.Player) _onPlayer += _chip;
            else if (spot == BaccaratOutcome.Banker) _onBanker += _chip;
            else _onTie += _chip;
            CasinoAudio.Play(CasinoArt.Chips(), transform.position, 0.4f);
        }

        public void Clear()
        {
            if (!Revealed) return;
            _onPlayer = _onBanker = _onTie = 0m;
        }

        public string Deal()
        {
            if (!Revealed) return "Dealing.";
            string error = Table.Deal(_onPlayer, _onBanker, _onTie);
            if (error != null)
            {
                _message = error;
                _hud.ShowToast(error, 3f);
                return error;
            }
            _coup++;
            _message = null;
            return null;
        }

        private void Update()
        {
            if (_shownCoup != _coup)
            {
                _shownCoup = _coup;
                _shown = 0;
                _nextCard = Time.time + 0.2f;
            }
            if (Table.Dealt && _shown < Table.Order.Count && Time.time >= _nextCard)
            {
                _shown++;
                _nextCard = Time.time + (GameSettings.ReduceMotion ? 0.12f : CardInterval);
                CasinoAudio.Play(CasinoArt.Card(), transform.position, 0.4f);
                if (_shown == Table.Order.Count) Announce();
            }
            int layout = _shown + _coup * 100 + (int)(_onPlayer + _onBanker * 7 + _onTie * 13) * 1000;
            if (layout != _layout)
            {
                _layout = layout;
                Redraw();
            }
            if (Seated && !_view.Gliding && _view.BackPressed) Leave();
            if (_panel != null && _panel.style.display == DisplayStyle.Flex) RefreshPanel();
        }

        private void Redraw()
        {
            for (int i = _felt.childCount - 1; i >= 0; i--) Destroy(_felt.GetChild(i).gameObject);
            for (int i = 0; i < _shown && i < Table.Order.Count; i++)
            {
                (bool player, int index) = Table.Order[i];
                Card c = player ? Table.Player[index] : Table.Banker[index];
                // Player's hand left, banker's right; the third card turned sideways, as dealt.
                float x = (player ? -0.36f : 0.2f) + index * 0.09f;
                CasinoProps.Card(_kit, _felt, c, new Vector3(x, CasinoProps.FeltY + 0.001f + index * 0.0004f, CasinoProps.DealerEdge - 0.3f), index == 2 ? 90f : 0f);
            }
            // The stakes in front of the seat: Player, Banker, Tie circles side by side.
            Vector3 spot = CasinoProps.BetSpot(Seated ? _seat : 2);
            Vector3 toDealer = (new Vector3(0f, spot.y, CasinoProps.DealerEdge) - spot).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, toDealer);
            if (!Table.Dealt || _shown < Table.Order.Count || _shownCoup != _coup)
            {
                if (_onPlayer > 0m) CasinoProps.Stacks(_kit, _felt, spot - right * 0.1f, _onPlayer, 12);
                if (_onBanker > 0m) CasinoProps.Stacks(_kit, _felt, spot + right * 0.1f, _onBanker, 12);
                if (_onTie > 0m) CasinoProps.Stacks(_kit, _felt, spot + toDealer * 0.1f, _onTie, 12);
            }
        }

        private void Announce()
        {
            int p = BaccaratTable.Total(Table.Player), b = BaccaratTable.Total(Table.Banker);
            decimal net = Table.LastReturned - Table.LastWagered;
            string result = Table.Outcome == BaccaratOutcome.Tie ? $"Tie, {p} all." : $"{Table.Outcome} wins, {System.Math.Max(p, b)} to {System.Math.Min(p, b)}.";
            _message = result + (net > 0m ? $"  +{CasinoMoney.Cents(net)}" : net < 0m ? $"  −{CasinoMoney.Cents(-net)}" : "  Push.");
            if (_dealer != null && _dealer.AtStation) _dealer.Say(result);
        }

        // ---------------------------------------------------------------- panel

        private void ShowPanel(bool show)
        {
            if (_panel == null)
            {
                if (_hud.Root == null) return;
                BuildPanel(_hud.Root);
            }
            _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildPanel(VisualElement root)
        {
            _panel = CasinoUi.Frame(root, "baccarat-panel", 520f);
            PhoneKit.Absolute(_panel, left: 24f, bottom: 24f);
            VisualElement top = PhoneKit.Row(_panel);
            _title = CasinoUi.Heading(top, "");
            _chips = PhoneKit.Label(top, "", 15f, PhoneKit.Text, true);
            _status = PhoneKit.Label(_panel, "", 17f, Color.white, true);
            _status.name = "baccarat-status";
            _status.style.marginTop = 6f;
            _hands = PhoneKit.Label(_panel, "", 14f, PhoneKit.Text);
            VisualElement chips = CasinoUi.Wrap(_panel);
            foreach (decimal v in new[] { 25m, 100m, 500m, 1_000m })
            {
                decimal value = v;
                var pill = new CasinoUi.Pill(chips, $"baccarat-chip-{v}", CasinoMoney.Whole(v), () => SetChip(value));
                pill.Root.style.backgroundColor = Color.Lerp(CasinoProps.ChipColor(v), Color.black, 0.3f);
            }
            _bets = PhoneKit.Label(_panel, "", 14f, CasinoUi.Gold, true);
            _bets.style.marginTop = 4f;
            VisualElement spots = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(spots, "baccarat-player", "+ Player", () => Add(BaccaratOutcome.Player), () => Revealed));
            _pills.Add(new CasinoUi.Pill(spots, "baccarat-banker", "+ Banker", () => Add(BaccaratOutcome.Banker), () => Revealed));
            _pills.Add(new CasinoUi.Pill(spots, "baccarat-tie", "+ Tie", () => Add(BaccaratOutcome.Tie), () => Revealed));
            _pills.Add(new CasinoUi.Pill(spots, "baccarat-clear", "Clear", Clear, () => Revealed));
            VisualElement actions = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(actions, "baccarat-deal", "Deal", () => Deal(), () => Revealed && _onPlayer + _onBanker + _onTie > 0m, primary: true));
            _pills.Add(new CasinoUi.Pill(actions, "baccarat-leave", "Leave (Esc)", Leave, () => Revealed));
        }

        private void RefreshPanel()
        {
            _title.text = $"{_name.ToUpperInvariant()} · {CasinoMoney.Whole(Table.Rules.MinBet)}–{CasinoMoney.Whole(Table.Rules.MaxBet)}";
            decimal chips = _game.Casino.Account.Chips;
            if (!Revealed) chips -= Table.LastReturned;
            _chips.text = "Chips " + CasinoMoney.Cents(chips);
            _status.text = !Revealed ? "Dealing…" : _message ?? "";
            _bets.text = $"Player {CasinoMoney.Whole(_onPlayer)} · Banker {CasinoMoney.Whole(_onBanker)} · Tie {CasinoMoney.Whole(_onTie)}   (chip {CasinoMoney.Whole(_chip)})";
            _hands.text = Table.Dealt && Revealed ? $"Player {BaccaratTable.Total(Table.Player)} ({string.Join(" ", Table.Player)}) · Banker {BaccaratTable.Total(Table.Banker)} ({string.Join(" ", Table.Banker)})" : "";
            _pills.RefreshAll();
        }
    }

    public sealed class BaccaratSeat : Interactable
    {
        private BaccaratTableView _table;
        private int _seat;

        public void Configure(BaccaratTableView table, int seat)
        {
            _table = table;
            _seat = seat;
        }

        public override string Prompt => _table.Prompt;
        public override bool CanInteract => base.CanInteract && _table.CanSit;
        public override void Interact() => _table.Sit(_seat);
    }
}
