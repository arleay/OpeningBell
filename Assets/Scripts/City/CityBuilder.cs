using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Generates the city around the apartment when the scene loads (Phase 9): streets, buildings, the office
    /// building, shops, traffic and pedestrians, all from <see cref="CityPlan"/>. Generated rather than authored
    /// so the layout lives in reviewable code and every run gets the same city.
    /// </summary>
    public sealed class CityBuilder : MonoBehaviour
    {
        [SerializeField] private GameBootstrap game;
        [SerializeField] private InteractionHud hud;
        [SerializeField] private FirstPersonController player;
        [SerializeField] private WorkstationController workstation;
        [SerializeField] private DaylightCycle daylight;
        [SerializeField] private Camera viewCamera;
        [Tooltip("The scene-authored apartment room, furnished from the art library.")]
        [SerializeField] private Transform apartment;

        [Header("Material templates (their shader variants ship in builds)")]
        [SerializeField] private Material litTemplate;
        [SerializeField] private Material litEmissiveTemplate;
        [SerializeField] private Material glassTemplate;
        [SerializeField] private Material unlitTemplate;
        [SerializeField] private Material signTemplate;

        [Tooltip("Third-party models (buildings, props, people). Without it the city is all primitives.")]
        [SerializeField, Optional] private CityArt art;
        public CityArt Art => art;

        private CityContext _c;

        public IReadOnlyDictionary<string, Vector3> Anchors => _c.Anchors;
        public TrafficView Traffic { get; private set; }
        public PedestrianView Pedestrians { get; private set; }
        public Desk OfficeDesk { get; private set; }
        public FleetView Fleet { get; private set; }
        public RideController Rider { get; private set; }
        public DriveController Driver { get; private set; }
        /// <summary>First Street Motors (new) and Harbor Auto Sales (used).</summary>
        public List<DealerLot> Dealers { get; private set; }
        public MechanicShop Mechanic { get; private set; }
        public Minimap Minimap { get; private set; }
        public Phone Phone { get; private set; }
        public TitleScreen Title { get; private set; }
        public RoadNetwork Roads => _c.Roads;

        private void Awake()
        {
            if (!game.enabled) return; // bootstrap failed to start; nothing to hang the city on

            CityLayers.Apply();
            var palette = new Palette(litTemplate, litEmissiveTemplate, glassTemplate, unlitTemplate, signTemplate);
            _c = new CityContext
            {
                Kit = new Kit(palette, art),
                Game = game,
                Hud = hud,
                Player = player.transform,
                Workstation = workstation,
                Roads = RoadNetwork.FromPlan(),
            };
            _c.Static = Kit.Group(transform, "Static");
            _c.Dynamic = Kit.Group(transform, "Dynamic");

            // Lots first: the terrain (built with the streets) is levelled under them.
            TownBuilder.AddPads(_c);
            Dealerships.AddPads(_c);
            Landmarks.AddPads(_c);
            BusinessPlan.AddPads(_c);
            Industrial.AddPads(_c);
            MechanicShop.AddPads(_c);
            Residential.AddPads(_c);
            Waterfront.AddPads(_c);
            ShellBuilder.Build(_c);
            TownBuilder.Build(_c, player.transform);
            ApartmentBuilding.Build(_c);
            CalderBuilding.Build(_c, out Desk desk);
            OfficeDesk = desk;
            Shops.Build(_c);
            MobilityShops.Build(_c);
            FuelStation.Build(_c);
            Dealers = Dealerships.Build(_c);
            Landmarks.Build(_c);
            BusinessPlan.Build(_c);
            Industrial.Build(_c);
            Mechanic = MechanicShop.Build(_c);
            Residential.BuildApartments(_c);
            Waterfront.Build(_c);

            // Places (doors, benches) are registered by the builders above; the street pass adds benches too,
            // so gather everything first, then build the walk graph the markings and signals need.
            var signals = new List<SignalHead>();
            var walkSignals = new List<WalkSignal>();
            var benchesAndStreets = new StreetPass(_c);
            SidewalkGraph walks = benchesAndStreets.Build(signals, walkSignals);

            StaticBatchingUtility.Combine(_c.Static.gameObject);

            var traffic = new TrafficSimulation(_c.Roads, seed: 3301);
            var peds = new PedestrianSimulation(walks, seed: 3302);
            Pedestrians = new GameObject("Pedestrians").AddComponent<PedestrianView>();
            Pedestrians.transform.SetParent(transform, false);
            Traffic = new GameObject("Traffic").AddComponent<TrafficView>();
            Traffic.transform.SetParent(transform, false);
            Pedestrians.Configure(_c, peds, traffic);
            Traffic.Configure(_c, traffic, signals, walkSignals, viewCamera, (people, cars) =>
            {
                Vector3 p = player.transform.position;
                if (Driver != null && Driver.IsDriving) cars.Add(new Vector2(p.x, p.z));
                else people.Add(new Vector2(p.x, p.z));
                foreach (PedestrianSimulation.Walker w in peds.Walkers) people.Add(w.Position);
                Fleet?.ParkedCarPositions(cars);
            });

            // Owned bikes and boards: parked ones in the world, riding on the player.
            Fleet = new GameObject("Fleet").AddComponent<FleetView>();
            Fleet.transform.SetParent(transform, false);
            PlayerInteractor interactor = player.GetComponentInChildren<PlayerInteractor>();
            if (interactor == null) interactor = FindAnyObjectByType<PlayerInteractor>();
            Rider = player.gameObject.AddComponent<RideController>();
            Rider.Configure(game, player, interactor, hud, _c.Kit, Fleet);
            Driver = player.gameObject.AddComponent<DriveController>();
            Driver.Configure(game, player, interactor, hud, Fleet, () => _c.Night);
            Fleet.Configure(game, _c.Kit, Rider, Driver);
            foreach (DealerLot dealer in Dealers) dealer.Wire(Fleet, Driver);
            _c.FleetView = Fleet;
            _c.Driver = Driver;
            player.gameObject.AddComponent<Footsteps>().Configure(player);
            PlayerBody body = null;
            if (art != null && art.HasPeople)
            {
                body = player.gameObject.AddComponent<PlayerBody>();
                body.Configure(player, art, game.Look);
            }
            player.gameObject.AddComponent<PlayerFists>().Configure(player, body, Pedestrians);
            Minimap = gameObject.AddComponent<Minimap>();
            Minimap.Configure(_c, player, hud, Fleet);
            Phone = gameObject.AddComponent<Phone>();
            Phone.Configure(game, player, hud, Minimap, _c);
            if (Debug.isDebugBuild) gameObject.AddComponent<DevCheats>().Configure(game, Phone);
            if (art != null && art.HasPeople)
            {
                Title = gameObject.AddComponent<TitleScreen>();
                Title.Configure(game, player, hud, art, body);
            }

            // Last, so a renamed scene object costs only the furniture, not the city.
            ApartmentInterior.Dress(_c.Kit, apartment, workstation.transform);
        }

        private bool _lightsOn;
        private OpeningBell.UI.TradingTerminal _terminal;

        /// <summary>Where private sellers leave a car: the kerb on Maple outside 118 (north side, facing west with the traffic).</summary>
        public static readonly Vector3[] CurbSpots =
        {
            new Vector3(14f, CityPlan.RoadY, -10.15f), new Vector3(20.5f, CityPlan.RoadY, -10.15f), new Vector3(27f, CityPlan.RoadY, -10.15f),
            new Vector3(33.5f, CityPlan.RoadY, -10.15f), new Vector3(40f, CityPlan.RoadY, -10.15f),
        };

        /// <summary>Classifieds purchase: pay by card, the car appears at the first free kerb spot outside home.</summary>
        public string DeliverUsedCar(OpeningBell.Vehicles.UsedListing listing)
        {
            OpeningBell.Vehicles.Fleet fleet = game.Vehicles;
            if (fleet.IsSold(listing.Id)) return "That one's already sold.";
            Vector3 spot = CurbSpots[0];
            foreach (Vector3 s in CurbSpots)
            {
                bool taken = false;
                foreach (OpeningBell.Vehicles.OwnedVehicle v in fleet.Vehicles)
                    if (v.State == OpeningBell.Vehicles.VehicleState.Parked && new Vector2((float)v.X - s.x, (float)v.Z - s.z).magnitude < 3.5f) taken = true;
                if (!taken) { spot = s; break; }
            }
            string error = game.Economy.Spend((decimal)listing.Price, "Used car", game.Clock.Now);
            if (error != null) return error;
            fleet.AddUsed(listing, game.Clock.Now, spot.x, spot.y, spot.z, 270);
            return null;
        }

        private void Update()
        {
            if (_c == null || daylight == null) return;
            if (_terminal == null) _terminal = FindAnyObjectByType<OpeningBell.UI.TradingTerminal>();
            if (_terminal != null && _terminal.Context != null && _terminal.Context.BuyUsedCar == null) _terminal.Context.BuyUsedCar = DeliverUsedCar;
            _c.Night = daylight.NightFactor;
            _c.P.ApplyNight(_c.Night);
            // Street lamps switch as a group, with a little hysteresis around dusk and dawn.
            bool on = _lightsOn ? _c.Night > 0.3f : _c.Night > 0.6f;
            if (on == _lightsOn) return;
            _lightsOn = on;
            foreach (Light light in _c.NightLights) light.enabled = on;
        }

        private void OnDestroy() => _c?.P.Dispose();

        /// <summary>Street furniture registers benches, which the walk graph needs, which the markings need.</summary>
        private sealed class StreetPass
        {
            private readonly CityContext _c;

            public StreetPass(CityContext c) => _c = c;

            public SidewalkGraph Build(List<SignalHead> signals, List<WalkSignal> walkSignals)
            {
                // Build a provisional graph for crosswalk positions (they only depend on the roads).
                SidewalkGraph provisional = SidewalkGraph.Build(_c.Roads, System.Array.Empty<(Vector2, PlaceKind, string)>());
                StreetBuilder.Build(_c, provisional, signals, walkSignals);
                // Now with every door and bench known. Crosswalk objects must be the final graph's, for the signals.
                SidewalkGraph final = SidewalkGraph.Build(_c.Roads, _c.Places, _c.WalkPaths);
                foreach (WalkSignal w in walkSignals)
                    w.Crosswalk = final.Crosswalks.Find(cw => Vector2.Distance(cw.Center, w.Crosswalk.Center) < 0.1f);
                return final;
            }
        }
    }
}
