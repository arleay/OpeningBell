using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>A named spot for the map: where it is, its icon, and whether it's known before you've been there.</summary>
    public sealed class MapPlace
    {
        public MapIcon Icon;
        public string Name;
        public Vector3 At;
        /// <summary>Home and the office are on the map from the start.</summary>
        public bool Always;
    }

    /// <summary>
    /// Everything that goes on the map, gathered from the plan once the town is built: the old landmarks, every
    /// storefront (icon by trade), the mechanic, dealers, fuel, the casino and the parkade.
    /// </summary>
    public static class MapPlaces
    {
        public static MapIcon? IconFor(Trade t) => t switch
        {
            Trade.Vacant or Trade.MotelOffice => null,
            Trade.Bank => MapIcon.Atm,
            Trade.Police => MapIcon.Police,
            Trade.Supermarket => MapIcon.Mart,
            Trade.Diner or Trade.Bakery or Trade.Restaurant or Trade.FastFood or Trade.Pizza => MapIcon.Food,
            Trade.Bar or Trade.Nightclub => MapIcon.Bar,
            _ => MapIcon.Shop,
        };

        public static List<MapPlace> Gather(CityContext c)
        {
            var all = new List<MapPlace>();
            void Add(MapIcon icon, string name, Vector3 at, bool always = false) =>
                all.Add(new MapPlace { Icon = icon, Name = name, At = at, Always = always });
            foreach (var (icon, anchor, name) in Minimap.Landmarks)
                if (c.Anchors.TryGetValue(anchor, out Vector3 at)) Add(icon, name, at, icon == MapIcon.Home || icon == MapIcon.Office);
            foreach (Business b in BusinessPlan.All())
                if (IconFor(b.Trade) is MapIcon icon) Add(icon, b.Name, new Vector3(b.Front.x, 0f, b.Front.y));
            Add(MapIcon.Mechanic, MechanicShop.Name, new Vector3(MechanicShop.Front.x, 0f, MechanicShop.Front.y));
            if (c.Anchors.TryGetValue("dealer_new_lot", out Vector3 fresh)) Add(MapIcon.Dealer, "Westgate Motors", fresh);
            if (c.Anchors.TryGetValue("dealer_used_lot", out Vector3 used)) Add(MapIcon.Dealer, "Railside Auto Sales", used);
            Add(MapIcon.Fuel, "Northstar", new Vector3(-440f, 0f, 18f));
            Add(MapIcon.Casino, "Silver Tide Casino", new Vector3(Landmarks.CasinoLot.center.x, 0f, Landmarks.CasinoLot.yMin));
            Add(MapIcon.Parking, "Lantern Parkade", new Vector3(Rooftops.Parkade.center.x, 0f, Rooftops.Parkade.yMin));
            if (c.Anchors.TryGetValue("timberline_door", out Vector3 furniture)) Add(MapIcon.Shop, HomeStores.FurnitureName, furniture);
            if (c.Anchors.TryGetValue("circuit_door", out Vector3 tech)) Add(MapIcon.Shop, HomeStores.TechName, tech);
            return all;
        }
    }

    /// <summary>
    /// What the player has found: a place goes on the map once you've been within <see cref="Radius"/> of it, a
    /// district once you've set foot in it. Kept in <see cref="GameBootstrap.Discovered"/>, so it's saved.
    /// </summary>
    public sealed class MapDiscovery : MonoBehaviour
    {
        public const float Radius = 35f;
        private const string DistrictPrefix = "district:";

        private GameBootstrap _game;
        private Transform _player;
        private InteractionHud _hud;
        private float _next;
        private readonly List<string> _found = new List<string>();

        public IReadOnlyList<MapPlace> Places { get; private set; } = new List<MapPlace>();
        /// <summary>Goes up with every discovery, so views rebuild only when something changed.</summary>
        public int Version { get; private set; }

        public void Configure(GameBootstrap game, Transform player, InteractionHud hud, List<MapPlace> places)
        {
            _game = game;
            _player = player;
            _hud = hud;
            Places = places;
        }

        public bool Knows(MapPlace p) => p.Always || (_game != null && _game.Discovered.Contains(p.Name));
        public bool KnowsDistrict(string name) => _game != null && _game.Discovered.Contains(DistrictPrefix + name);

        public int KnownCount
        {
            get
            {
                int n = 0;
                foreach (MapPlace p in Places) if (Knows(p)) n++;
                return n;
            }
        }

        private void Update()
        {
            if (_game == null || _player == null || Time.time < _next) return;
            _next = Time.time + 0.5f;
            Vector3 me = _player.position;
            _found.Clear();
            foreach (MapPlace p in Places)
            {
                if (Knows(p)) continue;
                float dx = p.At.x - me.x, dz = p.At.z - me.z;
                if (dx * dx + dz * dz > Radius * Radius) continue;
                _game.Discovered.Add(p.Name);
                _found.Add(p.Name);
            }
            string district = null;
            foreach (var (name, area) in CityPlan.Districts)
                if (area.Contains(new Vector2(me.x, me.z)) && _game.Discovered.Add(DistrictPrefix + name)) district = name;
            if (_found.Count == 0 && district == null) return;
            Version++;
            if (_hud == null) return;
            // One line however much turned up at once (walking out of the front door finds half of Maple St).
            string places = _found.Count == 0 ? null
                : _found.Count <= 2 ? string.Join(" and ", _found)
                : $"{_found[0]}, {_found[1]} and {_found.Count - 2} more";
            _hud.ShowToast(district != null
                ? district.ToUpperInvariant() + (places != null ? "  ·  on your map: " + places : "")
                : "On your map: " + places, 4f);
        }
    }

    /// <summary>
    /// Fast travel to a place you've found: a taxi from the phone's map. The fare and the ride's time follow the
    /// distance (streets run about 30% longer than the straight line); the clock moves on by the ride.
    /// </summary>
    public static class Taxi
    {
        /// <summary>Closer than this, walk.</summary>
        public const float Shortest = 80f;

        private static float Road(float metres) => metres * 1.3f;

        /// <summary>$3.50 flag fall plus $2.20 a kilometre.</summary>
        public static decimal Fare(float metres) => System.Math.Round(3.5m + 2.2m * (decimal)(Road(metres) / 1000f), 2);

        /// <summary>A few minutes' wait for the cab, then about 28 km/h through town.</summary>
        public static int Minutes(float metres) => 3 + Mathf.CeilToInt(Road(metres) / 1000f / 28f * 60f);

        public static float Distance(Vector3 from, MapPlace to) => Vector2.Distance(new Vector2(from.x, from.z), new Vector2(to.At.x, to.At.z));

        /// <summary>Pays, moves the clock on and sets the player down at the place. An error message, or null.</summary>
        public static string Ride(GameBootstrap game, FirstPersonController player, DriveController driver, MapPlace to)
        {
            if (driver != null && driver.IsDriving) return "You're driving.";
            float d = Distance(player.transform.position, to);
            if (d < Shortest) return "It's just down the street.";
            string error = game.Economy.Spend(Fare(d), "Taxi to " + to.Name, game.Clock.Now);
            if (error != null) return error;
            game.SkipTo(game.Clock.Now.AddMinutes(Minutes(d)));
            SetDown(player, to);
            return null;
        }

        /// <summary>Stands the player on the kerb in front of a place.</summary>
        public static void SetDown(FirstPersonController player, MapPlace to)
        {
            // Down from head height over the kerb: canopies and awnings overhead don't count.
            float grade = StreetMap.Plan.StreetGrade(new Vector2(to.At.x, to.At.z));
            float y = Physics.Raycast(new Vector3(to.At.x, grade + 2.5f, to.At.z), Vector3.down, out RaycastHit hit, 10f, ~0, QueryTriggerInteraction.Ignore)
                ? hit.point.y : grade;
            player.PlaceAt(new Vector3(to.At.x, y + 0.05f, to.At.z), player.transform.eulerAngles.y);
        }
    }

    /// <summary>Instant travel to a place you've found, from the phone's map: $250, no time passes.</summary>
    public static class Teleport
    {
        public const decimal Price = 250m;

        /// <summary>Pays and puts the player in front of the place. An error, or null.</summary>
        public static string Go(GameBootstrap game, FirstPersonController player, DriveController driver, MapPlace to)
        {
            if (driver != null && driver.IsDriving) return "Get out of the car first.";
            string error = game.Economy.Spend(Price, "Teleport to " + to.Name, game.Clock.Now);
            if (error != null) return error;
            Taxi.SetDown(player, to);
            return null;
        }
    }
}
