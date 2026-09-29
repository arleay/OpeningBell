using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Casino;
using OpeningBell.Casino.Poker;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// A poker table in the poker room (CASINO_SPEC §35–40, §66): a no-limit cash game or a Sit &amp; Go against five
    /// NPCs who sit round it. You sit in the near seat. NPCs act one at a time with a beat between; each only knows its
    /// own cards (<see cref="PokerBrain"/>). Chips in front of each seat, bets toward the middle, the board and the pot
    /// on the felt; the panel has your cards and the actions. Cash games buy in from your chips and cash out when you
    /// stand up (between hands); a Sit &amp; Go runs until you bust or win.
    /// </summary>
    public sealed class PokerTableView : MonoBehaviour
    {
        private GameBootstrap _game;
        private InteractionHud _hud;
        private Kit _kit;
        private StaffNpc _dealer;
        private SeatView _view;
        private Transform _felt;
        private string _name;

        private PokerStakes _stakes;
        private PokerCashGame _cash;
        private SitAndGo _sng;
        private HoldemTable _table;

        private readonly NpcBody[] _bodies = new NpcBody[6];
        private readonly TextMesh[] _labels = new TextMesh[6];
        private readonly string[] _lastAction = new string[6];
        private float _nextStep, _nextHand = -1f;
        private int _logSeen, _layout = -1;
        private decimal _raiseTo;
        private string _message;
        private readonly List<string> _feed = new List<string>();

        private VisualElement _panel, _buyIn;
        private Label _title, _stack, _status, _cards, _pot, _log, _raiseLabel;
        private readonly List<CasinoUi.Pill> _pills = new List<CasinoUi.Pill>();
        private CasinoUi.Pill _callPill, _raisePill;

        public bool Tournament => _sng != null || _stakes == null;
        public HoldemTable Table => _table;
        public PokerCashGame Cash => _cash;
        public SitAndGo Sng => _sng;
        public bool Seated => _view.Seated;
        public string Message => _message;
        public bool PlayerToAct => _table != null && !_table.HandOver && _table.ToAct == 0 && Playing;
        private bool Playing => (_cash != null && _cash.Seated) || (_sng != null && _sng.Registered && !_sng.Finished);
        public bool Open => _dealer == null || _dealer.AtStation;

        public static PokerTableView Build(CityContext c, Transform live, string name, PokerStakes stakes, StaffNpc dealer)
        {
            var v = live.gameObject.AddComponent<PokerTableView>();
            v._game = c.Game;
            v._hud = c.Hud;
            v._kit = c.Kit;
            v._dealer = dealer;
            v._name = name;
            v._stakes = stakes;
            v._view = new SeatView(c.Player);
            v._felt = Kit.Group(live, "On the felt");
            v.NewGame();
            // The NPCs round the table: bodies in the five far chairs (they change as players come and go).
            for (int s = 1; s < 6; s++)
            {
                Vector3 at = CasinoProps.PokerOnEllipse(CasinoProps.PokerSeatAngles[s], 1.5f, 0f);
                NpcBody body = NpcBody.Create(c.Kit, live, "Poker player", 9400 + s * 17 + name.Length, Color.HSVToRGB((s * 0.19f) % 1f, 0.4f, 0.5f));
                body.transform.localPosition = at;
                body.transform.localRotation = Quaternion.Euler(0f, Mathf.Atan2(-at.x, -at.z) * Mathf.Rad2Deg, 0f);
                v._bodies[s] = body;
                v._labels[s] = c.Kit.Text(live, "", at + new Vector3(0f, 1.62f, 0f), 0f, 0.05f, new Color(1f, 0.9f, 0.6f));
            }
            var seat = new GameObject("Player seat");
            seat.transform.SetParent(live, false);
            seat.transform.localPosition = CasinoProps.PokerOnEllipse(270f, 1.55f, 0.55f);
            var box = seat.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.size = new Vector3(0.6f, 0.8f, 0.6f);
            seat.AddComponent<PokerSeatTap>().Configure(v);
            return v;
        }

        private void NewGame()
        {
            var rng = new SeededRandom(CasinoRandom.FreshSeed());
            if (_stakes != null)
            {
                _cash = new PokerCashGame(_stakes, _game.Casino.Account, rng, () => _game.Clock.Now);
                _table = _cash.Table;
            }
            else
            {
                _sng = new SitAndGo(_game.Casino.Account, rng, () => _game.Clock.Now);
                _table = _sng.Table;
            }
            _logSeen = 0;
        }

        // ---------------------------------------------------------------- seat

        public string Prompt => !Open ? "Poker room table (closed: opens 10 AM)"
            : Tournament ? $"Sit & Go · {CasinoMoney.Whole(SitAndGo.BuyIn)} + {CasinoMoney.Whole(SitAndGo.Fee)} · top two paid"
            : $"Sit at {_stakes.Name} · buy in {CasinoMoney.Whole(_stakes.MinBuyIn)}–{CasinoMoney.Whole(_stakes.MaxBuyIn)}";

        public bool CanSit => !_view.Seated && !_view.Gliding && Open;

        public void Sit()
        {
            if (!CanSit) return;
            Vector3 chair = CasinoProps.PokerOnEllipse(270f, 1.55f, 0f);
            Vector3 eye = CasinoProps.PokerOnEllipse(270f, 1.3f, 1.3f);
            _view.Sit(this, transform.TransformPoint(chair + new Vector3(0f, 0f, -0.3f)), transform.eulerAngles.y,
                transform.TransformPoint(eye), transform.TransformPoint(new Vector3(0f, CasinoProps.PokerFelt - 0.05f, 0.25f)));
            ShowPanel(true);
            _message = Tournament ? $"Register for {CasinoMoney.Whole(SitAndGo.BuyIn + SitAndGo.Fee)}: six players, 1,500 chips each, blinds up every {SitAndGo.HandsPerLevel} hands."
                : $"Buy in for {CasinoMoney.Whole(_stakes.MinBuyIn)}–{CasinoMoney.Whole(_stakes.MaxBuyIn)} to take the seat.";
            if (_game.Casino.FirstTime("poker"))
                _hud.ShowToast("Poker: you play the other players, not the house (the room takes a small rake). Best five cards from your two and the five on the board. " +
                               "Fold, check or call, bet or raise; nobody at the table sees your cards, and you don't see theirs.", 10f);
        }

        public bool CanLeave => Seated && !_view.Gliding && (_cash == null || !_cash.Seated || _cash.CanLeave);

        public void Leave()
        {
            if (!CanLeave) return;
            if (_cash != null && _cash.Seated) _cash.Leave();
            if (_sng != null && _sng.Registered && !_sng.Finished)
            {
                _sng.Forfeit();
                _hud.ShowToast("You left the tournament: the entry is gone.", 4f);
            }
            if (_sng != null && _sng.Finished) NewGame(); // a fresh tournament for the next visit
            _game.Casino.PokerSeat = null;
            _nextHand = -1f;
            ShowPanel(false);
            _view.Stand(this);
        }

        public string BuyIn(decimal amount)
        {
            if (_cash == null) return "Not a cash table.";
            string error = _cash.Sit(amount);
            _message = error ?? $"You're in with {CasinoMoney.Whole(amount)}. Next hand coming up.";
            if (error == null)
            {
                _game.Casino.PokerSeat = () => _cash.Seated ? (_stakes.Id, _cash.StackIfAbandoned, _cash.BoughtIn) : ((string, decimal, decimal)?)null;
                _nextHand = Time.time + 1.5f;
            }
            return error;
        }

        public string Register()
        {
            if (_sng == null) return "Not a tournament table.";
            if (_sng.Finished) NewGame();
            string error = _sng.Register();
            _message = error ?? "Registered. Shuffle up and deal.";
            if (error == null) _nextHand = Time.time + 1.5f;
            return error;
        }

        public string TopUp(decimal amount)
        {
            string error = _cash?.TopUp(amount) ?? "Not a cash table.";
            _message = error ?? $"Topped up {CasinoMoney.Whole(amount)}.";
            if (error == null && _nextHand < 0f && (_table.HandOver || _table.Street == PokerStreet.Waiting)) _nextHand = Time.time + 1f;
            return error;
        }

        // ---------------------------------------------------------------- play

        public string Act(PokerMove move, decimal to = 0m)
        {
            if (!PlayerToAct) return "Not your turn.";
            string error = _table.Act(0, move, to);
            if (error != null)
            {
                _message = error;
                return error;
            }
            _nextStep = Time.time + 0.6f;
            return null;
        }

        public string Fold() => Act(PokerMove.Fold);
        public string CheckOrCall() => Act(_table.CanCheck(0) ? PokerMove.Check : PokerMove.Call);
        public string RaiseTo(decimal to) => Act(_table.CurrentBet == 0m ? PokerMove.Bet : PokerMove.Raise, to);
        public string AllIn() => Act(PokerMove.AllIn);

        private void SetRaise(decimal to)
        {
            if (_table == null || _table.ToAct != 0) return;
            _raiseTo = Math.Max(_table.MinRaiseTo, Math.Min(_table.MaxRaiseTo(0), Math.Floor(to)));
        }

        public bool StartNextHand()
        {
            bool started = _cash != null ? _cash.NextHand() : _sng.NextHand();
            if (!started) return false;
            _logSeen = 0;
            for (int i = 0; i < 6; i++) _lastAction[i] = null;
            if (_cash != null)
                foreach (string a in _cash.Arrivals) Feed(a);
            if (_sng != null) Feed($"Hand {_table.HandNumber} · blinds {CasinoMoney.Whole(_table.SmallBlind)}/{CasinoMoney.Whole(_table.BigBlind)} · {_sng.Remaining} left");
            _nextStep = Time.time + 0.8f;
            _message = null;
            CasinoAudio.Play(CasinoArt.Card(), transform.position, 0.4f);
            return true;
        }

        private void Update()
        {
            if (_table == null) return;
            float pace = GameSettings.ReduceMotion ? 0.35f : 0.9f;

            // Between hands: the next one deals after a pause, while you're in.
            if (Playing && (_table.HandOver || _table.Street == PokerStreet.Waiting) && _nextHand > 0f && Time.time >= _nextHand)
            {
                _nextHand = -1f;
                if (!StartNextHand()) _message = "Waiting for players.";
            }
            // NPC turns, one at a time.
            if (!_table.HandOver && _table.ToAct > 0 && Time.time >= _nextStep)
            {
                if (_cash != null) _cash.StepNpc();
                else _sng.StepNpc();
                _nextStep = Time.time + pace;
            }
            ReadLog();
            if (_table.HandOver && _nextHand < 0f && Playing && _handDone != _table.HandNumber) HandFinished();

            int layout = _table.Log.Count * 7 + _table.Board.Count * 1000 + _table.HandNumber * 100_000 + (_table.HandOver ? 3 : 0) + (Playing ? 1 : 0);
            if (layout != _layout)
            {
                _layout = layout;
                Redraw();
            }
            // Labels over the NPCs face the player's seat.
            Vector3 viewer = transform.TransformPoint(CasinoProps.PokerOnEllipse(270f, 1.3f, 1.3f));
            for (int s = 1; s < 6; s++)
            {
                PokerSeat seat = _table.Seats[s];
                string text = seat == null ? "" : $"{seat.Name}  {CasinoMoney.Whole(seat.Stack)}" + (_lastAction[s] != null ? "\n" + _lastAction[s] : "");
                if (_labels[s].text != text) _labels[s].text = text;
                _labels[s].transform.rotation = Quaternion.LookRotation(_labels[s].transform.position - viewer);
                bool present = seat != null;
                if (_bodies[s].gameObject.activeSelf != present) _bodies[s].gameObject.SetActive(present);
                if (present) _bodies[s].Animate(NpcPose.Sit, Time.time);
            }

            if (Seated && !_view.Gliding && _view.BackPressed) Leave();
            if (_panel != null && _panel.style.display == DisplayStyle.Flex) RefreshPanel();
        }

        private int _handDone;

        private void HandFinished()
        {
            _handDone = _table.HandNumber;
            foreach (PokerPot pot in _table.Pots)
                foreach (KeyValuePair<int, decimal> paid in pot.Paid)
                {
                    string who = paid.Key == 0 ? "You win" : _table.Seats[paid.Key].Name + " wins";
                    string with = _table.ShowdownReached && pot.WinningHand.HasValue ? " with " + pot.WinningHand.Value.Name.ToLowerInvariant() : "";
                    Feed($"{who} {CasinoMoney.Whole(paid.Value)}{with}.");
                }
            if (_table.Rake > 0m) Feed($"Rake {CasinoMoney.Whole(_table.Rake)}.");
            if (_table.WonBy(0) > 0m) CasinoAudio.Play(CasinoArt.Chips(), transform.position, 0.5f);
            if (_sng != null)
            {
                _sng.AfterHand();
                if (_sng.Finished)
                {
                    _message = _sng.Place == 1 ? $"You won the Sit & Go! {CasinoMoney.Whole(_sng.Prize)}." :
                        _sng.Prize > 0m ? $"You finished {Ordinal(_sng.Place)}: {CasinoMoney.Whole(_sng.Prize)}." : $"Out in {Ordinal(_sng.Place)}. Good game.";
                    _hud.ShowToast(_message, 5f);
                    return;
                }
            }
            if (_cash != null && _cash.Seated && _cash.PlayerStack <= 0m) _message = "You're out of chips: top up or stand up.";
            else _nextHand = Time.time + (GameSettings.ReduceMotion ? 1.2f : 3f);
        }

        private static string Ordinal(int n) => n + (n == 1 ? "st" : n == 2 ? "nd" : n == 3 ? "rd" : "th");

        private void ReadLog()
        {
            while (_logSeen < _table.Log.Count)
            {
                PokerAction a = _table.Log[_logSeen++];
                string who = a.Seat == 0 ? "You" : _table.Seats[a.Seat]?.Name ?? "?";
                string what = a.Move switch
                {
                    PokerMove.Fold => "fold",
                    PokerMove.Check => "check",
                    PokerMove.Call => $"call {CasinoMoney.Whole(a.Added)}",
                    PokerMove.Bet => $"bet {CasinoMoney.Whole(a.To)}",
                    PokerMove.Raise => $"raise to {CasinoMoney.Whole(a.To)}",
                    PokerMove.AllIn => $"all in ({CasinoMoney.Whole(a.To)})",
                    PokerMove.SmallBlind => $"small blind {CasinoMoney.Whole(a.Added)}",
                    _ => $"big blind {CasinoMoney.Whole(a.Added)}",
                };
                _lastAction[a.Seat] = what.ToUpperInvariant();
                if (a.Move != PokerMove.SmallBlind && a.Move != PokerMove.BigBlind) Feed($"{who} {what}.");
                if (a.Move != PokerMove.Fold && a.Move != PokerMove.Check) CasinoAudio.Play(CasinoArt.Chips(), transform.position, 0.3f);
            }
        }

        private void Feed(string line)
        {
            _feed.Add(line);
            if (_feed.Count > 6) _feed.RemoveAt(0);
        }

        private void Redraw()
        {
            for (int i = _felt.childCount - 1; i >= 0; i--) Destroy(_felt.GetChild(i).gameObject);
            float y = CasinoProps.PokerFelt + 0.001f;
            // Board.
            for (int i = 0; i < _table.Board.Count; i++)
                CasinoProps.Card(_kit, _felt, _table.Board[i], new Vector3(-0.2f + i * 0.1f, y, 0.02f), 0f);
            // Pot (what's been collected from earlier streets) in the middle.
            decimal collected = _table.Seats.Where(s => s != null).Sum(s => s.Total - s.Street);
            if (_table.HandOver) collected = 0m;
            if (collected > 0m) CasinoProps.Stacks(_kit, _felt, new Vector3(0f, y, -0.14f), collected, 10);
            for (int s = 0; s < 6; s++)
            {
                PokerSeat seat = _table.Seats[s];
                if (seat == null) continue;
                float angle = CasinoProps.PokerSeatAngles[s];
                Vector3 front = CasinoProps.PokerOnEllipse(angle, 0.82f, y);
                Vector3 bet = CasinoProps.PokerOnEllipse(angle, 0.62f, y);
                float yaw = Mathf.Atan2(-front.x, -front.z) * Mathf.Rad2Deg + 180f;
                if (seat.Stack > 0m) CasinoProps.Stacks(_kit, _felt, front + Quaternion.Euler(0f, yaw, 0f) * new Vector3(0.12f, 0f, 0f), seat.Stack, 10);
                if (!_table.HandOver && seat.Street > 0m) CasinoProps.Stacks(_kit, _felt, bet, seat.Street, 8);
                if (seat.InHand && !seat.Folded && seat.Hole.Count == 2)
                {
                    // Yours face up; theirs face down, turned over at showdown.
                    bool show = s == 0 || (_table.HandOver && _table.ShowdownReached);
                    for (int c = 0; c < 2; c++)
                        CasinoProps.Card(_kit, _felt, show ? seat.Hole[c] : (Card?)null,
                            front + Quaternion.Euler(0f, yaw, 0f) * new Vector3(-0.08f + c * 0.05f, c * 0.0005f, 0.02f), yaw + 180f);
                }
            }
            // Dealer button.
            if (_table.Button >= 0)
                _kit.Cylinder(_felt, "Dealer button", CasinoProps.PokerOnEllipse(CasinoProps.PokerSeatAngles[_table.Button] + 12f, 0.72f, y + 0.004f), 0.05f, 0.008f, _kit.P.Lit(Color.white, 0.5f));
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
            _panel = CasinoUi.Frame(root, "poker-panel", 560f);
            PhoneKit.Absolute(_panel, left: 24f, bottom: 24f);
            VisualElement top = PhoneKit.Row(_panel);
            _title = CasinoUi.Heading(top, "");
            _stack = PhoneKit.Label(top, "", 15f, PhoneKit.Text, true);
            _stack.name = "poker-stack";
            _status = PhoneKit.Label(_panel, "", 16f, Color.white, true);
            _status.name = "poker-status";
            _status.style.marginTop = 4f;
            _cards = PhoneKit.Label(_panel, "", 15f, CasinoUi.Gold, true);
            _cards.name = "poker-cards";
            _pot = PhoneKit.Label(_panel, "", 13f, PhoneKit.Text);
            _log = PhoneKit.Label(_panel, "", 12f, PhoneKit.Muted);
            _log.name = "poker-log";
            _log.style.marginTop = 4f;

            // Buy-in / register row (shown when you're not playing).
            _buyIn = CasinoUi.Wrap(_panel);
            if (_cash != null)
            {
                foreach ((string label, decimal amount) in new[] { ("Min", _stakes.MinBuyIn), ("Half", Math.Floor((_stakes.MinBuyIn + _stakes.MaxBuyIn) / 2m)), ("Max", _stakes.MaxBuyIn) })
                {
                    decimal a = amount;
                    new CasinoUi.Pill(_buyIn, "poker-buyin-" + label.ToLowerInvariant(), $"Buy in {CasinoMoney.Whole(a)}", () => BuyIn(a), primary: label == "Max");
                }
            }
            else new CasinoUi.Pill(_buyIn, "poker-register", $"Register {CasinoMoney.Whole(SitAndGo.BuyIn + SitAndGo.Fee)}", () => Register(), primary: true);

            VisualElement actions = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(actions, "poker-fold", "Fold", () => Fold(), () => PlayerToAct));
            _callPill = new CasinoUi.Pill(actions, "poker-call", "Check", () => CheckOrCall(), () => PlayerToAct, primary: true);
            _pills.Add(_callPill);
            _raisePill = new CasinoUi.Pill(actions, "poker-raise", "Raise", () => RaiseTo(_raiseTo), () => PlayerToAct && _table.CanRaise(0), primary: true);
            _pills.Add(_raisePill);
            _pills.Add(new CasinoUi.Pill(actions, "poker-allin", "All in", () => AllIn(), () => PlayerToAct));
            VisualElement sizing = CasinoUi.Wrap(_panel);
            _raiseLabel = PhoneKit.Label(sizing, "", 13f, CasinoUi.Gold, true);
            _raiseLabel.style.marginRight = 8f;
            _pills.Add(new CasinoUi.Pill(sizing, "poker-size-min", "Min", () => SetRaise(_table.MinRaiseTo), () => PlayerToAct, size: 12f));
            _pills.Add(new CasinoUi.Pill(sizing, "poker-size-half", "½ pot", () => SetRaise(_table.CurrentBet + _table.Pot / 2m), () => PlayerToAct, size: 12f));
            _pills.Add(new CasinoUi.Pill(sizing, "poker-size-pot", "Pot", () => SetRaise(_table.CurrentBet + _table.Pot + _table.ToCall(0)), () => PlayerToAct, size: 12f));
            _pills.Add(new CasinoUi.Pill(sizing, "poker-size-up", "+", () => SetRaise(_raiseTo + _table.BigBlind), () => PlayerToAct, size: 12f));
            _pills.Add(new CasinoUi.Pill(sizing, "poker-size-down", "−", () => SetRaise(_raiseTo - _table.BigBlind), () => PlayerToAct, size: 12f));
            VisualElement other = CasinoUi.Wrap(_panel);
            if (_cash != null) _pills.Add(new CasinoUi.Pill(other, "poker-topup", "Top up to max", () => TopUp(_stakes.MaxBuyIn - _cash.PlayerStack), () => _cash.Seated && _cash.CanLeave && _cash.PlayerStack < _stakes.MaxBuyIn));
            _pills.Add(new CasinoUi.Pill(other, "poker-leave", "Stand up (Esc)", Leave, () => CanLeave));
        }

        private void RefreshPanel()
        {
            _title.text = Tournament
                ? $"{_name.ToUpperInvariant()} · SIT & GO · BLINDS {CasinoMoney.Whole(_table.SmallBlind)}/{CasinoMoney.Whole(_table.BigBlind)}"
                : $"{_name.ToUpperInvariant()} · {_stakes.Name.ToUpperInvariant()}";
            PokerSeat me = _table.Seats[0];
            _stack.text = me != null ? $"Stack {CasinoMoney.Whole(me.Stack)}" + (Tournament ? " chips" : "") : $"Chips {CasinoMoney.Cents(_game.Casino.Account.Chips)}";
            _buyIn.style.display = Playing ? DisplayStyle.None : DisplayStyle.Flex;
            if (PlayerToAct)
            {
                decimal toCall = _table.ToCall(0);
                _status.text = toCall > 0m ? $"Your turn: {CasinoMoney.Whole(toCall)} to call." : "Your turn.";
                if (_raiseTo < _table.MinRaiseTo || _raiseTo > _table.MaxRaiseTo(0)) _raiseTo = Math.Min(_table.MinRaiseTo, _table.MaxRaiseTo(0));
            }
            else _status.text = _message ?? (Playing ? (_table.HandOver ? "Next hand shortly…" : "Waiting…") : "");
            _callPill.Text.text = PlayerToAct && !_table.CanCheck(0) ? $"Call {CasinoMoney.Whole(_table.ToCall(0))}" : "Check";
            _raisePill.Text.text = (_table.CurrentBet == 0m ? "Bet " : "Raise to ") + CasinoMoney.Whole(_raiseTo);
            _raiseLabel.text = PlayerToAct ? $"Size {CasinoMoney.Whole(_raiseTo)}" : "";
            if (me != null && me.Hole.Count == 2 && me.InHand)
            {
                string hand = string.Join("  ", me.Hole);
                if (_table.Board.Count >= 3) hand += "   ·   " + HandEvaluator.Evaluate(me.Hole.Concat(_table.Board).ToList()).Name;
                _cards.text = (me.Folded ? "Folded: " : "Your cards: ") + hand;
            }
            else _cards.text = "";
            _pot.text = _table.Street == PokerStreet.Waiting ? "" : $"Pot {CasinoMoney.Whole(_table.Pot)} · {_table.Street}" + (_table.Board.Count > 0 ? " · board " + string.Join(" ", _table.Board) : "");
            _log.text = string.Join("\n", _feed);
            _pills.RefreshAll();
        }
    }

    public sealed class PokerSeatTap : Interactable
    {
        private PokerTableView _table;
        public void Configure(PokerTableView table) => _table = table;
        public override string Prompt => _table.Prompt;
        public override bool CanInteract => base.CanInteract && _table.CanSit;
        public override void Interact() => _table.Sit();
    }
}
