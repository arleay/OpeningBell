using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace OpeningBell.Gameplay
{
    /// <summary>Crosshair dot and "[E] Sit"-style prompt. Hidden while at the workstation.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class InteractionHud : MonoBehaviour
    {
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private WorkstationController workstation;
        [SerializeField] private GameInput input;
        [SerializeField] private GameBootstrap game;

        private VisualElement _root;
        private Label _prompt, _clock;
        private long _clockMinute = -1;
        private string _shownPrompt;

        public string PromptText => _prompt.style.display == DisplayStyle.None ? "" : _prompt.text;
        public string ClockText => _clock.text;

        private void Update()
        {
            if (_prompt == null) return;
            // Prompts can change while in focus (e.g. the bed at 4 PM).
            if (interactor.Current != null && interactor.Current.Prompt != _shownPrompt) OnFocusChanged(interactor.Current);

            long minute = game.Clock.Now.Ticks / System.TimeSpan.TicksPerMinute;
            if (minute == _clockMinute) return;
            _clockMinute = minute;
            string session = game.Market.Session switch
            {
                OpeningBell.Market.MarketSession.Premarket => "PRE-MARKET",
                OpeningBell.Market.MarketSession.Regular => "MARKET OPEN",
                OpeningBell.Market.MarketSession.AfterHours => "AFTER HOURS",
                _ => "MARKET CLOSED",
            };
            _clock.text = $"DAY {game.Days.DayNumber}   {game.Clock.Now.ToString("ddd h:mm tt", System.Globalization.CultureInfo.InvariantCulture)}   {session}";
        }

        private void Start()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            _root.pickingMode = PickingMode.Ignore;
            _root.style.flexGrow = 1;
            _root.style.alignItems = Align.Center;
            _root.style.justifyContent = Justify.Center;

            var dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.style.width = 6;
            dot.style.height = 6;
            dot.style.backgroundColor = new Color(1f, 1f, 1f, 0.85f);
            SetRadius(dot, 3);
            _root.Add(dot);

            _prompt = new Label { pickingMode = PickingMode.Ignore };
            _prompt.style.position = Position.Absolute;
            _prompt.style.top = Length.Percent(54);
            _prompt.style.color = Color.white;
            _prompt.style.fontSize = 20;
            _prompt.style.unityFontStyleAndWeight = FontStyle.Bold;
            _prompt.style.backgroundColor = new Color(0f, 0f, 0f, 0.55f);
            _prompt.style.paddingLeft = _prompt.style.paddingRight = 10;
            _prompt.style.paddingTop = _prompt.style.paddingBottom = 4;
            SetRadius(_prompt, 4);
            _root.Add(_prompt);

            _clock = new Label { pickingMode = PickingMode.Ignore, name = "hud-clock" };
            _clock.style.position = Position.Absolute;
            _clock.style.top = 18;
            _clock.style.right = 24;
            _clock.style.color = new Color(1f, 1f, 1f, 0.85f);
            _clock.style.fontSize = 18;
            _clock.style.unityFontStyleAndWeight = FontStyle.Bold;
            _root.Add(_clock);

            interactor.FocusChanged += OnFocusChanged;
            workstation.StateChanged += state =>
                _root.style.display = state == WorkstationState.Standing ? DisplayStyle.Flex : DisplayStyle.None;
            OnFocusChanged(interactor.Current);
        }

        private void OnFocusChanged(Interactable target)
        {
            _prompt.style.display = target != null ? DisplayStyle.Flex : DisplayStyle.None;
            _shownPrompt = target?.Prompt;
            if (target != null) _prompt.text = $"[{input.Interact.GetBindingDisplayString()}] {_shownPrompt}";
        }

        private static void SetRadius(VisualElement e, float r) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius =
                e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
    }
}
