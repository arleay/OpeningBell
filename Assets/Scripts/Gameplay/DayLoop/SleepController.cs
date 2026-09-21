using System;
using System.Collections;
using System.Globalization;
using OpeningBell.Trading;
using UnityEngine;
using UnityEngine.UIElements;
using Position = UnityEngine.UIElements.Position;

namespace OpeningBell.Gameplay
{
    public enum SleepState
    {
        Awake,
        FallingAsleep,
        Asleep,
        Waking,
    }

    /// <summary>
    /// Sleep: fade out → simulate through the night → recap the finished day → wake at the next trading day's
    /// morning beside the bed. Owns a full-screen overlay on the HUD panel.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class SleepController : MonoBehaviour
    {
        [SerializeField] private GameBootstrap game;
        [SerializeField] private FirstPersonController player;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private Transform wakePoint;
        [SerializeField] private int earliestBedtimeHour = 16;
        [SerializeField] private int wakeHour = 6;
        [SerializeField] private float fadeSeconds = 0.8f;
        [Tooltip("Real seconds the morning recap stays up.")]
        [SerializeField] private float recapSeconds = 3f;

        private VisualElement _overlay;
        private Label _title, _body, _morning;
        private int _recappedDays;

        public SleepState State { get; private set; } = SleepState.Awake;
        public string RecapText => _title.text + "\n" + _body.text + "\n" + _morning.text;

        public TimeSpan EarliestBedtime => TimeSpan.FromHours(earliestBedtimeHour);
        public bool CanSleep => State == SleepState.Awake &&
                                SleepRules.CanSleep(game.Clock.Now, EarliestBedtime, TimeSpan.FromHours(wakeHour));

        private void Start()
        {
            _recappedDays = game.Days.Completed.Count; // a loaded game has already recapped its past days
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;

            _overlay = new VisualElement { pickingMode = PickingMode.Ignore };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = _overlay.style.right = _overlay.style.top = _overlay.style.bottom = 0;
            _overlay.style.backgroundColor = new Color(0.02f, 0.02f, 0.035f, 1f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            _overlay.style.opacity = 0f;
            _overlay.style.display = DisplayStyle.None;
            root.Add(_overlay);

            _title = Text(26, new Color(0.88f, 0.66f, 0.23f), FontStyle.Bold);
            _body = Text(18, new Color(0.84f, 0.86f, 0.89f), FontStyle.Normal);
            _morning = Text(16, new Color(0.49f, 0.53f, 0.58f), FontStyle.Normal);
            _body.style.marginTop = 10;
            _morning.style.marginTop = 26;
        }

        public void Sleep()
        {
            if (CanSleep) StartCoroutine(SleepRoutine());
        }

        private IEnumerator SleepRoutine()
        {
            State = SleepState.FallingAsleep;
            player.ControlEnabled = false;
            interactor.enabled = false;
            _title.text = _body.text = _morning.text = "";
            _overlay.style.display = DisplayStyle.Flex;
            yield return Fade(0f, 1f);

            State = SleepState.Asleep;
            DateTime wake = SleepRules.NextWake(game.Clock.Now, TimeSpan.FromHours(wakeHour), game.Market.Schedule);
            game.SkipTo(wake);

            // Recap the latest finished day unless an earlier sleep already showed it.
            var days = game.Days.Completed;
            ShowRecap(days.Count > _recappedDays ? days[days.Count - 1] : null, wake);
            _recappedDays = days.Count;
            player.PlaceAt(wakePoint.position, wakePoint.eulerAngles.y);
            game.Save(); // autosave: end of day

            float until = Time.realtimeSinceStartup + recapSeconds;
            while (Time.realtimeSinceStartup < until) yield return null;

            State = SleepState.Waking;
            yield return Fade(1f, 0f);
            _overlay.style.display = DisplayStyle.None;
            interactor.enabled = true;
            player.ControlEnabled = true;
            State = SleepState.Awake;
        }

        private void ShowRecap(TradingDayReport day, DateTime wake)
        {
            var c = CultureInfo.InvariantCulture;
            if (day != null)
            {
                _title.text = $"Day {day.DayNumber} complete";
                string sign = day.NetPnL >= 0 ? "+" : "-";
                _body.text = $"P&L {sign}${Math.Abs(day.NetPnL).ToString("N2", c)}   ·   {day.Fills} fills   ·   " +
                             $"{day.Winners}W / {day.Losers}L   ·   Equity ${day.EndEquity.ToString("N2", c)}";
            }
            else
            {
                _title.text = "You slept";
                _body.text = "";
            }
            _morning.text = wake.ToString("dddd, MMMM d  ·  h:mm tt", c);
        }

        private IEnumerator Fade(float from, float to)
        {
            for (float t = 0f; t < fadeSeconds; t += Time.unscaledDeltaTime)
            {
                _overlay.style.opacity = Mathf.Lerp(from, to, t / fadeSeconds);
                yield return null;
            }
            _overlay.style.opacity = to;
        }

        private Label Text(int size, Color color, FontStyle style)
        {
            var label = new Label { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            label.style.unityFontStyleAndWeight = style;
            _overlay.Add(label);
            return label;
        }
    }
}
