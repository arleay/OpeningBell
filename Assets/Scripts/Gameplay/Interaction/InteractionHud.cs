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
        private Label _prompt, _clock, _toast, _subtitle, _details, _status;
        private float _toastUntil, _subtitleUntil;
        private long _clockMinute = -1;
        private string _shownPrompt, _shownDetails;

        public string PromptText => _prompt.style.display == DisplayStyle.None ? "" : _prompt.text;
        public string ClockText => _clock.text;
        public string ToastText => _toast.style.display == DisplayStyle.None ? "" : _toast.text;
        public string SubtitleText => _subtitle.style.display == DisplayStyle.None ? "" : _subtitle.text;
        public string DetailsText => _details.style.display == DisplayStyle.None ? "" : _details.text;
        public string StatusText => _status.style.display == DisplayStyle.None ? "" : _status.text;

        /// <summary>Persistent line at the bottom left (speed and battery while riding). Null hides it.</summary>
        public void SetStatus(string text)
        {
            if (_status == null) return;
            bool show = !string.IsNullOrEmpty(text);
            _status.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (show && _status.text != text) _status.text = text;
        }

        /// <summary>Spoken line from someone nearby (receptionist, shop staff). Replaces the previous line.</summary>
        public void ShowSubtitle(string speaker, string line, float seconds = 4.5f)
        {
            _subtitle.text = $"<b>{speaker}:</b> {line}";
            _subtitle.style.display = DisplayStyle.Flex;
            _subtitleUntil = Time.realtimeSinceStartup + seconds;
        }

        /// <summary>Short message at the bottom of the screen; messages arriving together stack (newest last).</summary>
        public void ShowToast(string message, float seconds = 6f)
        {
            bool visible = _toast.style.display == DisplayStyle.Flex && Time.realtimeSinceStartup < _toastUntil;
            string[] lines = visible ? _toast.text.Split('\n') : System.Array.Empty<string>();
            int keep = System.Math.Min(lines.Length, 2);
            _toast.text = (keep > 0 ? string.Join("\n", lines, lines.Length - keep, keep) + "\n" : "") + message;
            _toast.style.display = DisplayStyle.Flex;
            _toastUntil = Time.realtimeSinceStartup + seconds;
        }

        private void Update()
        {
            if (_prompt == null) return;
            if (_toast.style.display == DisplayStyle.Flex && Time.realtimeSinceStartup >= _toastUntil)
                _toast.style.display = DisplayStyle.None;
            if (_subtitle.style.display == DisplayStyle.Flex && Time.realtimeSinceStartup >= _subtitleUntil)
                _subtitle.style.display = DisplayStyle.None;
            // Prompts can change while in focus (e.g. the bed at 4 PM).
            if (interactor.Current != null && (interactor.Current.Prompt != _shownPrompt || interactor.Current.Details != _shownDetails))
                OnFocusChanged(interactor.Current);

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

            _toast = new Label { pickingMode = PickingMode.Ignore, name = "hud-toast" };
            _toast.style.position = Position.Absolute;
            _toast.style.bottom = 60;
            _toast.style.color = Color.white;
            _toast.style.fontSize = 18;
            _toast.style.backgroundColor = new Color(0.08f, 0.1f, 0.13f, 0.85f);
            _toast.style.paddingLeft = _toast.style.paddingRight = 14;
            _toast.style.paddingTop = _toast.style.paddingBottom = 8;
            _toast.style.unityTextAlign = TextAnchor.MiddleCenter;
            _toast.style.display = DisplayStyle.None;
            SetRadius(_toast, 5);
            _root.Add(_toast);

            _subtitle = new Label { pickingMode = PickingMode.Ignore, name = "hud-subtitle", enableRichText = true };
            _subtitle.style.position = Position.Absolute;
            _subtitle.style.bottom = 150;
            _subtitle.style.maxWidth = Length.Percent(60);
            _subtitle.style.whiteSpace = WhiteSpace.Normal;
            _subtitle.style.color = new Color(0.95f, 0.93f, 0.86f);
            _subtitle.style.fontSize = 19;
            _subtitle.style.unityTextAlign = TextAnchor.MiddleCenter;
            _subtitle.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.9f);
            _subtitle.style.unityTextOutlineWidth = 0.6f;
            _subtitle.style.display = DisplayStyle.None;
            _root.Add(_subtitle);

            _details = new Label { pickingMode = PickingMode.Ignore, name = "hud-details" };
            _details.style.position = Position.Absolute;
            _details.style.top = Length.Percent(59);
            _details.style.maxWidth = 460;
            _details.style.whiteSpace = WhiteSpace.Normal;
            _details.style.color = new Color(0.85f, 0.87f, 0.9f);
            _details.style.fontSize = 14;
            _details.style.backgroundColor = new Color(0f, 0f, 0f, 0.5f);
            _details.style.paddingLeft = _details.style.paddingRight = 10;
            _details.style.paddingTop = _details.style.paddingBottom = 5;
            _details.style.display = DisplayStyle.None;
            SetRadius(_details, 4);
            _root.Add(_details);

            _status = new Label { pickingMode = PickingMode.Ignore, name = "hud-status" };
            _status.style.position = Position.Absolute;
            _status.style.left = 24;
            _status.style.bottom = 22;
            _status.style.color = new Color(1f, 1f, 1f, 0.9f);
            _status.style.fontSize = 17;
            _status.style.unityFontStyleAndWeight = FontStyle.Bold;
            _status.style.unityTextOutlineColor = new Color(0f, 0f, 0f, 0.8f);
            _status.style.unityTextOutlineWidth = 0.5f;
            _status.style.display = DisplayStyle.None;
            _root.Add(_status);

            game.Inbox.Received += email => ShowToast($"New email: {email.Subject}   (computer: MAIL)");
            var c = System.Globalization.CultureInfo.InvariantCulture;
            game.Economy.TransactionPosted += tx =>
            {
                if (!tx.IsNotable) return;
                ShowToast($"{tx.Description}  {(tx.Amount < 0 ? "-" : "+")}${System.Math.Abs(tx.Amount).ToString("N2", c)}" +
                          $"   ·   Bank {(tx.BalanceAfter < 0 ? "-$" : "$")}{System.Math.Abs(tx.BalanceAfter).ToString("N2", c)}");
            };

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
            _shownDetails = target?.Details;
            _details.style.display = string.IsNullOrEmpty(_shownDetails) ? DisplayStyle.None : DisplayStyle.Flex;
            if (_shownDetails != null) _details.text = _shownDetails;
        }

        private static void SetRadius(VisualElement e, float r) =>
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius =
                e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
    }
}
