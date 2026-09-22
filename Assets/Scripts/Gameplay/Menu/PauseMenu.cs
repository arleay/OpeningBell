using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// Esc while walking around: pauses game time and offers Resume, Save, Settings, New Game (confirmed) and Quit.
    /// Not reachable while seated (Esc stands up there), which keeps saving "outside active trading" (spec §41).
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PauseMenu : MonoBehaviour
    {
        [SerializeField] private GameBootstrap game;
        [SerializeField] private GameInput input;
        [SerializeField] private FirstPersonController player;
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField] private SleepController sleep;

        private VisualElement _overlay, _main, _settings, _confirm;
        private Label _status;
        private bool _wasPaused;
        private int _closedFrame = -1;

        public bool IsOpen { get; private set; }
        public VisualElement Root { get; private set; }

        private void Start()
        {
            Root = GetComponent<UIDocument>().rootVisualElement;
            Root.style.flexGrow = 1;
            Root.pickingMode = PickingMode.Ignore;

            _overlay = new VisualElement { name = "pause-menu" };
            Fill(_overlay);
            _overlay.style.backgroundColor = new Color(0.02f, 0.03f, 0.04f, 0.72f);
            _overlay.style.alignItems = Align.Center;
            _overlay.style.justifyContent = Justify.Center;
            Root.Add(_overlay);

            VisualElement card = Card();
            _overlay.Add(card);
            card.Add(Text("PAUSED", 26, new Color(0.88f, 0.66f, 0.23f), bold: true));
            _status = Text("", 13, new Color(0.6f, 0.65f, 0.7f));
            _status.style.marginBottom = 12;
            card.Add(_status);

            _main = Group(card);
            _main.Add(MenuButton("RESUME", Close, "menu-resume"));
            _main.Add(MenuButton("SAVE GAME", SaveGame, "menu-save"));
            _main.Add(MenuButton("SETTINGS", () => ShowPage(_settings), "menu-settings"));
            _main.Add(MenuButton("NEW GAME", () => ShowPage(_confirm), "menu-new"));
            _main.Add(MenuButton("QUIT", Quit, "menu-quit"));

            _settings = Group(card);
            _settings.Add(SettingSlider("Mouse sensitivity", 0.2f, 3f, GameSettings.MouseSensitivity, v => GameSettings.MouseSensitivity = v, "setting-sensitivity"));
            _settings.Add(SettingSlider("Master volume", 0f, 1f, GameSettings.MasterVolume, v => GameSettings.MasterVolume = v, "setting-volume"));
            _settings.Add(MenuButton("BACK", () => ShowPage(_main), "menu-back"));

            _confirm = Group(card);
            Label warning = Text("Start over? Your saved game will be deleted.", 15, Color.white);
            warning.style.whiteSpace = WhiteSpace.Normal;
            warning.style.marginBottom = 10;
            _confirm.Add(warning);
            _confirm.Add(MenuButton("DELETE SAVE AND START OVER", game.StartNewGame, "menu-confirm-new"));
            _confirm.Add(MenuButton("CANCEL", () => ShowPage(_main), "menu-cancel"));

            _overlay.style.display = DisplayStyle.None;
        }

        private void Update()
        {
            if (!IsOpen && Time.frameCount != _closedFrame && input.OpenMenu.WasPressedThisFrame() && sleep.State == SleepState.Awake)
                Open();
            else if (IsOpen && input.CloseMenu.WasPressedThisFrame())
                Close();
        }

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            _wasPaused = game.IsPaused;
            game.IsPaused = true;
            // Physics too: a car mid-drive must not roll on under the menu. (UI and the clock use unscaled time.)
            Time.timeScale = 0f;
            player.ControlEnabled = false;
            interactor.enabled = false;
            input.UseMenuControls();
            _status.text = $"{game.Clock.Now.ToString("dddd h:mm tt", CultureInfo.InvariantCulture)} · Day {game.Days.DayNumber}";
            ShowPage(_main);
            _overlay.style.display = DisplayStyle.Flex;
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            _closedFrame = Time.frameCount; // the same Esc press must not reopen it
            _overlay.style.display = DisplayStyle.None;
            input.UsePlayerControls();
            interactor.enabled = true;
            player.ControlEnabled = true;
            game.IsPaused = _wasPaused;
            Time.timeScale = 1f;
        }

        private void SaveGame()
        {
            try
            {
                game.Save();
                _status.text = $"Saved · {game.Clock.Now.ToString("dddd h:mm tt", CultureInfo.InvariantCulture)}";
            }
            catch (Exception e)
            {
                _status.text = "Save failed: " + e.Message;
                Debug.LogException(e);
            }
        }

        private static void Quit()
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit(); // autosaves via GameBootstrap.OnApplicationQuit
#endif
        }

        private void ShowPage(VisualElement page)
        {
            foreach (VisualElement p in new[] { _main, _settings, _confirm })
                p.style.display = p == page ? DisplayStyle.Flex : DisplayStyle.None;
        }

        // ---- inline styling (the HUD panel has no stylesheet) ----

        private static void Fill(VisualElement e)
        {
            e.style.position = Position.Absolute;
            e.style.left = e.style.right = e.style.top = e.style.bottom = 0;
        }

        private static VisualElement Card()
        {
            var card = new VisualElement();
            card.style.width = 420;
            card.style.paddingLeft = card.style.paddingRight = 28;
            card.style.paddingTop = card.style.paddingBottom = 24;
            card.style.backgroundColor = new Color(0.086f, 0.11f, 0.14f, 1f);
            card.style.borderTopWidth = card.style.borderBottomWidth = card.style.borderLeftWidth = card.style.borderRightWidth = 1;
            card.style.borderTopColor = card.style.borderBottomColor = card.style.borderLeftColor = card.style.borderRightColor =
                new Color(0.88f, 0.66f, 0.23f, 1f);
            card.style.borderTopLeftRadius = card.style.borderTopRightRadius =
                card.style.borderBottomLeftRadius = card.style.borderBottomRightRadius = 6;
            return card;
        }

        private static VisualElement Group(VisualElement parent)
        {
            var group = new VisualElement();
            parent.Add(group);
            return group;
        }

        private static Label Text(string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text);
            label.style.fontSize = size;
            label.style.color = color;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            return label;
        }

        private static Button MenuButton(string text, Action onClick, string name)
        {
            var button = new Button(onClick) { text = text, name = name };
            button.style.height = 40;
            button.style.marginTop = button.style.marginBottom = 4;
            button.style.marginLeft = button.style.marginRight = 0;
            button.style.fontSize = 15;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            button.style.color = new Color(0.84f, 0.86f, 0.89f);
            button.style.backgroundColor = new Color(0.11f, 0.14f, 0.17f);
            return button;
        }

        private static VisualElement SettingSlider(string label, float min, float max, float value, Action<float> onChange, string name)
        {
            var slider = new Slider(label, min, max) { value = value, name = name };
            slider.style.marginTop = slider.style.marginBottom = 8;
            slider.labelElement.style.color = new Color(0.84f, 0.86f, 0.89f);
            slider.labelElement.style.minWidth = 150;
            slider.RegisterValueChangedCallback(e => onChange(e.newValue));
            return slider;
        }
    }
}
