using System;
using System.Collections;
using System.Collections.Generic;
using OpeningBell.Casino;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// A playable blackjack table (CASINO_SPEC C3). Sit on any stool: the view settles over the felt and the table's
    /// controls come up. The round (<see cref="BlackjackRound"/>) decides every card the moment an action is taken;
    /// the table then lays the cards out one at a time and pays the chips once they're all showing, so nothing on the
    /// felt ever runs ahead of, or disagrees with, the result. You can't get up mid-hand.
    /// </summary>
    public sealed class BlackjackTable : MonoBehaviour
    {
        private const float DealInterval = 0.32f;

        private string _id, _name;
        private GameBootstrap _game;
        private InteractionHud _hud;
        private Kit _kit;
        private StaffNpc _dealer;
        private SeatView _view;
        private Transform _live; // cards and chips, rebuilt when what's showing changes

        private int _seat = -1;
        private bool _gliding => _view.Gliding;

        // What the felt shows: cards laid so far this round, and the round they belong to.
        private int _roundNumber, _shownRound = -1, _shown, _layout = -1;
        private float _nextCard;
        private decimal _bet;
        private string _message;

        private VisualElement _panel;
        private Label _title, _status, _dealerLine, _hands, _chips, _betLabel;
        private readonly List<CasinoUi.Pill> _pills = new List<CasinoUi.Pill>();

        public BlackjackRound Round { get; private set; }
        public BlackjackRules Rules => Round.Rules;
        public string Id => _id;
        public string Name => _name;
        public bool Seated => _seat >= 0;
        public int Seat => _seat;
        /// <summary>Every card of the round is on the felt and the chips are paid (tests wait for this).</summary>
        public bool Revealed => _shown >= CardCount && _shownRound == _roundNumber;
        public decimal Bet => _bet;
        public string Message => _message;

        public static BlackjackTable Build(CityContext c, Transform table, Transform live, string id, string name, BlackjackRules rules, StaffNpc dealer)
        {
            var t = live.gameObject.AddComponent<BlackjackTable>();
            t._id = id;
            t._name = name;
            t._game = c.Game;
            t._hud = c.Hud;
            t._kit = c.Kit;
            t._dealer = dealer;
            t._view = new SeatView(c.Player);
            t._live = Kit.Group(live, "On the felt");
            // A fresh shuffle from the operating system's entropy: no seed a player could learn or replay.
            var shoe = new Shoe(rules.Decks, rules.Penetration, new SeededRandom(CasinoRandom.FreshSeed()));
            t.Round = new BlackjackRound(rules, shoe, c.Game.Casino.Account, () => c.Game.Clock.Now);
            t._bet = rules.MinBet;
            c.Game.Casino.Register(id, t.Round);

            for (int s = 0; s < CasinoProps.Seats; s++)
            {
                var seat = new GameObject($"Seat {s + 1}");
                seat.transform.SetParent(live, false);
                seat.transform.localPosition = CasinoProps.StoolSpot(s) + Vector3.up * 0.55f;
                var box = seat.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.5f, 0.6f, 0.5f);
                seat.AddComponent<BlackjackSeat>().Configure(t, s);
            }
            return t;
        }


        // ---------------------------------------------------------------- seat

        public string SeatPrompt
        {
            get
            {
                string waiting = _game.Casino.TableInPlay;
                if (waiting != null && waiting != _id) return "Your hand is waiting at the other table";
                return $"Sit at blackjack · {CasinoUi.Money(Rules.MinBet)}–{CasinoUi.Money(Rules.MaxBet)}";
            }
        }

        public bool CanSit
        {
            get
            {
                string waiting = _game.Casino.TableInPlay;
                return !Seated && !_gliding && (waiting == null || waiting == _id);
            }
        }

        public void Sit(int seat)
        {
            if (!CanSit) return;
            _seat = seat;
            // Stand the body just behind the stool, facing the dealer; then settle the view over the felt.
            Vector3 stool = CasinoProps.StoolSpot(seat);
            Vector3 back = (stool - new Vector3(0f, 0f, CasinoProps.DealerEdge)).normalized;
            float yaw = transform.eulerAngles.y + CasinoProps.SeatYaw(seat);
            (Vector3 eye, Vector3 focus) = SeatPose();
            _view.Sit(this, transform.TransformPoint(stool + back * 0.35f), yaw, eye, focus);
            if (_game.Casino.FirstTime("blackjack"))
                _hud.ShowToast("Blackjack: get closer to 21 than the dealer without going over. Pick a bet with the chips, then Deal. " +
                               "Double: double the bet for one card. Split: a pair becomes two hands.", 9f);
            if (_bet > _game.Casino.Account.Chips) ClearBet();
            _message = Round.Phase == BlackjackPhase.PlayerTurn ? "Your hand is still in play." : "Place your bet.";
            _layout = -1;
            ShowPanel(true);
            if (_dealer != null && _dealer.AtStation && Round.Phase != BlackjackPhase.PlayerTurn)
                _dealer.Say(_game.Casino.Account.Chips < Rules.MinBet ? "You'll need chips from the cage first." : "Welcome. Place your bet when you're ready.");
        }

        public bool CanLeave => Seated && !_gliding && Round.Phase != BlackjackPhase.PlayerTurn && Revealed;

        public void Leave()
        {
            if (!Seated) return;
            if (Round.Phase == BlackjackPhase.PlayerTurn)
            {
                _hud.ShowToast("Finish the hand first.", 2.5f);
                return;
            }
            if (!CanLeave) return;
            ShowPanel(false);
            _view.Stand(this, () => _seat = -1);
        }

        /// <summary>The seated view: over the player's spot at the rail, looking at the dealer's side of the felt.</summary>
        private (Vector3 Eye, Vector3 Focus) SeatPose()
        {
            Vector3 stool = CasinoProps.StoolSpot(_seat);
            Vector3 toDealer = (new Vector3(0f, 0f, CasinoProps.DealerEdge) - stool).normalized;
            // Pitched ~21° down: the bet circle (~50° below) and the dealer's face (~8° above) both fit a 60° view.
            Vector3 eyeLocal = stool + toDealer * 0.3f + Vector3.up * 1.36f;
            return (transform.TransformPoint(eyeLocal), transform.TransformPoint(eyeLocal + toDealer * 1.2f + Vector3.down * 0.46f));
        }

        // ---------------------------------------------------------------- play

        public bool CanDeal => Seated && !_gliding && Round.Phase != BlackjackPhase.PlayerTurn && Revealed;
        private bool Acting => Seated && !_gliding && Round.Phase == BlackjackPhase.PlayerTurn && Revealed;

        /// <summary>The bet starts at the table minimum; the first chip clicked replaces it, later ones add to it.</summary>
        public void AddToBet(decimal chip)
        {
            if (!CanDeal) return;
            _bet = Math.Min(_betIsDefault ? chip : _bet + chip, Rules.MaxBet);
            _betIsDefault = false;
        }

        private bool _betIsDefault = true;

        public void ClearBet()
        {
            _bet = Rules.MinBet;
            _betIsDefault = true;
        }

        public void SetBet(decimal bet)
        {
            _bet = bet;
            _betIsDefault = false;
        }

        public string Deal()
        {
            if (!CanDeal) return "Not now.";
            string error = Round.Deal(_bet);
            if (error != null)
            {
                _message = error;
                _hud.ShowToast(error, 3f);
                return error;
            }
            _roundNumber++;
            _message = null;
            return null;
        }

        public string Act(Func<string> action)
        {
            if (!Acting) return "Not now.";
            string error = action();
            if (error != null) _hud.ShowToast(error, 2.5f);
            return error;
        }

        public string Hit() => Act(Round.Hit);
        public string Stand() => Act(Round.Stand);
        public string Double() => Act(Round.Double);
        public string Split() => Act(Round.Split);

        private void Update()
        {
            // Lay the next card after a beat; a new round clears the felt first.
            if (_shownRound != _roundNumber)
            {
                _shownRound = _roundNumber;
                _shown = 0;
                _nextCard = Time.time + 0.15f;
            }
            if (_shown < CardCount && Time.time >= _nextCard)
            {
                _shown++;
                _nextCard = Time.time + DealInterval;
                if (_shown == CardCount && Round.Phase == BlackjackPhase.Settled) Announce();
            }
            int layout = _shown * 10 + (Round.HoleRevealed ? 1 : 0) + (Revealed && Round.Phase == BlackjackPhase.Settled ? 5 : 0)
                         + Round.Hands.Count * 1000 + (Seated ? 100_000 + _seat * 10_000 : 0);
            if (layout != _layout || _drawnChips != _game.Casino.Account.Chips)
            {
                _layout = layout;
                Redraw();
            }

            if (Seated && !_gliding && _view.BackPressed) Leave();
            if (_panel != null && _panel.style.display == DisplayStyle.Flex) RefreshPanel();
        }

        /// <summary>Cards in deal order: the first four (player, dealer, player, dealer), then player hits, then the dealer's draws.</summary>
        private List<(int Hand, int Index)> DealOrder()
        {
            var order = new List<(int, int)>();
            if (Round.Hands.Count == 0 || Round.Dealer.Count < 2) return order;
            // After a split, the first deal's second card is the one that moved to hand 2.
            bool split = Round.Hands.Count > 1;
            order.Add((0, 0));
            order.Add((-1, 0));
            order.Add(split ? (1, 0) : (0, 1));
            order.Add((-1, 1));
            var seen = new HashSet<(int, int)>(order);
            for (int h = 0; h < Round.Hands.Count; h++)
                for (int i = 0; i < Round.Hands[h].Cards.Count; i++)
                    if (seen.Add((h, i))) order.Add((h, i));
            for (int i = 2; i < Round.Dealer.Count; i++) order.Add((-1, i));
            return order;
        }

        private int CardCount
        {
            get
            {
                int n = Round.Dealer.Count;
                foreach (BlackjackHand h in Round.Hands) n += h.Cards.Count;
                return n;
            }
        }

        private decimal _drawnChips = -1m;

        private void Redraw()
        {
            for (int i = _live.childCount - 1; i >= 0; i--) Destroy(_live.GetChild(i).gameObject);
            _drawnChips = _game.Casino.Account.Chips;
            int seat = Seated ? _seat : 2;
            List<(int Hand, int Index)> order = DealOrder();
            var visible = new HashSet<(int, int)>();
            for (int i = 0; i < Math.Min(_shown, order.Count); i++) visible.Add(order[i]);
            int playerCards = 0;
            foreach (BlackjackHand h in Round.Hands) playerCards += h.Cards.Count;
            bool holeUp = Round.HoleRevealed && _shown >= playerCards + 2;

            Vector3 spot = CasinoProps.BetSpot(seat);
            Vector3 toDealer = (new Vector3(0f, spot.y, CasinoProps.DealerEdge) - spot).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, toDealer);
            float seatYaw = CasinoProps.SeatYaw(seat);
            bool paid = Revealed && Round.Phase == BlackjackPhase.Settled;

            for (int h = 0; h < Round.Hands.Count; h++)
            {
                BlackjackHand hand = Round.Hands[h];
                float lateral = (h - (Round.Hands.Count - 1) / 2f) * 0.2f;
                Vector3 bet = spot + right * lateral;
                if (Round.Phase != BlackjackPhase.Settled || !paid || hand.Returned > 0m)
                    CasinoProps.Stacks(_kit, _live, bet, hand.Bet);
                if (paid && hand.Returned > hand.Bet)
                    CasinoProps.Stacks(_kit, _live, bet + right * 0.075f, hand.Returned - hand.Bet);
                for (int i = 0; i < hand.Cards.Count; i++)
                {
                    if (!visible.Contains((h, i))) continue;
                    // Fanned up toward the dealer, each overlapping the last so every corner index shows.
                    Vector3 at = bet + toDealer * (0.14f + i * 0.03f) + right * (i * 0.028f) + Vector3.up * (0.001f + i * 0.0005f);
                    float yaw = seatYaw + (hand.Doubled && i == 2 ? 90f : 0f);
                    CasinoProps.Card(_kit, _live, hand.Cards[i], at, yaw);
                }
            }
            for (int i = 0; i < Round.Dealer.Count; i++)
            {
                if (!visible.Contains((-1, i))) continue;
                Vector3 at = new Vector3(-0.05f * (Round.Dealer.Count - 1) + i * 0.1f, CasinoProps.FeltY + 0.001f + i * 0.0005f, CasinoProps.DealerEdge - 0.24f);
                Card? face = i == 1 && !holeUp ? (Card?)null : Round.Dealer[i];
                CasinoProps.Card(_kit, _live, face, at, 0f); // upright to the players
            }
            // The player's chips on the felt to the right of their circle.
            Vector3 rack = spot - toDealer * 0.06f + right * 0.2f;
            if (Seated && _drawnChips >= 1m) CasinoProps.Stacks(_kit, _live, rack, Math.Floor(_drawnChips), 12);
        }

        private void Announce()
        {
            string line;
            decimal net = Round.TotalReturned - Round.TotalWagered;
            if (Round.Hands.Count == 1 && Round.Hands[0].Outcome == HandOutcome.Blackjack) line = "Blackjack! Pays three to two.";
            else if (Round.DealerBlackjack) line = "Dealer has blackjack.";
            else if (Round.DealerTotal > 21 && net > 0m) line = "Dealer busts.";
            else if (net > 0m) line = "Winner.";
            else if (net == 0m) line = "Push.";
            else if (System.Linq.Enumerable.All(Round.Hands, h => h.Outcome == HandOutcome.Bust)) line = "Too many.";
            else line = $"Dealer has {Round.DealerTotal}.";
            _message = net > 0m ? $"{line}  +{CasinoUi.Money(net)}" : net < 0m ? $"{line}  −{CasinoUi.Money(-net)}" : line;
            if (_dealer != null && _dealer.AtStation) _dealer.Say(line);
        }

        // ---------------------------------------------------------------- panel

        private void ShowPanel(bool show)
        {
            if (_panel == null)
            {
                if (_hud == null || _hud.Root == null) return;
                BuildPanel(_hud.Root);
            }
            _panel.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private void BuildPanel(VisualElement root)
        {
            _panel = CasinoUi.Frame(root, "blackjack-panel", 620f);
            // Bottom left: subtitles (the dealer's calls) use the bottom centre, notifications the right.
            _panel.style.width = 520f;
            PhoneKit.Absolute(_panel, left: 24f, bottom: 24f);

            VisualElement top = PhoneKit.Row(_panel);
            _title = CasinoUi.Heading(top, "");
            _chips = PhoneKit.Label(top, "", 15f, PhoneKit.Text, true);
            _chips.name = "bj-chips";
            _status = PhoneKit.Label(_panel, "", 18f, Color.white, true);
            _status.name = "bj-status";
            _status.style.marginTop = 6f;
            _dealerLine = PhoneKit.Label(_panel, "", 14f, PhoneKit.Muted);
            _dealerLine.name = "bj-dealer";
            _hands = PhoneKit.Label(_panel, "", 14f, PhoneKit.Text);
            _hands.name = "bj-hands";

            VisualElement betRow = PhoneKit.Row(_panel, Justify.FlexStart);
            betRow.style.marginTop = 8f;
            _betLabel = PhoneKit.Label(betRow, "", 15f, CasinoUi.Gold, true);
            _betLabel.name = "bj-bet";
            _betLabel.style.marginRight = 10f;
            VisualElement chips = CasinoUi.Wrap(_panel);
            foreach (decimal v in new[] { 5m, 25m, 100m, 500m })
            {
                decimal chip = v;
                var p = new CasinoUi.Pill(chips, $"bj-add-{v}", "+" + CasinoUi.Money(v), () => AddToBet(chip), () => CanDeal && chip <= Rules.MaxBet);
                p.Root.style.backgroundColor = Color.Lerp(CasinoProps.ChipColor(v), Color.black, 0.35f);
                _pills.Add(p);
            }
            _pills.Add(new CasinoUi.Pill(chips, "bj-clear", "Min bet", ClearBet, () => CanDeal));
            _pills.Add(new CasinoUi.Pill(chips, "bj-deal", "Deal", () => Deal(), () => CanDeal && _game.Casino.Account.Chips >= _bet, primary: true));

            VisualElement actions = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(actions, "bj-hit", "Hit", () => Hit(), () => Acting && Round.CanHit, primary: true));
            _pills.Add(new CasinoUi.Pill(actions, "bj-stand", "Stand", () => Stand(), () => Acting && Round.CanStand, primary: true));
            _pills.Add(new CasinoUi.Pill(actions, "bj-double", "Double", () => Double(), () => Acting && Round.CanDouble));
            _pills.Add(new CasinoUi.Pill(actions, "bj-split", "Split", () => Split(), () => Acting && Round.CanSplit));
            _pills.Add(new CasinoUi.Pill(actions, "bj-leave", "Leave table (Esc)", Leave, () => CanLeave));
        }

        private void RefreshPanel()
        {
            _title.text = $"{_name.ToUpperInvariant()} · {CasinoUi.Money(Rules.MinBet)}–{CasinoUi.Money(Rules.MaxBet)} · {(Rules.BlackjackPays == 1.5m ? "3:2" : "6:5")} · {(Rules.DealerHitsSoft17 ? "H17" : "S17")}";
            // Chips shown are what the felt shows: winnings count once the last card is down.
            decimal chips = _game.Casino.Account.Chips;
            if (!Revealed && Round.Phase == BlackjackPhase.Settled) chips -= Round.TotalReturned;
            _chips.text = "Chips " + CasinoUi.Money(chips);
            _betLabel.text = Round.Phase == BlackjackPhase.PlayerTurn ? $"In play {CasinoUi.Money(Round.TotalWagered)}" : $"Bet {CasinoUi.Money(_bet)}";

            bool showing = Round.Hands.Count > 0 && _shownRound == _roundNumber;
            if (!Revealed) _status.text = "Dealing…";
            else if (Round.Phase == BlackjackPhase.PlayerTurn)
            {
                _status.text = Round.Hands.Count > 1 ? $"Hand {Round.ActiveHand + 1}: hit or stand?" : "Hit or stand?";
                if (GameSettings.GameHints && Round.Current != null)
                    _status.text += $"   (hint: {BlackjackStrategy.Advice(Round.Current, Round.Dealer[0], Round.CanDouble, Round.CanSplit)})";
            }
            else _status.text = _message ?? "Place your bet.";

            if (showing && Revealed)
            {
                int up = BlackjackMath.Total(new[] { Round.Dealer[0] }, out _);
                _dealerLine.text = Round.HoleRevealed ? $"Dealer {Round.DealerTotal}{(Round.DealerTotal > 21 ? " (bust)" : "")}" : $"Dealer shows {up}";
                var lines = new List<string>();
                for (int h = 0; h < Round.Hands.Count; h++)
                {
                    BlackjackHand hand = Round.Hands[h];
                    string total = hand.IsBlackjack ? "Blackjack" : (hand.IsSoft && hand.Total < 21 ? "soft " : "") + hand.Total;
                    string result = hand.Outcome == HandOutcome.Pending ? (Round.Phase == BlackjackPhase.PlayerTurn && h == Round.ActiveHand ? "  ◀" : "") : "  " + hand.Outcome.ToString().ToUpperInvariant();
                    lines.Add($"{(Round.Hands.Count > 1 ? $"Hand {h + 1}: " : "You: ")}{total} · {CasinoUi.Money(hand.Bet)}{(hand.Doubled ? " (doubled)" : "")}{result}");
                }
                _hands.text = string.Join("\n", lines);
            }
            else
            {
                _dealerLine.text = "";
                _hands.text = "";
            }
            _pills.RefreshAll();
        }
    }

    public sealed class BlackjackSeat : Interactable
    {
        private BlackjackTable _table;
        private int _seat;

        public void Configure(BlackjackTable table, int seat)
        {
            _table = table;
            _seat = seat;
        }

        public override string Prompt => _table.SeatPrompt;
        public override string Details => $"{_table.Name} · {_table.Rules.Felt.ToLowerInvariant()}";
        public override bool CanInteract => base.CanInteract && _table.CanSit;
        public override void Interact() => _table.Sit(_seat);
    }
}
