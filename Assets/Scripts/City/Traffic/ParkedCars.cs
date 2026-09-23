using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    public enum ParkingKind
    {
        /// <summary>Kerbside on a shopping or downtown street: full by day, emptier at night.</summary>
        Commercial,
        /// <summary>Kerbside elsewhere: fuller at night, when people are home.</summary>
        Kerb,
        /// <summary>A lot bay: busy while the place is open.</summary>
        Lot,
    }

    /// <summary>
    /// Cars that aren't going anywhere (TOWN_SPEC A10): along the kerbs of streets and industrial roads, in lot
    /// bays. Which spots are taken changes through the day (fuller downtown by day, fuller outside houses at night)
    /// and from day to day, but a car never appears or vanishes in view: spots only change while unseen, and only
    /// near the player (cars are pooled, so the town's thousands of spots cost a few hundred objects).
    /// </summary>
    public sealed class ParkedCars : MonoBehaviour
    {
        private sealed class Spot
        {
            public Vector3 P;
            public float Yaw;
            public ParkingKind Kind;
            public int Index;
            public GameObject Car;
            public string Model;
        }

        private const float ActiveRadius = 260f, ChangeHidden = 45f;
        private readonly List<Spot> _spots = new List<Spot>();
        private readonly Dictionary<string, Stack<GameObject>> _pool = new Dictionary<string, Stack<GameObject>>();
        private readonly Plane[] _planes = new Plane[6];
        private CityContext _c;
        private Camera _camera;
        private List<string> _models;
        private float _timer;

        public int SpotCount => _spots.Count;
        public int ShownCount { get; private set; }

        public static ParkedCars Build(CityContext c, Camera camera)
        {
            var parked = new GameObject("Parked cars").AddComponent<ParkedCars>();
            parked.transform.SetParent(c.Dynamic, false);
            parked._c = c;
            parked._camera = camera;
            parked._models = new List<string>();
            if (c.Game.VehicleLibrary != null)
                foreach (string m in c.Game.VehicleLibrary.TrafficMix)
                    if (m != "police" && !m.StartsWith("truck-flat") && !m.StartsWith("delivery")) parked._models.Add(m);
            parked.FindKerbs();
            foreach (var (p, yaw, kind) in c.ParkingSpots) parked.Add(p, yaw, kind);
            parked.Refresh(force: true);
            return parked;
        }

        private void Add(Vector3 p, float yaw, ParkingKind kind) =>
            _spots.Add(new Spot { P = p, Yaw = yaw, Kind = kind, Index = _spots.Count });

        private static readonly string[] Commercial = { "MAPLE ST", "EXCHANGE ST", "HARBOR AVE", "QUAY ST", "NEON ROW", "BAYVIEW AVE", "FIRST ST", "CANAL ST", "GROVE ST" };

        /// <summary>
        /// Maple's south kerb outside the apartment is a loading zone: bought cars are delivered to
        /// <see cref="CityBuilder.CurbSpots"/> and the player needs room to pull away west.
        /// </summary>
        private static readonly Rect HomeKerb = new Rect(-35f, -12.5f, 85f, 5f);

        /// <summary>Kerb spots every 6.5 m on streets and industrial roads, clear of junctions, driveways, ramps and bridges.</summary>
        private void FindKerbs()
        {
            Physics.SyncTransforms();
            StreetMap map = _c.Roads.Map;
            var mouths = new List<Vector2>();
            foreach (StreetDef d in map.Driveways) { mouths.Add(d.Points[0]); mouths.Add(d.Points[d.Points.Length - 1]); }
            var hits = new Collider[8];
            foreach (StreetMap.Segment s in map.Segments)
            {
                if (s.Class != RoadClass.Street && s.Class != RoadClass.Industrial) continue;
                ParkingKind kind = System.Array.IndexOf(Commercial, s.Street) >= 0 ? ParkingKind.Commercial : ParkingKind.Kerb;
                float lateral = s.HalfWidth - 1.05f;
                float from = s.AtA.StopLine + 5f, to = s.Length - s.AtB.StopLine - 5f;
                foreach (float side in new[] { -1f, 1f })
                    for (float t = from; t < to; t += 6.5f)
                    {
                        if (s.IsBridge && t > s.BridgeFrom - 4f && t < s.BridgeTo + 4f) continue;
                        Vector2 p2 = s.At(t) + s.Left * side * lateral;
                        if (mouths.Exists(m => Vector2.Distance(m, s.At(t)) < 9f)) continue;
                        if (HomeKerb.Contains(p2)) continue;
                        float y = s.GradeAt(t) + CityPlan.RoadY;
                        var centre = new Vector3(p2.x, y + 0.9f, p2.y);
                        // Right-hand traffic: cars on a side face the way its lane runs.
                        Vector2 facing = side > 0f ? -s.Dir : s.Dir;
                        var rot = Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y));
                        int n = Physics.OverlapBoxNonAlloc(centre, new Vector3(0.95f, 0.6f, 2.4f), hits, rot, ~0, QueryTriggerInteraction.Ignore);
                        bool blocked = false;
                        for (int i = 0; i < n; i++)
                            if (!(hits[i] is TerrainCollider) && !(hits[i] is MeshCollider)) blocked = true; // ramps, poles, hydrants, other cars
                        if (blocked) continue;
                        Add(new Vector3(p2.x, y, p2.y), rot.eulerAngles.y, kind);
                    }
            }
        }

        /// <summary>How likely a spot is taken at this hour.</summary>
        private static float Chance(ParkingKind kind, double hour) => kind switch
        {
            ParkingKind.Commercial => hour >= 8 && hour < 20 ? 0.6f : hour >= 20 && hour < 23 ? 0.4f : 0.18f,
            ParkingKind.Kerb => hour >= 8 && hour < 17 ? 0.28f : 0.45f,
            _ => hour >= 7 && hour < 22 ? 0.65f : 0.12f,
        };

        private static uint Hash(uint x)
        {
            x ^= x >> 16; x *= 0x7feb352d; x ^= x >> 15; x *= 0x846ca68b; x ^= x >> 16;
            return x;
        }

        private bool Occupied(Spot s, System.DateTime now)
        {
            // New draw every four hours and every day; a spot keeps its car between draws.
            uint key = Hash((uint)s.Index * 2654435761u ^ (uint)(now.DayOfYear * 97 + now.Hour / 4));
            return (key % 1000) / 1000f < Chance(s.Kind, now.TimeOfDay.TotalHours);
        }

        private void Update()
        {
            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = 1.5f;
            Refresh(force: false);
        }

        private void Refresh(bool force)
        {
            if (_models.Count == 0) return;
            Camera cam = _camera != null ? _camera : Camera.main;
            if (cam != null) GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            Vector3 player = _c.Player.position;
            System.DateTime now = _c.Game.Clock.Now;
            int shown = 0;
            foreach (Spot s in _spots)
            {
                float d2 = (s.P - player).sqrMagnitude;
                bool near = d2 < ActiveRadius * ActiveRadius;
                bool want = near && Occupied(s, now);
                if (want == (s.Car != null))
                {
                    if (s.Car != null) shown++;
                    continue;
                }
                // Only change what nobody's looking at (the first fill and anything far off are fine).
                bool visible = cam != null && d2 < 150f * 150f && GeometryUtility.TestPlanesAABB(_planes, new Bounds(s.P + Vector3.up, new Vector3(2f, 2f, 5f)));
                if (!force && near && (visible || d2 < ChangeHidden * ChangeHidden)) { if (s.Car != null) shown++; continue; }
                if (want) { Show(s, now); shown++; }
                else Hide(s);
            }
            ShownCount = shown;
        }

        private void Show(Spot s, System.DateTime now)
        {
            s.Model = _models[(int)(Hash((uint)s.Index * 31u + (uint)now.DayOfYear) % (uint)_models.Count)];
            if (!_pool.TryGetValue(s.Model, out Stack<GameObject> pool)) _pool[s.Model] = pool = new Stack<GameObject>();
            GameObject car = pool.Count > 0 ? pool.Pop() : Make(s.Model);
            if (car == null) return;
            car.transform.SetPositionAndRotation(s.P, Quaternion.Euler(0f, s.Yaw, 0f));
            car.SetActive(true);
            s.Car = car;
        }

        private void Hide(Spot s)
        {
            if (s.Car == null) return;
            s.Car.SetActive(false);
            _pool[s.Model].Push(s.Car);
            s.Car = null;
        }

        private GameObject Make(string model)
        {
            GameObject mesh = _c.Game.VehicleLibrary.CarMesh(model);
            if (mesh == null) return null;
            GameObject car = Instantiate(mesh, transform, false);
            car.name = "Parked " + model;
            car.transform.localScale = Vector3.one * CarFactory.Scale;
            var box = car.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = new Vector3(1.45f, 1.1f, 3.3f);
            return car;
        }
    }
}
