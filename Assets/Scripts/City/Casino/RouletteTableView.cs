using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Casino;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// A playable roulette table (CASINO_SPEC §24–27). Bets go down from a betting board on screen (no pixel-hunting on
    /// the felt): pick a chip and a bet type, click numbers or the outside boxes; the chips appear on the layout.
    /// <see cref="RouletteTable.Spin"/> settles everything the moment Spin is pressed; the wheel and ball then play it
    /// out, the ball dropping into the pocket already chosen, and only then do the chips pay (§26).
    /// </summary>
    public sealed class RouletteTableView : MonoBehaviour
    {
        private const float BallTrack = 0.36f, BallPocket = 0.27f;

        private GameBootstrap _game;
        private InteractionHud _hud;
        private Kit _kit;
        private StaffNpc _croupier;
        private SeatView _view;
        private string _name;
        private decimal[] _chipValues;

        private Transform _rotor, _ball, _chips, _dolly;
        private float _rotorAngle, _rotorSpeed = 20f;
        // The spin being played out: start time, its length, where the ball ends relative to the rotor.
        private float _spinStart = -1f, _spinLength, _ballStart, _ballFinal;
        private int _pocketIndex;
        private bool _landed = true;
        private int _chipsVersion = -1;

        private decimal _chip;
        private RouletteBetKind _mode = RouletteBetKind.Straight;
        private int _pendingSplit = -1;
        private readonly List<RouletteBet> _placed = new List<RouletteBet>();
        private string _message;

        private VisualElement _panel, _board;
        private Label _title, _chipsLabel, _status, _betsLabel, _history;
        private readonly List<CasinoUi.Pill> _pills = new List<CasinoUi.Pill>();
        private readonly Dictionary<RouletteBetKind, CasinoUi.Pill> _modes = new Dictionary<RouletteBetKind, CasinoUi.Pill>();
        private readonly Dictionary<decimal, CasinoUi.Pill> _chipPills = new Dictionary<decimal, CasinoUi.Pill>();
        private readonly Dictionary<int, VisualElement> _cells = new Dictionary<int, VisualElement>();

        public RouletteTable Table { get; private set; }
        public string Name => _name;
        public bool Seated => _view.Seated;
        public bool Spinning => !_landed;
        public string Message => _message;
        public decimal Chip => _chip;

        private int[] Order => Table.Rules.DoubleZero ? Roulette.AmericanWheel : Roulette.EuropeanWheel;

        public static RouletteTableView Build(CityContext c, Transform live, string name, RouletteRules rules, StaffNpc croupier, decimal[] chips)
        {
            var v = live.gameObject.AddComponent<RouletteTableView>();
            v._game = c.Game;
            v._hud = c.Hud;
            v._kit = c.Kit;
            v._croupier = croupier;
            v._name = name;
            v._chipValues = chips;
            v._chip = chips[1];
            v._view = new SeatView(c.Player);
            v.Table = new RouletteTable(rules, c.Game.Casino.Account, new SeededRandom(CasinoRandom.FreshSeed()), () => c.Game.Clock.Now);
            v.BuildWheel(rules.DoubleZero);
            v._chips = Kit.Group(live, "Chips on the layout");
            for (int s = 0; s < 5; s++)
            {
                var seat = new GameObject("Seat " + (s + 1));
                seat.transform.SetParent(live, false);
                seat.transform.localPosition = new Vector3(-1.05f + s * 0.4f, 0.55f, -1.0f);
                var box = seat.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = new Vector3(0.4f, 0.6f, 0.45f);
                seat.AddComponent<RouletteSeat>().Configure(v, s);
            }
            return v;
        }

        private void BuildWheel(bool doubleZero)
        {
            Transform wheel = Kit.Group(transform, "Wheel", CasinoProps.WheelCentre + new Vector3(0f, 0.1f, 0f));
            _rotor = Kit.Group(wheel, "Rotor");
            var disc = new GameObject("Pockets");
            disc.transform.SetParent(_rotor, false);
            disc.AddComponent<MeshFilter>().sharedMesh = CasinoProps.Disc(0.34f);
            disc.AddComponent<MeshRenderer>().sharedMaterial = new Material(_kit.P.Lit(Color.white, 0.6f)) { mainTexture = CasinoArt.WheelTexture(doubleZero) };
            int[] order = Order;
            for (int i = 0; i < order.Length; i++)
            {
                float a = i * 360f / order.Length;
                Vector3 p = Quaternion.Euler(0f, a, 0f) * new Vector3(0f, 0.002f, 0.29f);
                TextMesh t = _kit.Text(_rotor, Roulette.Label(order[i]), p, 0f, 0.016f, Color.white);
                // Flat on the wheel, the top of each number pointing outward.
                t.transform.localRotation = Quaternion.Euler(0f, a, 0f) * Quaternion.Euler(90f, 0f, 0f);
            }
            _kit.Cylinder(_rotor, "Turret", new Vector3(0f, 0.03f, 0f), 0.07f, 0.06f, _kit.P.Metal(new Color(0.85f, 0.7f, 0.35f), 0.8f));
            _kit.Box(_rotor, "Handle", new Vector3(0f, 0.065f, 0f), new Vector3(0.16f, 0.012f, 0.012f), _kit.P.Metal(new Color(0.85f, 0.7f, 0.35f), 0.8f), collider: false);
            _kit.Box(_rotor, "Handle", new Vector3(0f, 0.065f, 0f), new Vector3(0.012f, 0.012f, 0.16f), _kit.P.Metal(new Color(0.85f, 0.7f, 0.35f), 0.8f), collider: false);
            _ball = _kit.Sphere(wheel, "Ball", new Vector3(0f, 0.012f, BallTrack), 0.022f, _kit.P.Lit(new Color(0.98f, 0.98f, 0.96f), 0.9f)).transform;
            _ball.gameObject.SetActive(false);
            foreach (Collider col in wheel.GetComponentsInChildren<Collider>()) Destroy(col);
            _dolly = _kit.Cylinder(transform, "Dolly", Vector3.zero, 0.035f, 0.05f, _kit.P.Lit(new Color(0.95f, 0.95f, 0.95f), 0.8f)).transform;
            _dolly.gameObject.SetActive(false);
        }

        // ---------------------------------------------------------------- seat

        public string Prompt => $"Play roulette · {Table.Rules.Name.ToLowerInvariant()} · {CasinoMoney.Whole(Table.Rules.TableMin)} min";
        public bool CanSit => !_view.Seated && !_view.Gliding;

        public void Sit(int seat)
        {
            if (!CanSit) return;
            Vector3 stand = transform.TransformPoint(new Vector3(-1.05f + seat * 0.4f, 0f, -1.15f));
            Vector3 eye = transform.TransformPoint(new Vector3(-0.2f, 1.55f, -1.15f));
            Vector3 focus = transform.TransformPoint(new Vector3(0.15f, CasinoProps.RouletteFelt, 0f));
            _view.Sit(this, stand, transform.eulerAngles.y, eye, focus);
            _message = "Pick a chip, then click the board. Spin when you're set.";
            ShowPanel(true);
            if (_croupier != null && _croupier.AtStation) _croupier.Say("Welcome. Place your bets.");
            if (_game.Casino.FirstTime("roulette"))
                _hud.ShowToast("Roulette: bet on a number (pays 35 to 1), a group (split, street, corner, six line) or an outside bet " +
                               "(red/black, odd/even, halves pay even money; dozens and columns 2 to 1). Green zero loses outside bets.", 9f);
        }

        public void Leave()
        {
            if (!Seated || Spinning) return;
            Table.Clear();
            _placed.Clear();
            ShowPanel(false);
            _view.Stand(this);
        }

        // ---------------------------------------------------------------- betting

        public void SetChip(decimal value) => _chip = value;

        public void SetMode(RouletteBetKind mode)
        {
            _mode = mode;
            _pendingSplit = -1;
        }

        /// <summary>A click on number <paramref name="n"/> on the board, read by the current bet type.</summary>
        public string ClickNumber(int n)
        {
            if (Spinning) return "No more bets.";
            try
            {
                RouletteBet bet;
                switch (_mode)
                {
                    case RouletteBetKind.Split:
                        if (_pendingSplit < 0)
                        {
                            _pendingSplit = n;
                            _message = $"Split from {Roulette.Label(n)}: now click a number next to it.";
                            return null;
                        }
                        bet = Roulette.Split(_pendingSplit, n, _chip);
                        _pendingSplit = -1;
                        break;
                    case RouletteBetKind.Street:
                        bet = Roulette.Street((n + 2) / 3, _chip);
                        break;
                    case RouletteBetKind.Corner:
                        bet = Roulette.Corner(n, _chip);
                        break;
                    case RouletteBetKind.SixLine:
                        bet = Roulette.SixLine((n + 2) / 3, _chip);
                        break;
                    default:
                        bet = Roulette.Straight(n, _chip);
                        break;
                }
                return Place(bet);
            }
            catch (ArgumentException e)
            {
                _pendingSplit = -1;
                _message = e.Message.Split('(')[0].Trim();
                return _message;
            }
        }

        public string ClickOutside(RouletteBetKind kind, int which = 1)
        {
            if (Spinning) return "No more bets.";
            return Place(Roulette.Outside(kind, _chip, which));
        }

        public string Place(RouletteBet bet)
        {
            string error = Table.Place(bet);
            if (error != null)
            {
                _message = error;
                return error;
            }
            _placed.Add(bet);
            _message = $"{bet.Describe()} {CasinoMoney.Whole(bet.Amount)} · pays {bet.PaysToOne} to 1";
            CasinoAudio.Play(CasinoArt.Chips(), transform.position, 0.4f);
            return null;
        }

        /// <summary>Takes back the last chip placed.</summary>
        public void Undo()
        {
            if (Spinning || _placed.Count == 0) return;
            _placed.RemoveAt(_placed.Count - 1);
            Table.Clear();
            foreach (RouletteBet b in _placed) Table.Place(b);
        }

        public void ClearBets()
        {
            if (Spinning) return;
            Table.Clear();
            _placed.Clear();
        }

        public string Rebet()
        {
            if (Spinning) return "Spinning.";
            string error = Table.Rebet();
            _placed.Clear();
            _placed.AddRange(Table.Bets.Select(b => new RouletteBet { Kind = b.Kind, Numbers = b.Numbers, Amount = b.Amount }));
            _message = error;
            return error;
        }

        public string Spin()
        {
            if (Spinning) return "Spinning.";
            var staked = new List<RouletteBet>(Table.Bets);
            string error = Table.Spin();
            if (error != null)
            {
                _message = error;
                _hud.ShowToast(error, 3f);
                return error;
            }
            _placed.Clear();
            _spinBets = staked; // they stay on the felt while the ball runs
            StartSpin();
            return null;
        }

        private void StartSpin()
        {
            _landed = false;
            _spinStart = Time.time;
            _spinLength = GameSettings.ReduceMotion ? 1.2f : 6f;
            _pocketIndex = Array.IndexOf(Order, Table.LastPocket);
            _rotorSpeed = 160f;
            _ballStart = UnityEngine.Random.Range(0f, 360f);
            _ball.gameObject.SetActive(true);
            _dolly.gameObject.SetActive(false);
            _message = "No more bets.";
            if (_croupier != null && _croupier.AtStation) _croupier.Say("No more bets.");
            CasinoAudio.Play(CasinoArt.BallRoll(), _ball.position, 0.35f, loopFor: _spinLength - 0.6f);
        }

        // ---------------------------------------------------------------- animation

        private void Update()
        {
            float dt = Time.deltaTime;
            // The rotor keeps turning; after a spin it slows back to its idle pace.
            _rotorSpeed = Mathf.Lerp(_rotorSpeed, 20f, 1f - Mathf.Exp(-dt / 2.5f));
            _rotorAngle = (_rotorAngle + _rotorSpeed * dt) % 360f;
            _rotor.localRotation = Quaternion.Euler(0f, _rotorAngle, 0f);

            if (!_landed)
            {
                float t = Time.time - _spinStart;
                float u = Mathf.Clamp01(t / _spinLength);
                float pocketAngle = _rotorAngle + _pocketIndex * 360f / Order.Length;
                // The ball runs the other way, easing out, and finishes exactly over its pocket (wherever the rotor is).
                float ease = 1f - Mathf.Pow(1f - u, 3f);
                float laps = GameSettings.ReduceMotion ? 1f : 6f;
                float from = pocketAngle + laps * 360f + (_ballStart % 360f);
                float angle = Mathf.Lerp(from, pocketAngle, ease);
                float radius = u < 0.7f ? BallTrack : Mathf.Lerp(BallTrack, BallPocket, (u - 0.7f) / 0.3f);
                PlaceBall(angle, radius, u < 0.7f ? 0.012f : Mathf.Lerp(0.012f, 0.004f, (u - 0.7f) / 0.3f));
                if (u >= 1f)
                {
                    _landed = true;
                    _ballFinal = _pocketIndex * 360f / Order.Length;
                    CasinoAudio.Play(CasinoArt.BallDrop(), _ball.position, 0.5f);
                    Announce();
                }
            }
            else if (_ball.gameObject.activeSelf)
                PlaceBall(_rotorAngle + _ballFinal, BallPocket, 0.004f); // riding round in its pocket

            // Chips on the layout: what's bet now, or after a spin the winners (with their payouts) until the next bet.
            int version = Table.Bets.Count * 1000 + (int)Table.Staked + (Spinning ? 1 : 0);
            if (version != _chipsVersion)
            {
                _chipsVersion = version;
                DrawChips();
            }

            if (Seated && !_view.Gliding && _view.BackPressed) Leave();
            if (_panel != null && _panel.style.display == DisplayStyle.Flex) RefreshPanel();
        }

        private void PlaceBall(float angle, float radius, float height)
        {
            _ball.localPosition = Quaternion.Euler(0f, angle, 0f) * new Vector3(0f, height, radius);
        }

        private void DrawChips()
        {
            for (int i = _chips.childCount - 1; i >= 0; i--) Destroy(_chips.GetChild(i).gameObject);
            bool dz = Table.Rules.DoubleZero;
            if (Table.Bets.Count > 0 || Spinning)
            {
                // During the spin the bets stay down (taken, not yet paid).
                IEnumerable<RouletteBet> bets = Spinning ? _spinBets : Table.Bets;
                foreach (RouletteBet b in bets) CasinoProps.Stacks(_kit, _chips, CasinoProps.BetSpot(b, dz), b.Amount, 12);
            }
            else if (Table.LastPocket >= 0)
            {
                foreach (RouletteBet b in Table.LastWinners)
                {
                    Vector3 spot = CasinoProps.BetSpot(b, dz);
                    CasinoProps.Stacks(_kit, _chips, spot, b.Amount, 12);
                    CasinoProps.Stacks(_kit, _chips, spot + new Vector3(0f, 0f, 0.05f), b.Amount * b.PaysToOne, 12);
                }
            }
        }

        private List<RouletteBet> _spinBets = new List<RouletteBet>();

        private void Announce()
        {
            int p = Table.LastPocket;
            decimal net = Table.LastReturned - Table.LastWagered;
            _message = $"{Roulette.Describe(p)}. " + (Table.LastReturned > 0m ? $"You win {CasinoMoney.Whole(Table.LastReturned)} (net {(net >= 0 ? "+" : "−")}{CasinoMoney.Whole(Math.Abs(net))})." : "No win this time.");
            _dolly.localPosition = CasinoProps.CellOf(p, Table.Rules.DoubleZero) + new Vector3(0f, 0.025f, 0f);
            _dolly.gameObject.SetActive(true);
            _chipsVersion = -1;
            if (_croupier != null && _croupier.AtStation) _croupier.Say(Roulette.Describe(p) + ".");
            if (Table.LastReturned > 0m) CasinoAudio.Play(CasinoArt.Chips(), transform.position, 0.5f);
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

        private static readonly Color Red = new Color(0.62f, 0.07f, 0.08f), Black = new Color(0.1f, 0.1f, 0.11f), Green = new Color(0.07f, 0.4f, 0.22f);

        private VisualElement Cell(VisualElement parent, string text, Color bg, float width, Action tap, string name, string tag = null)
        {
            VisualElement cell = PhoneKit.Box(parent, name);
            cell.style.width = width;
            cell.style.height = 26f;
            cell.style.backgroundColor = bg;
            cell.style.alignItems = Align.Center;
            cell.style.justifyContent = Justify.Center;
            PhoneKit.Border(cell, 1f, new Color(CasinoUi.Gold.r, CasinoUi.Gold.g, CasinoUi.Gold.b, 0.5f));
            Label l = PhoneKit.Label(cell, text, 12f, Color.white, true);
            l.pickingMode = PickingMode.Ignore;
            if (tag != null)
            {
                // Colour spelled out small in the corner, for anyone who can't tell the red from the black (§100).
                Label t = PhoneKit.Label(cell, tag, 7f, new Color(1f, 1f, 1f, 0.7f));
                PhoneKit.Absolute(t, left: 2f, top: 1f);
                t.pickingMode = PickingMode.Ignore;
            }
            PhoneKit.Tap(cell, tap);
            return cell;
        }

        private void BuildPanel(VisualElement root)
        {
            _panel = CasinoUi.Frame(root, "roulette-panel", 640f);
            PhoneKit.Absolute(_panel, left: 24f, bottom: 24f);
            VisualElement top = PhoneKit.Row(_panel);
            _title = CasinoUi.Heading(top, "");
            _chipsLabel = PhoneKit.Label(top, "", 15f, PhoneKit.Text, true);
            _chipsLabel.name = "roulette-chips";
            _status = PhoneKit.Label(_panel, "", 15f, Color.white, true);
            _status.name = "roulette-status";
            _status.style.marginTop = 4f;
            _history = PhoneKit.Label(_panel, "", 12f, PhoneKit.Muted);

            VisualElement chips = CasinoUi.Wrap(_panel);
            foreach (decimal v in _chipValues)
            {
                decimal value = v;
                var p = new CasinoUi.Pill(chips, $"roulette-chip-{v}", CasinoMoney.Whole(v), () => SetChip(value));
                p.Root.style.backgroundColor = Color.Lerp(CasinoProps.ChipColor(v), Color.black, 0.3f);
                _chipPills[v] = p;
            }
            VisualElement modes = CasinoUi.Wrap(_panel);
            foreach ((RouletteBetKind kind, string label) in new[] { (RouletteBetKind.Straight, "Straight"), (RouletteBetKind.Split, "Split"), (RouletteBetKind.Street, "Street"),
                         (RouletteBetKind.Corner, "Corner"), (RouletteBetKind.SixLine, "Six line") })
            {
                RouletteBetKind k = kind;
                _modes[k] = new CasinoUi.Pill(modes, "roulette-mode-" + k.ToString().ToLowerInvariant(), label, () => SetMode(k), size: 12f);
            }

            // The board: zero(s) on the left, three rows of twelve (3 on top, as seen across a real table), 2:1 on the right.
            _board = PhoneKit.Box(_panel, "roulette-board");
            _board.style.flexDirection = FlexDirection.Row;
            _board.style.marginTop = 8f;
            VisualElement zeros = PhoneKit.Box(_board);
            if (Table.Rules.DoubleZero)
            {
                _cells[Roulette.DoubleZeroPocket] = Cell(zeros, "00", Green, 34f, () => ClickNumber(Roulette.DoubleZeroPocket), "roulette-n-00");
                _cells[Roulette.DoubleZeroPocket].style.height = 39f;
                _cells[0] = Cell(zeros, "0", Green, 34f, () => ClickNumber(0), "roulette-n-0");
                _cells[0].style.height = 39f;
            }
            else
            {
                _cells[0] = Cell(zeros, "0", Green, 34f, () => ClickNumber(0), "roulette-n-0");
                _cells[0].style.height = 78f;
            }
            VisualElement grid = PhoneKit.Box(_board);
            for (int row = 2; row >= 0; row--)
            {
                VisualElement line = PhoneKit.Row(grid, Justify.FlexStart);
                for (int col = 0; col < 12; col++)
                {
                    int n = col * 3 + row + 1;
                    _cells[n] = Cell(line, n.ToString(), Roulette.IsRed(n) ? Red : Black, 38f, () => ClickNumber(n), "roulette-n-" + n, Roulette.IsRed(n) ? "R" : "B");
                }
                int column = row + 1;
                Cell(line, "2:1", Green, 36f, () => ClickOutside(RouletteBetKind.Column, column), "roulette-col-" + column);
            }
            VisualElement dozens = PhoneKit.Row(grid, Justify.FlexStart);
            for (int d = 1; d <= 3; d++)
            {
                int dozen = d;
                Cell(dozens, d == 1 ? "1st 12" : d == 2 ? "2nd 12" : "3rd 12", Green, 152f, () => ClickOutside(RouletteBetKind.Dozen, dozen), "roulette-dozen-" + d);
            }
            VisualElement evens = PhoneKit.Row(grid, Justify.FlexStart);
            foreach ((RouletteBetKind kind, string label, Color bg) in new[] { (RouletteBetKind.Low, "1-18", Green), (RouletteBetKind.Even, "EVEN", Green),
                         (RouletteBetKind.Red, "RED", Red), (RouletteBetKind.Black, "BLACK", Black), (RouletteBetKind.Odd, "ODD", Green), (RouletteBetKind.High, "19-36", Green) })
            {
                RouletteBetKind k = kind;
                Cell(evens, label, bg, 76f, () => ClickOutside(k), "roulette-" + k.ToString().ToLowerInvariant());
            }

            _betsLabel = PhoneKit.Label(_panel, "", 12f, CasinoUi.Gold);
            _betsLabel.name = "roulette-bets";
            _betsLabel.style.marginTop = 6f;
            VisualElement actions = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(actions, "roulette-undo", "Undo", Undo, () => !Spinning && _placed.Count > 0));
            _pills.Add(new CasinoUi.Pill(actions, "roulette-clear", "Clear", ClearBets, () => !Spinning && Table.Bets.Count > 0));
            _pills.Add(new CasinoUi.Pill(actions, "roulette-rebet", "Rebet", () => Rebet(), () => !Spinning && Table.Bets.Count == 0));
            _pills.Add(new CasinoUi.Pill(actions, "roulette-spin", "Spin", () => Spin(), () => !Spinning && Table.Staked >= Table.Rules.TableMin && !_view.Gliding, primary: true));
            _pills.Add(new CasinoUi.Pill(actions, "roulette-leave", "Leave (Esc)", Leave, () => !Spinning));
        }

        private void RefreshPanel()
        {
            RouletteRules r = Table.Rules;
            _title.text = $"{_name.ToUpperInvariant()} · {r.Name.ToUpperInvariant()} · {CasinoMoney.Whole(r.TableMin)} MIN · INSIDE {CasinoMoney.Whole(r.InsideMax)} · OUTSIDE {CasinoMoney.Whole(r.OutsideMax)}";
            decimal chips = _game.Casino.Account.Chips;
            if (Spinning) chips -= Table.LastReturned; // paid when the ball drops
            _chipsLabel.text = "Chips " + CasinoMoney.Cents(chips);
            _status.text = Spinning ? "No more bets…" : _message ?? "";
            _history.text = Table.History.Count == 0 ? "" : "Last: " + string.Join("  ", Table.History.Reverse().Take(12).Select(n => Roulette.Label(n) + (Roulette.IsGreen(n) ? "G" : Roulette.IsRed(n) ? "R" : "B")));
            _betsLabel.text = Table.Bets.Count == 0 ? "No bets on the layout." :
                string.Join(" · ", Table.Bets.Select(b => $"{b.Describe()} {CasinoMoney.Whole(b.Amount)}")) + $"   = {CasinoMoney.Whole(Table.Staked)}";
            foreach (var m in _modes) m.Value.Root.style.backgroundColor = m.Key == _mode ? CasinoUi.Primary : CasinoUi.Button;
            foreach (var c in _chipPills) PhoneKit.Border(c.Value.Root, c.Key == _chip ? 2f : 1f, c.Key == _chip ? Color.white : new Color(CasinoUi.Gold.r, CasinoUi.Gold.g, CasinoUi.Gold.b, 0.35f));
            foreach (var cell in _cells)
            {
                bool pending = cell.Key == _pendingSplit;
                bool hit = !Spinning && Table.LastPocket == cell.Key && Table.History.Count > 0;
                PhoneKit.Border(cell.Value, pending || hit ? 2f : 1f, pending ? Color.white : hit ? new Color(1f, 0.9f, 0.3f) : new Color(CasinoUi.Gold.r, CasinoUi.Gold.g, CasinoUi.Gold.b, 0.5f));
            }
            _pills.RefreshAll();
        }
    }

    public sealed class RouletteSeat : Interactable
    {
        private RouletteTableView _table;
        private int _seat;

        public void Configure(RouletteTableView table, int seat)
        {
            _table = table;
            _seat = seat;
        }

        public override string Prompt => _table.Prompt;
        public override bool CanInteract => base.CanInteract && _table.CanSit;
        public override void Interact() => _table.Sit(_seat);
    }
}
