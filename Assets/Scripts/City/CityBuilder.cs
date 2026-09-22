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

        [Header("Material templates (their shader variants ship in builds)")]
        [SerializeField] private Material litTemplate;
        [SerializeField] private Material litEmissiveTemplate;
        [SerializeField] private Material glassTemplate;
        [SerializeField] private Material unlitTemplate;
        [SerializeField] private Material signTemplate;

        private CityContext _c;

        public IReadOnlyDictionary<string, Vector3> Anchors => _c.Anchors;
        public TrafficView Traffic { get; private set; }
        public PedestrianView Pedestrians { get; private set; }
        public Desk OfficeDesk { get; private set; }
        public RoadNetwork Roads => _c.Roads;

        private void Awake()
        {
            if (!game.enabled) return; // bootstrap failed to start; nothing to hang the city on

            var palette = new Palette(litTemplate, litEmissiveTemplate, glassTemplate, unlitTemplate, signTemplate);
            _c = new CityContext
            {
                Kit = new Kit(palette),
                Game = game,
                Hud = hud,
                Player = player.transform,
                Workstation = workstation,
                Roads = RoadNetwork.FromPlan(),
            };
            _c.Static = Kit.Group(transform, "Static");
            _c.Dynamic = Kit.Group(transform, "Dynamic");

            ShellBuilder.Build(_c);
            ApartmentBuilding.Build(_c);
            CalderBuilding.Build(_c, out Desk desk);
            OfficeDesk = desk;
            Shops.Build(_c);

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
            Traffic.Configure(_c, traffic, signals, walkSignals, viewCamera, into =>
            {
                Vector3 p = player.transform.position;
                into.Add(new Vector2(p.x, p.z));
                foreach (PedestrianSimulation.Walker w in peds.Walkers) into.Add(w.Position);
            });
        }

        private void Update()
        {
            if (_c != null && daylight != null) _c.P.ApplyNight(daylight.NightFactor);
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
                SidewalkGraph final = SidewalkGraph.Build(_c.Roads, _c.Places);
                foreach (WalkSignal w in walkSignals)
                    w.Crosswalk = final.Crosswalks.Find(cw => Vector2.Distance(cw.Center, w.Crosswalk.Center) < 0.1f);
                return final;
            }
        }
    }
}
