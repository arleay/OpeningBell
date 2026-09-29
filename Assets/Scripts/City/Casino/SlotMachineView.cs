using System.Collections.Generic;
using OpeningBell.Casino;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// A playable slot machine (CASINO_SPEC §29–34). Sit at it: the view fills with the machine and its controls come
    /// up (line bet, spin or Space, max bet, the pay table). A spin is settled by <see cref="SlotMachine"/> before a
    /// reel moves; the reels here then spin and stop, left to right, on the stops it chose. The live reels are drawn
    /// over the cabinet's static ones only while someone plays.
    /// </summary>
    public sealed class SlotMachineView : MonoBehaviour
    {
        private const float SpinSeconds = 0.9f, ReelGap = 0.35f, StepSeconds = 0.05f;

        private GameBootstrap _game;
        private InteractionHud _hud;
        private Kit _kit;
        private SeatView _view;
        private Color _theme;
        private int[] _idle;

        private Transform _overlay;
        private Renderer[,] _reels;
        private TextMesh _info;
        private Renderer _candle;
        private Material _candleOn, _candleOff;

        private decimal _lineBet;
        private float _spinStarted = -1f;
        private int[] _shown = new int[3];
        private string _message;
        private bool _paytable;

        private VisualElement _panel;
        private Label _title, _chips, _status, _bet, _table, _jackpot;
        private readonly List<CasinoUi.Pill> _pills = new List<CasinoUi.Pill>();

        public SlotMachine Machine { get; private set; }
        public SlotDefinition Definition => Machine.Definition;
        public bool Seated => _view.Seated;
        public bool Spinning => _spinStarted >= 0f;
        public decimal LineBet => _lineBet;
        public string Message => _message;

        public static SlotMachineView Build(CityContext c, Transform dyn, Vector3 at, float yaw, SlotDefinition d, Color theme, int[] idle)
        {
            Transform root = Kit.Group(dyn, "Slot " + d.Name, at, yaw);
            var v = root.gameObject.AddComponent<SlotMachineView>();
            v._game = c.Game;
            v._hud = c.Hud;
            v._kit = c.Kit;
            v._theme = theme;
            v._idle = idle;
            v._shown = (int[])idle.Clone();
            v._view = new SeatView(c.Player);
            v.Machine = new SlotMachine(d, c.Game.Casino.Account, new SeededRandom(CasinoRandom.FreshSeed()), () => c.Game.Clock.Now,
                d.Progressive ? c.Game.Casino.Jackpot : null);
            v._lineBet = d.MinLineBet;
            var box = root.gameObject.AddComponent<BoxCollider>();
            box.isTrigger = true;
            box.center = new Vector3(0f, 1.1f, -0.55f);
            box.size = new Vector3(0.62f, 1.6f, 0.5f);
            root.gameObject.AddComponent<SlotSeat>().Configure(v);
            return v;
        }

        public string Prompt => $"Play {Definition.Name} · {CasinoMoney.Whole(Definition.MinBet)}–{CasinoMoney.Whole(Definition.MaxBet)}";

        public string Details
        {
            get
            {
                string vol = Definition.Volatility switch { Volatility.Low => "low", Volatility.High => "high", _ => "medium" };
                return $"{Definition.Theme} · {vol} volatility" + (Definition.Progressive ? $" · jackpot {CasinoMoney.Cents(_game.Casino.Jackpot.Pool)}" : "");
            }
        }

        public bool CanSit => !_view.Seated && !_view.Gliding;

        public void Sit()
        {
            if (!CanSit) return;
            BuildOverlay();
            Vector3 stand = transform.TransformPoint(new Vector3(0f, 0f, -1.05f));
            Vector3 eye = transform.TransformPoint(new Vector3(0f, 1.42f, -0.95f));
            Vector3 focus = transform.TransformPoint(new Vector3(0f, 1.34f, -0.26f));
            _view.Sit(this, stand, transform.eulerAngles.y, eye, focus);
            if (_lineBet * Definition.LineCount > _game.Casino.Account.Chips) _lineBet = Definition.MinLineBet;
            _message = _game.Casino.Account.Chips < Definition.MinBet ? "You need chips: the cashier's by the entrance." : "Choose a bet and spin.";
            ShowPanel(true);
            if (_game.Casino.FirstTime("slots"))
                _hud.ShowToast("Slots: three matching symbols on a payline win; cherries pay anywhere on the line. Every machine keeps a share of what's bet over time.", 8f);
        }

        public void Leave()
        {
            if (!Seated || Spinning) return;
            ShowPanel(false);
            _view.Stand(this);
        }

        public void ChangeBet(int step)
        {
            if (Spinning) return;
            _lineBet = System.Math.Max(Definition.MinLineBet, System.Math.Min(Definition.MaxLineBet, _lineBet + step));
            CasinoAudio.Play(CasinoArt.Button(), transform.position, 0.5f);
        }

        public void MaxBet()
        {
            if (Spinning) return;
            _lineBet = Definition.MaxLineBet;
        }

        public string Spin()
        {
            if (Spinning) return "Spinning.";
            string error = Machine.Spin(_lineBet);
            if (error != null)
            {
                _message = error;
                _hud.ShowToast(error, 3f);
                return error;
            }
            _message = null;
            _spinStarted = Time.time;
            CasinoAudio.Play(CasinoArt.Button(), transform.position, 0.5f);
            CasinoAudio.Play(CasinoArt.ReelSpin(), transform.position, 0.35f, loopFor: Duration);
            return null;
        }

        private float Duration => GameSettings.ReduceMotion ? 0.2f : SpinSeconds + ReelGap * 2f;

        private void BuildOverlay()
        {
            if (_overlay != null) return;
            _overlay = Kit.Group(transform, "Live reels");
            _reels = new Renderer[3, 3];
            for (int r = 0; r < 3; r++)
                for (int row = 0; row < 3; row++)
                    _reels[r, row] = _kit.Quad(_overlay, "Reel", CasinoProps.ReelSpot(r, row) + new Vector3(0f, 0f, -0.003f), CasinoProps.SymbolSize, 0f,
                        CasinoArt.Symbol(_kit.P, Definition.At(_shown[r] + row - 1))).GetComponent<Renderer>();
            _kit.Box(_overlay, "Info cover", new Vector3(0f, 1.62f, -0.2685f), new Vector3(0.5f, 0.11f, 0.002f), _kit.P.Unlit(new Color(0.03f, 0.06f, 0.16f)), collider: false);
            _info = _kit.Text(_overlay, "", new Vector3(0f, 1.62f, -0.272f), 0f, 0.022f, new Color(1f, 0.85f, 0.3f));
            _candleOn = _kit.P.Glow(Color.white, 2.5f);
            _candleOff = _kit.P.Glow(_theme, 1.6f);
            _candle = _kit.Box(_overlay, "Candle light", new Vector3(0f, 2.34f, 0f), new Vector3(0.065f, 0.225f, 0.065f), _candleOff, collider: false).GetComponent<Renderer>();
            foreach (Collider col in _overlay.GetComponentsInChildren<Collider>()) Destroy(col);
        }

        private void SetReel(int reel, int stop)
        {
            _shown[reel] = stop;
            for (int row = 0; row < 3; row++)
                _reels[reel, row].sharedMaterial = CasinoArt.Symbol(_kit.P, Definition.At(stop + row - 1));
        }

        private void Update()
        {
            if (_overlay == null) return;
            if (Spinning)
            {
                float t = Time.time - _spinStarted;
                bool reduced = GameSettings.ReduceMotion;
                for (int r = 0; r < 3; r++)
                {
                    float stopAt = reduced ? 0.2f : SpinSeconds + r * ReelGap;
                    if (t < stopAt)
                    {
                        // Turning: the strip steps down past the window.
                        int step = (int)(t / StepSeconds) + r * 7;
                        if (!reduced) SetReel(r, (_idle[r] + step) % Definition.Strip.Length);
                    }
                    else if (_shown[r] != Machine.Last.Stops[r])
                    {
                        SetReel(r, Machine.Last.Stops[r]);
                        CasinoAudio.Play(CasinoArt.ReelStop(), transform.position, 0.5f);
                    }
                }
                if (t >= Duration)
                {
                    for (int r = 0; r < 3; r++) SetReel(r, Machine.Last.Stops[r]);
                    _spinStarted = -1f;
                    Announce();
                }
            }
            // Win light: the candle flashes after a win (steady when flashing is reduced).
            bool lit = _winUntil > Time.time && (GameSettings.ReduceFlashing || (int)(Time.time * 6f) % 2 == 0);
            Material want = lit ? _candleOn : _candleOff;
            if (_candle.sharedMaterial != want) _candle.sharedMaterial = want;

            decimal chips = _game.Casino.Account.Chips;
            if (Spinning && Machine.Last != null) chips -= Machine.Last.Won; // winnings show when the reels stop
            string info = $"CREDIT {CasinoMoney.Cents(chips)}   BET {CasinoMoney.Whole(_lineBet * Definition.LineCount)}";
            if (!Spinning && Machine.Last != null && Machine.Last.Won > 0m) info += $"   WIN {CasinoMoney.Cents(Machine.Last.Won)}";
            if (_info.text != info) _info.text = info;

            if (Seated && !_view.Gliding)
            {
                if (_view.BackPressed) Leave();
                else if (Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame) Spin();
            }
            if (_panel != null && _panel.style.display == DisplayStyle.Flex) RefreshPanel();
        }

        private float _winUntil;

        private void Announce()
        {
            SlotSpin s = Machine.Last;
            if (s.Won <= 0m)
            {
                _message = "No win.";
                return;
            }
            bool big = s.Jackpot > 0m || s.Won >= s.Bet * 50m;
            _message = s.Jackpot > 0m ? $"JACKPOT! {CasinoMoney.Cents(s.Jackpot)}" : big ? $"Big win: {CasinoMoney.Cents(s.Won)}" : $"Win {CasinoMoney.Cents(s.Won)}";
            _winUntil = Time.time + (big ? 4f : 1.5f);
            CasinoAudio.Play(CasinoArt.Win(big), transform.position, big ? 0.7f : 0.45f);
            if (s.Jackpot > 0m) _hud.ShowToast($"Diamond Dusk progressive: {CasinoMoney.Cents(s.Jackpot)}!", 6f);
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
            _panel = CasinoUi.Frame(root, "slot-panel", 500f);
            PhoneKit.Absolute(_panel, left: 24f, bottom: 24f);
            VisualElement top = PhoneKit.Row(_panel);
            _title = CasinoUi.Heading(top, "");
            _chips = PhoneKit.Label(top, "", 15f, PhoneKit.Text, true);
            _chips.name = "slot-chips";
            _status = PhoneKit.Label(_panel, "", 18f, Color.white, true);
            _status.name = "slot-status";
            _status.style.marginTop = 6f;
            _jackpot = PhoneKit.Label(_panel, "", 14f, new Color(0.5f, 0.85f, 1f), true);
            _bet = PhoneKit.Label(_panel, "", 15f, CasinoUi.Gold, true);
            _bet.name = "slot-bet";
            _bet.style.marginTop = 6f;
            VisualElement row = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(row, "slot-bet-down", "Bet −", () => ChangeBet(-1), () => !Spinning && _lineBet > Definition.MinLineBet));
            _pills.Add(new CasinoUi.Pill(row, "slot-bet-up", "Bet +", () => ChangeBet(1), () => !Spinning && _lineBet < Definition.MaxLineBet));
            _pills.Add(new CasinoUi.Pill(row, "slot-max", "Max bet", MaxBet, () => !Spinning));
            _pills.Add(new CasinoUi.Pill(row, "slot-spin", "Spin (Space)", () => Spin(), () => !Spinning && !_view.Gliding, primary: true));
            VisualElement row2 = CasinoUi.Wrap(_panel);
            _pills.Add(new CasinoUi.Pill(row2, "slot-paytable", "Pay table", () => _paytable = !_paytable));
            _pills.Add(new CasinoUi.Pill(row2, "slot-leave", "Leave (Esc)", Leave, () => !Spinning));
            _table = PhoneKit.Label(_panel, "", 12f, PhoneKit.Muted);
            _table.name = "slot-paytable-text";
            _table.style.marginTop = 6f;
        }

        private static readonly Dictionary<string, double> RtpCache = new Dictionary<string, double>();

        /// <summary>The machine's exact return, computed once (it enumerates every stop combination).</summary>
        private static double Rtp(SlotDefinition d)
        {
            if (!RtpCache.TryGetValue(d.Id, out double rtp)) RtpCache[d.Id] = rtp = SlotMath.Compute(d).Rtp;
            return rtp;
        }

        private void RefreshPanel()
        {
            SlotDefinition d = Definition;
            _title.text = $"{d.Name.ToUpperInvariant()} · {d.LineCount} LINE{(d.LineCount > 1 ? "S" : "")} · {d.Volatility.ToString().ToUpperInvariant()} VOLATILITY";
            decimal chips = _game.Casino.Account.Chips;
            if (Spinning && Machine.Last != null) chips -= Machine.Last.Won;
            _chips.text = "Chips " + CasinoMoney.Cents(chips);
            _status.text = Spinning ? "Spinning…" : _message ?? "";
            _bet.text = $"{CasinoMoney.Whole(_lineBet)} a line × {d.LineCount} = {CasinoMoney.Whole(_lineBet * d.LineCount)} a spin";
            _jackpot.text = d.Progressive ? $"Progressive jackpot {CasinoMoney.Cents(_game.Casino.Jackpot.Pool)} (3 diamonds at max bet)" : "";
            _jackpot.style.display = d.Progressive ? DisplayStyle.Flex : DisplayStyle.None;
            if (_paytable)
            {
                string top = d.TopSymbol == SlotSymbol.Diamond ? "◆ ◆ ◆" : "7 7 7";
                _table.text = $"{top}  {d.ThreeOfAKind[d.TopSymbol]}×   ·   3BAR ×3  {d.ThreeOfAKind[SlotSymbol.TripleBar]}×   ·   2BAR ×3  {d.ThreeOfAKind[SlotSymbol.DoubleBar]}×   ·   BAR ×3  {d.ThreeOfAKind[SlotSymbol.Bar]}×\n" +
                              $"Any 3 bars  {d.AnyBar}×   ·   Cherries: 3 = {d.Cherries[3]}×, 2 = {d.Cherries[2]}×, 1 = {d.Cherries[1]}×   (× the line bet)\n" +
                              $"Returns about {Rtp(d):P1} of what's bet over the long run.";
                _table.style.display = DisplayStyle.Flex;
            }
            else _table.style.display = DisplayStyle.None;
            _pills.RefreshAll();
        }
    }

    public sealed class SlotSeat : Interactable
    {
        private SlotMachineView _machine;
        public void Configure(SlotMachineView machine) => _machine = machine;
        public override string Prompt => _machine.Prompt;
        public override string Details => _machine.Details;
        public override bool CanInteract => base.CanInteract && _machine.CanSit;
        public override void Interact() => _machine.Sit();
    }
}
