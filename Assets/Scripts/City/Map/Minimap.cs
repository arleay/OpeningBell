using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;
using UnityEngine.UIElements;

namespace OpeningBell.City
{
    /// <summary>
    /// Round minimap in the HUD's top-right corner. The painted town map (<see cref="MapTexture"/>) turns with the
    /// player, who is the arrow in the middle pointing up; icons mark home, the office, shops, fuel and parked cars.
    /// Places beyond the edge stick to the rim, pointing the way. Hidden while seated at a desk or in a menu.
    /// </summary>
    public sealed class Minimap : MonoBehaviour
    {
        private const float Size = 210f, IconSize = 26f;
        /// <summary>Metres from the centre to the rim.</summary>
        private const float Range = 85f;

        private readonly struct Place
        {
            public readonly MapIcon Icon;
            public readonly Vector3 At;
            public readonly VisualElement Element;

            public Place(MapIcon icon, Vector3 at, VisualElement element)
            {
                Icon = icon;
                At = at;
                Element = element;
            }
        }

        private CityContext _c;
        private FirstPersonController _player;
        private InteractionHud _hud;
        private FleetView _fleet;
        private Texture2D _texture;
        private VisualElement _frame, _map, _north, _icons;
        private readonly List<Place> _places = new List<Place>();
        private readonly List<VisualElement> _cars = new List<VisualElement>();
        private readonly List<Vector2> _carPositions = new List<Vector2>();

        public bool Visible => _frame != null && _frame.style.display != DisplayStyle.None;

        /// <summary>The painted town (north up), shared with the phone's Maps app.</summary>
        public Texture2D Texture => _texture;

        /// <summary>Anchors of the marked places, available before the HUD is built.</summary>
        public static readonly (MapIcon Icon, string Anchor, string Name)[] Landmarks =
        {
            (MapIcon.Home, "apartment_front_out", "Home"),
            (MapIcon.Office, "calder_front_out", "Calder Building"),
            (MapIcon.Coffee, "coffee_front_out", "Half Past Nine"),
            (MapIcon.Mart, "mart_front_out", "Corner Mart"),
            (MapIcon.Skate, "skate_front_out", "Curbside Skate"),
            (MapIcon.Bike, "bike_front_out", "Hillside Cycles"),
            (MapIcon.Fuel, "fuel_driveway", "Tidewater Fuel"),
        };

        /// <summary>Where each marked place is (for tests and a future full map).</summary>
        public IEnumerable<(MapIcon Icon, Vector3 At)> Places
        {
            get { foreach (Place p in _places) yield return (p.Icon, p.At); }
        }

        public void Configure(CityContext c, FirstPersonController player, InteractionHud hud, FleetView fleet)
        {
            _c = c;
            _player = player;
            _hud = hud;
            _fleet = fleet;
            _texture = MapTexture.Paint(c);
        }

        private float Scale => Size / 2f / Range; // UI pixels per metre

        private void Build(VisualElement root)
        {
            _frame = new VisualElement { name = "minimap", pickingMode = PickingMode.Ignore };
            _frame.style.position = Position.Absolute;
            _frame.style.top = 16;
            _frame.style.right = 16;
            _frame.style.width = _frame.style.height = Size;
            _frame.style.overflow = Overflow.Hidden; // with the radius, clips the map to a circle
            _frame.style.backgroundColor = new Color(0.3f, 0.42f, 0.26f);
            SetRadius(_frame, Size / 2f);
            _frame.style.borderTopWidth = _frame.style.borderBottomWidth = _frame.style.borderLeftWidth = _frame.style.borderRightWidth = 3;
            _frame.style.borderTopColor = _frame.style.borderBottomColor = _frame.style.borderLeftColor = _frame.style.borderRightColor = new Color(0.95f, 0.95f, 0.92f, 0.9f);
            root.Add(_frame);

            float s = Scale / MapTexture.PixelsPerMetre;
            _map = new VisualElement { pickingMode = PickingMode.Ignore };
            _map.style.position = Position.Absolute;
            _map.style.width = _texture.width * s;
            _map.style.height = _texture.height * s;
            _map.style.backgroundImage = new StyleBackground(_texture);
            _frame.Add(_map);

            _icons = new VisualElement { pickingMode = PickingMode.Ignore };
            _icons.style.position = Position.Absolute;
            _icons.style.left = _icons.style.top = 0;
            _icons.style.width = _icons.style.height = Size;
            _frame.Add(_icons);

            // The player: an arrow in the middle, always pointing up (the map turns instead).
            var arrow = new VisualElement { pickingMode = PickingMode.Ignore };
            arrow.style.position = Position.Absolute;
            arrow.style.left = arrow.style.top = Size / 2f - 3f - 9f;
            arrow.style.width = arrow.style.height = 18;
            arrow.generateVisualContent += ctx =>
            {
                Painter2D p = ctx.painter2D;
                p.fillColor = Color.white;
                p.strokeColor = new Color(0.1f, 0.1f, 0.12f);
                p.lineWidth = 1.5f;
                p.BeginPath();
                p.MoveTo(new Vector2(9f, 1f));
                p.LineTo(new Vector2(16f, 17f));
                p.LineTo(new Vector2(9f, 13f));
                p.LineTo(new Vector2(2f, 17f));
                p.ClosePath();
                p.Fill();
                p.Stroke();
            };
            _frame.Add(arrow);

            _north = new Label("N") { pickingMode = PickingMode.Ignore };
            _north.style.position = Position.Absolute;
            _north.style.width = _north.style.height = 18;
            _north.style.unityTextAlign = TextAnchor.MiddleCenter;
            _north.style.fontSize = 12;
            _north.style.unityFontStyleAndWeight = FontStyle.Bold;
            _north.style.color = Color.white;
            _north.style.backgroundColor = new Color(0.1f, 0.1f, 0.12f, 0.8f);
            SetRadius(_north, 9f);
            _frame.Add(_north);

            void Mark(MapIcon icon, string anchor)
            {
                if (_c.Anchors.TryGetValue(anchor, out Vector3 at)) _places.Add(new Place(icon, at, Icon(icon)));
            }
            foreach (var (icon, anchor, _) in Landmarks) Mark(icon, anchor);
        }

        private VisualElement Icon(MapIcon icon)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore, name = "map-icon-" + icon };
            e.style.position = Position.Absolute;
            e.style.width = e.style.height = IconSize;
            e.style.backgroundImage = new StyleBackground(MapIcons.Get(icon));
            _icons.Add(e);
            return e;
        }

        private void LateUpdate()
        {
            if (_hud == null || _texture == null) return;
            if (_frame == null)
            {
                if (_hud.Root == null) return;
                Build(_hud.Root);
            }
            bool show = _player.ControlEnabled || _player.Browsing;
            _frame.style.display = show ? DisplayStyle.Flex : DisplayStyle.None;
            if (!show) return;

            // Follow whatever the player is looking through (a car's chase camera still faces the car's way).
            Transform view = Camera.main != null ? Camera.main.transform : _player.CameraPivot;
            Vector3 me = _player.transform.position;
            float yaw = view.eulerAngles.y;

            // Turn the map about the player's spot so their heading points up.
            Vector2 px = MapTexture.ToPixel(me) * (Scale / MapTexture.PixelsPerMetre);
            _map.style.left = Size / 2f - 3f - px.x;
            _map.style.top = Size / 2f - 3f - px.y;
            _map.style.transformOrigin = new TransformOrigin(px.x, px.y, 0f);
            _map.style.rotate = new Rotate(-yaw);

            PlaceOnMap(_north, me + new Vector3(0f, 0f, 10000f), me, yaw, 18f);
            foreach (Place p in _places) PlaceOnMap(p.Element, p.At, me, yaw, IconSize);

            // Parked cars you own.
            _carPositions.Clear();
            if (_fleet != null) _fleet.ParkedCarPositions(_carPositions);
            while (_cars.Count < _carPositions.Count) _cars.Add(Icon(MapIcon.Car));
            for (int i = 0; i < _cars.Count; i++)
            {
                bool on = i < _carPositions.Count;
                _cars[i].style.display = on ? DisplayStyle.Flex : DisplayStyle.None;
                if (on) PlaceOnMap(_cars[i], new Vector3(_carPositions[i].x, 0f, _carPositions[i].y), me, yaw, IconSize);
            }
        }

        /// <summary>Positions a marker by its bearing from the player, clamped to the rim when out of range.</summary>
        private void PlaceOnMap(VisualElement e, Vector3 at, Vector3 me, float yaw, float size)
        {
            Vector2 d = new Vector2(at.x - me.x, at.z - me.z);
            float r = yaw * Mathf.Deg2Rad;
            // Into the view's frame: x to the right of the heading, y along it.
            var local = new Vector2(d.x * Mathf.Cos(r) - d.y * Mathf.Sin(r), d.x * Mathf.Sin(r) + d.y * Mathf.Cos(r)) * Scale;
            float rim = Size / 2f - size / 2f - 4f;
            if (local.magnitude > rim) local = local.normalized * rim;
            e.style.left = Size / 2f - 3f + local.x - size / 2f;
            e.style.top = Size / 2f - 3f - local.y - size / 2f;
        }

        private static void SetRadius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = e.style.borderTopRightRadius = e.style.borderBottomLeftRadius = e.style.borderBottomRightRadius = r;
        }

        private void OnDestroy()
        {
            if (_texture != null) Destroy(_texture);
        }
    }
}
