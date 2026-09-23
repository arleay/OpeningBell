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
        private readonly List<Vector2> _people = new List<Vector2>();
        private readonly List<Vector2> _others = new List<Vector2>();
        private readonly List<TrafficSimulation.Car> _remove = new List<TrafficSimulation.Car>();
        private List<SignalHead> _signals;
        private List<WalkSignal> _walkSignals;
        private Material _red, _yellow, _green, _off, _walk, _wait;
        private readonly Plane[] _frustum = new Plane[6];
        private float _populationTimer;

        /// <summary>Fills the lists with people cars must not drive into, and cars outside the simulation to follow.</summary>
        public delegate void PeopleSource(List<Vector2> people, List<Vector2> cars);

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

            // Fill the streets for the current hour and let them settle before the first frame.
            int target = TargetCount(_c.Game.Clock.Now.TimeOfDay.TotalHours);
            Vector2 player = Flat(_c.Player.position);
            for (int i = 0; i < target * 3 && _sim.Cars.Count < target; i++) _sim.Spawn(p => (p - player).sqrMagnitude > 30f * 30f);
            for (int i = 0; i < 200; i++) _sim.Step(0.1f, null);
            Sync();
        }

        /// <summary>Cars on the whole network by hour: rush hours busy, nights quiet. Sized for the town's ~3 km of road.</summary>
        public static int TargetCount(double hour)
        {
            if (hour < 5) return 5;
            if (hour < 6.5) return 11;
            if (hour < 9.5) return 28;
            if (hour < 16) return 17;
            if (hour < 19) return 28;
            if (hour < 22) return 14;
            return 7;
        }

        private static Vector2 Flat(Vector3 p) => new Vector2(p.x, p.z);

        private void Update()
        {
            float dt = _c.Game.IsPaused ? 0f : Mathf.Min(Time.deltaTime, 0.1f);
            _people.Clear();
            _others.Clear();
            _peopleSource?.Invoke(_people, _others);
            if (dt > 0f) _sim.Step(dt, _people, _others);

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

        /// <summary>One traffic car's look: a car-kit model with wheels that roll.</summary>
        private sealed class TrafficCar
        {
            public Transform Root;
            public Rigidbody Body;
            public Transform[] Wheels;
            public float Radius;
            public Light Beam;
            public string Model;
            public double Spin;
            public Vector3 Last;
        }

        private readonly Dictionary<TrafficSimulation.Car, TrafficCar> _cars = new Dictionary<TrafficSimulation.Car, TrafficCar>();
        private readonly Dictionary<string, Stack<TrafficCar>> _byModel = new Dictionary<string, Stack<TrafficCar>>();

        private void Sync()
        {
            bool night = _c.Night > 0.5f;
            foreach (TrafficSimulation.Car car in _sim.Cars)
            {
                if (!_cars.TryGetValue(car, out TrafficCar t))
                {
                    t = Take(ModelFor(car));
                    _cars[car] = t;
                    t.Root.gameObject.SetActive(true);
                    t.Root.SetPositionAndRotation(Pose(car, out Quaternion r), r);
                    t.Last = t.Root.position;
                }
                Vector3 p = Pose(car, out Quaternion rotation);
                // Kinematic bodies move through the physics step so bumps with the player's car stay sane.
                t.Body.MovePosition(p);
                t.Body.MoveRotation(rotation);
                t.Spin += Vector3.Distance(p, t.Last) / t.Radius;
                t.Last = p;
                var roll = Quaternion.Euler((float)(t.Spin * Mathf.Rad2Deg), 0f, 0f);
                foreach (Transform w in t.Wheels) w.localRotation = roll;
                if (t.Beam != null && t.Beam.enabled != night) t.Beam.enabled = night;
            }
            _remove.Clear();
            foreach (var pair in _cars)
                if (!Contains(pair.Key)) _remove.Add(pair.Key);
            foreach (TrafficSimulation.Car car in _remove)
            {
                TrafficCar t = _cars[car];
                t.Root.gameObject.SetActive(false);
                _byModel[t.Model].Push(t);
                _cars.Remove(car);
            }
        }

        private static Vector3 Pose(TrafficSimulation.Car car, out Quaternion rotation)
        {
            rotation = Quaternion.LookRotation(new Vector3(car.Heading.x, 0f, car.Heading.y));
            return new Vector3(car.Position.x, car.Y, car.Position.y);
        }

        private string ModelFor(TrafficSimulation.Car car)
        {
            IReadOnlyList<string> mix = _c.Game.VehicleLibrary != null ? _c.Game.VehicleLibrary.TrafficMix : null;
            return mix == null || mix.Count == 0 ? "" : mix[(car.ColorSeed & 0x7fffffff) % mix.Count];
        }

        private bool Contains(TrafficSimulation.Car car)
        {
            foreach (TrafficSimulation.Car c in _sim.Cars)
                if (c == car) return true;
            return false;
        }

        private TrafficCar Take(string model)
        {
            if (!_byModel.TryGetValue(model, out Stack<TrafficCar> pool)) _byModel[model] = pool = new Stack<TrafficCar>();
            if (pool.Count > 0) return pool.Pop();
            GameObject mesh = _c.Game.VehicleLibrary != null ? _c.Game.VehicleLibrary.CarMesh(model) : null;
            TrafficCar t = mesh != null ? FromModel(mesh, model) : BoxCar();
            t.Model = model;
            return t;
        }

        private TrafficCar FromModel(GameObject mesh, string model)
        {
            GameObject root = CarFactory.Model(transform, mesh, "Traffic " + model, out Transform[] wheels, out float radius, out Bounds body);
            SetLayer(root, CityLayers.Vehicle);
            var box = root.AddComponent<BoxCollider>();
            box.center = body.center;
            box.size = body.size;
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.interpolation = RigidbodyInterpolation.Interpolate;
            return new TrafficCar { Root = root.transform, Body = rb, Wheels = wheels, Radius = radius, Beam = CarFactory.AddHeadlights(root.transform, body) };
        }

        private static void SetLayer(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform child in go.transform) SetLayer(child.gameObject, layer);
        }

        /// <summary>Fallback without the car kit: the old primitive car.</summary>
        private TrafficCar BoxCar()
        {
            Kit k = _c.Kit;
            Transform car = Kit.Group(transform, "Car", new Vector3(0f, -100f, 0f));
            k.Box(car, "Lower", new Vector3(0f, 0.6f, 0f), new Vector3(1.8f, 0.62f, 4.4f), _c.P.Lit(Paints[_rng++ % Paints.Length], 0.45f), collider: false);
            k.Box(car, "Cabin", new Vector3(0f, 1.18f, -0.25f), new Vector3(1.6f, 0.58f, 2.3f), _c.P.Lit(new Color(0.1f, 0.12f, 0.15f), 0.9f), collider: false);
            var box = car.gameObject.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.85f, 0f);
            box.size = new Vector3(1.8f, 1.5f, 4.4f);
            var rb = car.gameObject.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            return new TrafficCar { Root = car, Body = rb, Wheels = new Transform[0], Radius = 0.34f };
        }

        private int _rng;

        private static readonly Color[] Paints =
        {
            new Color(0.75f, 0.75f, 0.77f), new Color(0.1f, 0.1f, 0.11f), new Color(0.92f, 0.92f, 0.9f), new Color(0.55f, 0.08f, 0.07f),
            new Color(0.12f, 0.22f, 0.42f), new Color(0.35f, 0.37f, 0.4f), new Color(0.2f, 0.32f, 0.25f), new Color(0.6f, 0.5f, 0.35f),
        };

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
