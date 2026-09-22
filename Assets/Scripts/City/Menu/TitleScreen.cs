using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// The start-up screen: the game's title with Continue / New Game / Quit, and a character creator (body and
    /// outfit, skin, hair and top colours, a name) with the character turning on a lit stage beside the menu. The
    /// world waits paused behind it. Shown once per launch (a new game reloads the scene straight into play); never in
    /// batch mode, so tests and headless runs aren't blocked.
    /// </summary>
    public sealed class TitleScreen : MonoBehaviour
    {
        private static bool _shownThisLaunch;

        private static readonly Color Ink = new Color(0.95f, 0.94f, 0.9f), Dim = new Color(0.7f, 0.72f, 0.76f);
        private static readonly Color Accent = new Color(0.98f, 0.76f, 0.2f), Panel = new Color(0.07f, 0.08f, 0.1f, 0.94f);

        private GameBootstrap _game;
        private FirstPersonController _player;
        private InteractionHud _hud;
        private CityArt _art;
        private PlayerBody _body;
        private PauseMenu _pause;

        private VisualElement _overlay, _menu, _creator;
        private Label _bodyLabel, _outfitLabel;
        private TextField _name;
        private readonly List<(VisualElement Swatch, int Index, int Group)> _swatches = new List<(VisualElement, int, int)>();
        private RenderTexture _stageTexture;
        private Camera _stageCamera;
        private Transform _stage, _turntable;
        private GameObject _preview;
        private OpeningBell.PlayerLook _look;
        private bool _wantOpen;

        public bool Visible => _overlay != null && _overlay.style.display != DisplayStyle.None;
        public OpeningBell.PlayerLook Look => _look;

        public void Configure(GameBootstrap game, FirstPersonController player, InteractionHud hud, CityArt art, PlayerBody body)
        {
            _game = game;
            _player = player;
            _hud = hud;
            _art = art;
            _body = body;
            _pause = FindAnyObjectByType<PauseMenu>();
            _look = game.Look != null ? game.Look.Copy() : DefaultLook();
        }

        private void Start()
        {
            if (Application.isBatchMode || _shownThisLaunch || _art == null || !_art.HasPeople) return;
            _shownThisLaunch = true;
            Open();
        }

        /// <summary>Shows the title menu (it waits for the HUD's UI to exist).</summary>
        public void Open()
        {
            _wantOpen = true;
            _player.ControlEnabled = false;
            _game.IsPaused = true;
            if (_pause != null) _pause.enabled = false;
        }

        private void Update()
        {
            if (_wantOpen && _hud.Root != null)
            {
                _wantOpen = false;
                if (_overlay == null) Build(_hud.Root);
                _overlay.style.display = DisplayStyle.Flex;
                _overlay.BringToFront();
                ShowMenu();
            }
            if (Visible && _turntable != null) _turntable.Rotate(0f, 12f * Time.unscaledDeltaTime, 0f);
        }

        private void Close()
        {
            _overlay.style.display = DisplayStyle.None;
            if (_stageCamera != null) _stageCamera.enabled = false;
            _player.ControlEnabled = true;
            _game.IsPaused = false;
            if (_pause != null) _pause.enabled = true;
        }

        // ------------------------------------------------------------------ actions

        public void Continue() => Close();

        public void ShowCreator()
        {
            _menu.style.display = DisplayStyle.None;
            _creator.style.display = DisplayStyle.Flex;
            RefreshCreator();
        }

        /// <summary>Starts playing as the character: a continued game restarts fresh (the save is replaced).</summary>
        public void StartGame()
        {
            _look.Name = string.IsNullOrWhiteSpace(_name.value) ? "Alex" : _name.value.Trim();
            _look.ModelLabel = _art.PeopleLabel(_look.Model);
            if (_game.Continued)
            {
                OpeningBell.GameBootstrap.PendingLook = _look.Copy();
                Close();
                _game.StartNewGame();
                return;
            }
            _game.Look = _look.Copy();
            if (_body != null) _body.SetLook(_game.Look);
            Close();
        }

        /// <summary>Steps a creator option: 0 body, 1 outfit.</summary>
        public void Step(int option, int by)
        {
            List<int> bodies = BodyTypes();
            string body = BodyOf(_look.Model);
            if (option == 0)
            {
                int b = (bodies.IndexOf(bodies.Find(i => BodyOf(i) == body)) + by + bodies.Count) % bodies.Count;
                _look.Model = bodies[b];
            }
            else
            {
                List<int> outfits = Outfits(body);
                int o = (outfits.IndexOf(_look.Model) + by + outfits.Count) % outfits.Count;
                _look.Model = outfits[o];
            }
            RefreshCreator();
        }

        /// <summary>Picks a colour: 0 skin, 1 hair, 2 top.</summary>
        public void Pick(int group, int index)
        {
            if (group == 0) _look.Skin = index;
            else if (group == 1) _look.Hair = index;
            else _look.Top = index;
            RefreshCreator();
        }

        // ------------------------------------------------------------------ people

        private OpeningBell.PlayerLook DefaultLook()
        {
            var look = new OpeningBell.PlayerLook();
            for (int i = 0; i < _art.People.Count; i++)
                if (_art.PeopleLabel(i).EndsWith("/Casual2") || _art.People[i].name == "Casual2") { look.Model = i; break; }
            return look;
        }

        private string BodyOf(int person)
        {
            string label = _art.PeopleLabel(person);
            int slash = label.IndexOf('/');
            return slash > 0 ? label.Substring(0, slash) : "";
        }

        /// <summary>The first person of each body type (Men, Women).</summary>
        private List<int> BodyTypes()
        {
            var first = new List<int>();
            var seen = new HashSet<string>();
            for (int i = 0; i < _art.People.Count; i++)
                if (seen.Add(BodyOf(i))) first.Add(i);
            return first;
        }

        private List<int> Outfits(string body)
        {
            var list = new List<int>();
            for (int i = 0; i < _art.People.Count; i++)
                if (BodyOf(i) == body) list.Add(i);
            return list;
        }

        private void RefreshCreator()
        {
            string body = BodyOf(_look.Model);
            if (_bodyLabel != null) _bodyLabel.text = body == "Women" ? "Feminine" : body == "Men" ? "Masculine" : body;
            if (_outfitLabel != null) _outfitLabel.text = _art.People[_look.Model].name;
            foreach (var (swatch, index, group) in _swatches)
            {
                int chosen = group == 0 ? _look.Skin : group == 1 ? _look.Hair : _look.Top;
                Color edge = index == chosen ? Accent : new Color(1f, 1f, 1f, 0.15f);
                swatch.style.borderTopColor = swatch.style.borderBottomColor = swatch.style.borderLeftColor = swatch.style.borderRightColor = edge;
            }
            ShowPreview();
        }

        // ------------------------------------------------------------------ stage

        private void ShowPreview()
        {
            if (_stage == null) BuildStage();
            if (_preview != null) Destroy(_preview);
            _preview = Instantiate(_art.People[CharacterStyle.ModelIndex(_art, _look)], _turntable, false);
            _preview.name = "Preview";
            CharacterStyle.Apply(_preview, _look);
            var animator = _preview.GetComponent<Animator>();
            animator.runtimeAnimatorController = _art.PeopleAnimator;
            animator.applyRootMotion = false;
            animator.updateMode = AnimatorUpdateMode.UnscaledTime; // lively even while the world is paused
            _stageCamera.enabled = true;
        }

        /// <summary>A small lit stage far below the town, filmed into the menu's picture.</summary>
        private void BuildStage()
        {
            _stage = Kit.Group(transform, "Title stage", new Vector3(0f, -400f, 0f));
            _turntable = Kit.Group(_stage, "Turntable");
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Destroy(disc.GetComponent<Collider>());
            disc.transform.SetParent(_stage, false);
            disc.transform.localPosition = new Vector3(0f, -0.03f, 0f);
            disc.transform.localScale = new Vector3(1.8f, 0.03f, 1.8f);
            disc.GetComponent<Renderer>().material.color = new Color(0.16f, 0.17f, 0.2f);

            Light Lamp(Vector3 at, float intensity, Color color)
            {
                var light = new GameObject("Stage light").AddComponent<Light>();
                light.transform.SetParent(_stage, false);
                light.transform.localPosition = at;
                light.type = LightType.Point;
                light.range = 8f;
                light.intensity = intensity;
                light.color = color;
                return light;
            }
            Lamp(new Vector3(1.2f, 2.4f, 1.8f), 9f, new Color(1f, 0.93f, 0.82f));
            Lamp(new Vector3(-1.6f, 1.4f, 1.2f), 3.5f, new Color(0.6f, 0.72f, 1f));
            Lamp(new Vector3(0f, 2.2f, -1.6f), 5f, new Color(1f, 0.8f, 0.55f));

            _stageTexture = new RenderTexture(720, 1080, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Title stage" };
            _stageCamera = new GameObject("Stage camera").AddComponent<Camera>();
            _stageCamera.transform.SetParent(_stage, false);
            _stageCamera.transform.localPosition = new Vector3(0f, 1.05f, 4.2f);
            _stageCamera.transform.LookAt(_stage.position + new Vector3(0f, 0.92f, 0f));
            _stageCamera.fieldOfView = 30f;
            _stageCamera.nearClipPlane = 0.1f;
            _stageCamera.farClipPlane = 20f;
            _stageCamera.clearFlags = CameraClearFlags.SolidColor;
            _stageCamera.backgroundColor = new Color(0f, 0f, 0f, 0f);
            _stageCamera.targetTexture = _stageTexture;
            _turntable.localRotation = Quaternion.Euler(0f, -20f, 0f); // people face +z, towards the camera; a slight three-quarter view
        }

        private void OnDestroy()
        {
            if (_stageTexture != null) _stageTexture.Release();
        }

        // ------------------------------------------------------------------ UI

        private void Build(VisualElement root)
        {
            _overlay = new VisualElement { name = "title-screen" };
            _overlay.style.position = Position.Absolute;
            _overlay.style.left = _overlay.style.top = _overlay.style.right = _overlay.style.bottom = 0;
            _overlay.style.flexDirection = FlexDirection.Row;
            _overlay.style.backgroundColor = new Color(0.1f, 0.11f, 0.15f);
            root.Add(_overlay);

            // Left: the character on its stage.
            var picture = new VisualElement { pickingMode = PickingMode.Ignore };
            picture.style.flexGrow = 1;
            picture.style.backgroundImage = Background.FromRenderTexture(GetStageTexture());
            picture.style.unityBackgroundScaleMode = ScaleMode.ScaleToFit;
            _overlay.Add(picture);

            // Right: the menus.
            var side = new VisualElement();
            side.style.width = 460;
            side.style.backgroundColor = Panel;
            side.style.paddingLeft = side.style.paddingRight = 40;
            side.style.paddingTop = side.style.paddingBottom = 48;
            side.style.justifyContent = Justify.Center;
            _overlay.Add(side);

            _menu = new VisualElement { name = "title-menu" };
            side.Add(_menu);
            _menu.Add(Text("OPENING BELL", 46, Accent, bold: true));
            _menu.Add(Text("a day-trading life", 18, Dim));
            _menu.Add(Gap(36));
            if (_game.Continued) _menu.Add(Button("title-continue", "CONTINUE", Continue, primary: true));
            _menu.Add(Button("title-new", "NEW GAME", ShowCreator, primary: !_game.Continued));
            _menu.Add(Button("title-quit", "QUIT", Application.Quit));
            if (_game.Continued)
            {
                _menu.Add(Gap(10));
                _menu.Add(Text("A new game replaces your save.", 13, Dim));
            }

            _creator = new VisualElement { name = "title-creator" };
            _creator.style.display = DisplayStyle.None;
            side.Add(_creator);
            _creator.Add(Text("CREATE YOUR TRADER", 28, Accent, bold: true));
            _creator.Add(Gap(18));
            _creator.Add(Text("NAME", 13, Dim));
            _name = new TextField { name = "title-name", value = _look.Name, maxLength = 20 };
            _name.style.fontSize = 18;
            _name.style.marginBottom = 14;
            _creator.Add(_name);
            _bodyLabel = Stepper("BODY", "title-body", 0);
            _outfitLabel = Stepper("OUTFIT", "title-outfit", 1);
            Swatches("SKIN", 0, CharacterStyle.Skins);
            Swatches("HAIR", 1, CharacterStyle.Hairs);
            Swatches("TOP", 2, CharacterStyle.Tops);
            _creator.Add(Gap(22));
            _creator.Add(Button("title-start", "START", StartGame, primary: true));
            _creator.Add(Button("title-back", "BACK", ShowMenu));
        }

        private RenderTexture GetStageTexture()
        {
            if (_stage == null) BuildStage();
            return _stageTexture;
        }

        private void ShowMenu()
        {
            _menu.style.display = DisplayStyle.Flex;
            _creator.style.display = DisplayStyle.None;
            ShowPreview();
        }

        private Label Stepper(string title, string name, int option)
        {
            _creator.Add(Text(title, 13, Dim));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.alignItems = Align.Center;
            row.style.marginBottom = 12;
            row.Add(Button(name + "-prev", "<", () => Step(option, -1), small: true));
            var value = Text("", 20, Ink, bold: true);
            value.style.flexGrow = 1;
            value.style.unityTextAlign = TextAnchor.MiddleCenter;
            row.Add(value);
            row.Add(Button(name + "-next", ">", () => Step(option, 1), small: true));
            _creator.Add(row);
            return value;
        }

        private void Swatches(string title, int group, Color[] colours)
        {
            _creator.Add(Text(title, 13, Dim));
            var row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.flexWrap = Wrap.Wrap;
            row.style.marginBottom = 12;
            for (int i = 0; i < colours.Length; i++)
            {
                int index = i;
                var swatch = new VisualElement { name = $"title-swatch-{group}-{i}" };
                swatch.style.width = swatch.style.height = 30;
                swatch.style.marginRight = swatch.style.marginBottom = 6;
                swatch.style.borderTopWidth = swatch.style.borderBottomWidth = swatch.style.borderLeftWidth = swatch.style.borderRightWidth = 3;
                SetRadius(swatch, 6);
                // Swatch 0 keeps the outfit's own colour.
                swatch.style.backgroundColor = i == 0 ? new Color(0.3f, 0.31f, 0.34f) : colours[i];
                if (i == 0)
                {
                    var own = Text("—", 14, Ink);
                    own.style.unityTextAlign = TextAnchor.MiddleCenter;
                    own.style.flexGrow = 1;
                    swatch.Add(own);
                }
                swatch.RegisterCallback<ClickEvent>(_ => Pick(group, index));
                _swatches.Add((swatch, i, group));
                row.Add(swatch);
            }
            _creator.Add(row);
        }

        private static Label Text(string text, int size, Color color, bool bold = false)
        {
            var label = new Label(text) { pickingMode = PickingMode.Ignore };
            label.style.fontSize = size;
            label.style.color = color;
            if (bold) label.style.unityFontStyleAndWeight = FontStyle.Bold;
            label.style.marginBottom = 2;
            return label;
        }

        private static VisualElement Gap(float height)
        {
            var gap = new VisualElement { pickingMode = PickingMode.Ignore };
            gap.style.height = height;
            return gap;
        }

        private static Button Button(string name, string text, System.Action onClick, bool primary = false, bool small = false)
        {
            var b = new Button(onClick) { name = name, text = text };
            b.style.fontSize = small ? 18 : 20;
            b.style.unityFontStyleAndWeight = FontStyle.Bold;
            b.style.height = small ? 36 : 50;
            if (small) b.style.width = 44;
            b.style.marginLeft = b.style.marginRight = 0;
            b.style.marginBottom = small ? 0 : 10;
            b.style.color = primary ? new Color(0.1f, 0.08f, 0.02f) : Ink;
            b.style.backgroundColor = primary ? Accent : new Color(1f, 1f, 1f, 0.08f);
            b.style.borderTopWidth = b.style.borderBottomWidth = b.style.borderLeftWidth = b.style.borderRightWidth = 0;
            SetRadius(b, 6);
            return b;
        }

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
        }
    }
}
