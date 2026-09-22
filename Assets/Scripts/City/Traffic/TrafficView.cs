using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Drives <see cref="TrafficSimulation"/> from the frame loop and shows it: pooled primitive cars, traffic
    /// signal lamps and walk signals. Car count follows the time of day (rush hours busy, nights quiet); cars
    /// appear and disappear only out of the player's sight.
    /// </summary>
    public sealed class TrafficView : MonoBehaviour
    {
        private TrafficSimulation _sim;
        private CityContext _c;
        private Camera _camera;
        private PeopleSource _peopleSource;
        private readonly Dictionary<TrafficSimulation.Car, Transform> _shown = new Dictionary<TrafficSimulation.Car, Transform>();
        private readonly Stack<Transform> _pool = new Stack<Transform>();
        private readonly List<Vector2> _people = new List<Vector2>();
        private readonly List<TrafficSimulation.Car> _remove = new List<TrafficSimulation.Car>();
        private List<SignalHead> _signals;
        private List<WalkSignal> _walkSignals;
        private Material _red, _yellow, _green, _off, _walk, _wait, _headlight, _taillight;
        private readonly Plane[] _frustum = new Plane[6];
        private float _populationTimer;

        /// <summary>Fills the list with positions cars must not drive into (pedestrians, the player).</summary>
        public delegate void PeopleSource(List<Vector2> into);

        public TrafficSimulation Simulation => _sim;
        public int CarCount => _sim.Cars.Count;

        public void Configure(CityContext c, TrafficSimulation sim, List<SignalHead> signals, List<WalkSignal> walkSignals, Camera camera, PeopleSource people)
        {
            _c = c;
            _sim = sim;
            _signals = signals;
            _walkSignals = walkSignals;
            _camera = camera;
            _peopleSource = people;
            _off = c.P.Unlit(new Color(0.12f, 0.12f, 0.12f));
            _red = c.P.Glow(new Color(1f, 0.15f, 0.1f));
            _yellow = c.P.Glow(new Color(1f, 0.75f, 0.1f));
            _green = c.P.Glow(new Color(0.2f, 1f, 0.45f));
            _walk = c.P.Glow(new Color(0.95f, 0.95f, 0.9f), 1.6f);
            _wait = c.P.Glow(new Color(1f, 0.5f, 0.1f), 1.6f);
            _headlight = c.P.Lamp(new Color(0.75f, 0.75f, 0.7f), new Color(1f, 0.97f, 0.85f));
            _taillight = c.P.Lamp(new Color(0.45f, 0.08f, 0.06f), new Color(1f, 0.12f, 0.08f));

            // Fill the streets for the current hour and let them settle before the first frame.
            int target = TargetCount(_c.Game.Clock.Now.TimeOfDay.TotalHours);
            Vector2 player = Flat(_c.Player.position);
            for (int i = 0; i < target * 3 && _sim.Cars.Count < target; i++) _sim.Spawn(p => (p - player).sqrMagnitude > 30f * 30f);
            for (int i = 0; i < 200; i++) _sim.Step(0.1f, null);
            Sync();
        }

        /// <summary>Cars on the whole network by hour: rush hours busy, nights quiet.</summary>
        public static int TargetCount(double hour)
        {
            if (hour < 5) return 3;
            if (hour < 6.5) return 7;
            if (hour < 9.5) return 18;
            if (hour < 16) return 11;
            if (hour < 19) return 18;
            if (hour < 22) return 9;
            return 5;
        }

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        private void Update()
        {
            float dt = _c.Game.IsPaused ? 0f : Mathf.Min(Time.deltaTime, 0.1f);
            _people.Clear();
            _peopleSource?.Invoke(_people);
            if (dt > 0f) _sim.Step(dt, _people);

            _populationTimer -= Time.deltaTime;
            if (_populationTimer <= 0f)
            {
                _populationTimer = 0.5f;
                Populate();
            }
            Sync();
            UpdateSignals();
        }

        private void Populate()
        {
            int target = TargetCount(_c.Game.Clock.Now.TimeOfDay.TotalHours);
            GeometryUtility.CalculateFrustumPlanes(_camera, _frustum);
            Vector2 player = Flat(_c.Player.position);
            // Catch up quickly after a time skip (a few cars per tick), gently otherwise.
            int change = Mathf.Clamp((target - _sim.Cars.Count + (target > _sim.Cars.Count ? 2 : -2)) / 3, -4, 4);
            for (int i = 0; i < change; i++) _sim.Spawn(p => Unseen(p, player));
            for (int i = 0; i < -change; i++)
            {
                TrafficSimulation.Car gone = null;
                foreach (TrafficSimulation.Car car in _sim.Cars)
                    if (Unseen(car.Position, player))
                    {
                        gone = car;
                        break;
                    }
                if (gone == null) break;
                _sim.Despawn(gone);
            }
        }

        private bool Unseen(Vector2 p, Vector2 player)
        {
            if ((p - player).sqrMagnitude < 40f * 40f) return false;
            var bounds = new Bounds(new Vector3(p.x, 1f, p.y), new Vector3(5f, 2f, 5f));
            return !GeometryUtility.TestPlanesAABB(_frustum, bounds);
        }

        private void Sync()
        {
            foreach (TrafficSimulation.Car car in _sim.Cars)
            {
                if (!_shown.TryGetValue(car, out Transform t))
                {
                    t = _pool.Count > 0 ? _pool.Pop() : BuildCar();
                    Paint(t, car.ColorSeed);
                    t.gameObject.SetActive(true);
                    _shown[car] = t;
                }
                t.SetPositionAndRotation(new Vector3(car.Position.x, CityPlan.RoadY, car.Position.y),
                    Quaternion.LookRotation(new Vector3(car.Heading.x, 0f, car.Heading.y)));
                Light beam = t.GetComponentInChildren<Light>(true);
                bool night = _c.Night > 0.5f;
                if (beam.enabled != night) beam.enabled = night;
            }
            _remove.Clear();
            foreach (var pair in _shown)
                if (!Contains(pair.Key)) _remove.Add(pair.Key);
            foreach (TrafficSimulation.Car car in _remove)
            {
                Transform t = _shown[car];
                t.gameObject.SetActive(false);
                _pool.Push(t);
                _shown.Remove(car);
            }
        }

        private bool Contains(TrafficSimulation.Car car)
        {
            foreach (TrafficSimulation.Car c in _sim.Cars)
                if (c == car) return true;
            return false;
        }

        private static readonly Color[] Paints =
        {
            new Color(0.75f, 0.75f, 0.77f), new Color(0.1f, 0.1f, 0.11f), new Color(0.92f, 0.92f, 0.9f), new Color(0.55f, 0.08f, 0.07f),
            new Color(0.12f, 0.22f, 0.42f), new Color(0.35f, 0.37f, 0.4f), new Color(0.2f, 0.32f, 0.25f), new Color(0.6f, 0.5f, 0.35f),
        };

        /// <summary>TODO(art): primitive car until vehicle models exist (Phase 10 adds real vehicles).</summary>
        private Transform BuildCar()
        {
            Kit k = _c.Kit;
            // Created far below the world: the origin is inside the apartment, and a collider appearing there
            // (even for a frame) shoves the player.
            Transform car = Kit.Group(transform, "Car", new Vector3(0f, -100f, 0f));
            Transform body = Kit.Group(car, "Body");
            k.Box(body, "Lower", new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 0.62f, 4.4f), _c.P.Lit(Color.gray), collider: false);
            k.Box(body, "Cabin", new Vector3(0f, 1.18f, -0.25f), new Vector3(1.6f, 0.58f, 2.3f), _c.P.Lit(Color.gray), collider: false);
            Material glass = _c.P.Lit(new Color(0.1f, 0.12f, 0.15f), 0.9f);
            k.Box(car, "Windshield", new Vector3(0f, 1.2f, 0.93f), new Vector3(1.5f, 0.48f, 0.04f), glass, collider: false);
            k.Box(car, "Rear window", new Vector3(0f, 1.2f, -1.42f), new Vector3(1.5f, 0.45f, 0.04f), glass, collider: false);
            k.Box(car, "Side windows", new Vector3(0f, 1.22f, -0.25f), new Vector3(1.62f, 0.4f, 2.1f), glass, collider: false);
            Material tyre = _c.P.Lit(new Color(0.08f, 0.08f, 0.08f));
            foreach (float x in new[] { -0.82f, 0.82f })
            foreach (float z in new[] { -1.35f, 1.35f })
            {
                GameObject w = k.Cylinder(car, "Wheel", new Vector3(x, 0.34f, z), 0.68f, 0.26f, tyre);
                w.transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }
            foreach (float x in new[] { -0.65f, 0.65f })
            {
                k.Box(car, "Headlight", new Vector3(x, 0.72f, 2.21f), new Vector3(0.36f, 0.14f, 0.03f), _headlight, collider: false);
                k.Box(car, "Taillight", new Vector3(x, 0.75f, -2.21f), new Vector3(0.3f, 0.12f, 0.03f), _taillight, collider: false);
            }
            var beam = new GameObject("Headlights");
            beam.transform.SetParent(car, false);
            beam.transform.localPosition = new Vector3(0f, 0.75f, 2.3f);
            beam.transform.localRotation = Quaternion.Euler(12f, 0f, 0f);
            var spot = beam.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 20f;
            spot.spotAngle = 70f;
            spot.intensity = 24f;
            spot.color = new Color(1f, 0.95f, 0.85f);
            spot.shadows = LightShadows.None;
            spot.enabled = false;
            var box = car.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.85f, 0f);
            box.size = new Vector3(1.8f, 1.5f, 4.4f);
            var rb = car.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.None;
            return car;
        }

        private void Paint(Transform car, int seed)
        {
            Material paint = _c.P.Lit(Paints[(seed & 0x7fffffff) % Paints.Length], 0.45f);
            foreach (Renderer r in car.Find("Body").GetComponentsInChildren<Renderer>()) r.sharedMaterial = paint;
        }

        private void UpdateSignals()
        {
            foreach (SignalHead s in _signals)
            {
                Signal state = _sim.SignalFor(s.Lane);
                Set(s.Red, state == Signal.Red ? _red : _off);
                Set(s.Yellow, state == Signal.Yellow ? _yellow : _off);
                Set(s.Green, state == Signal.Green ? _green : _off);
            }
            foreach (WalkSignal w in _walkSignals)
            {
                bool walk = PedestrianView.WalkShown(_sim, w.Crosswalk);
                Set(w.Walk, walk ? _walk : _off);
                Set(w.Wait, walk ? _off : _wait);
            }
        }

        private static void Set(Renderer r, Material m)
        {
            if (r.sharedMaterial != m) r.sharedMaterial = m;
        }
    }
}
