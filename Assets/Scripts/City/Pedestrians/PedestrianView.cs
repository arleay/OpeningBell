using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Shows <see cref="PedestrianSimulation"/> with pooled primitive people and applies the city's crossing rules:
    /// walk signals at lights, a gap in traffic elsewhere. Head count follows the time of day.
    /// </summary>
    public sealed class PedestrianView : MonoBehaviour
    {
        private PedestrianSimulation _sim;
        private TrafficSimulation _traffic;
        private CityContext _c;
        private readonly Dictionary<PedestrianSimulation.Walker, NpcBody> _shown = new Dictionary<PedestrianSimulation.Walker, NpcBody>();
        private readonly Stack<NpcBody> _pool = new Stack<NpcBody>();
        private readonly List<PedestrianSimulation.Walker> _gone = new List<PedestrianSimulation.Walker>();
        private float _spawnTimer;
        private Vector2 _lastPlayer;
        private bool _hasLast;
        private int _bodySeed = 900;

        public PedestrianSimulation Simulation => _sim;
        public int Count => _sim.Walkers.Count;

        public void Configure(CityContext c, PedestrianSimulation sim, TrafficSimulation traffic)
        {
            _c = c;
            _sim = sim;
            _traffic = traffic;
            _sim.MayCross = MayCross;

            // Start with the street already busy, people spread along their routes.
            int target = TargetCount(_c.Game.Clock.Now.TimeOfDay.TotalHours);
            for (int i = 0; i < target; i++) _sim.Spawn();
            for (int i = 0; i < 240; i++) _sim.Step(0.25f, new Vector2(9999f, 9999f));
            RemoveArrived();
            Sync(0f);
        }

        /// <summary>People out and about by hour, across the whole town (house doors to shops and back).</summary>
        public static int TargetCount(double hour)
        {
            if (hour < 5.5) return 3;
            if (hour < 7) return 12;
            if (hour < 9.5) return 32;
            if (hour < 12) return 22;
            if (hour < 13.5) return 30;
            if (hour < 16.5) return 22;
            if (hour < 19) return 32;
            if (hour < 22) return 15;
            return 6;
        }

        /// <summary>What a walk signal shows: WALK while the crossed road is red, with time left to cross.</summary>
        public static bool WalkShown(TrafficSimulation traffic, SidewalkGraph.Crosswalk cw)
        {
            if (!cw.Signalized) return false;
            Signal s = cw.Junction.Lights.SignalFor(cw.WalkPhase, traffic.Time, out float remaining);
            return s == Signal.Green && remaining > 4f;
        }

        private bool MayCross(SidewalkGraph.Crosswalk cw)
        {
            if (cw.Signalized) return WalkShown(_traffic, cw);
            return !_traffic.AnyCarNear(cw.Center, 17f);
        }

        private void Update()
        {
            float dt = _c.Game.IsPaused ? 0f : Mathf.Min(Time.deltaTime, 0.1f);
            if (dt <= 0f) return;
            Vector3 p = _c.Player.position;
            Vector2 flat = new Vector2(p.x, p.z);
            Vector2 velocity = _hasLast ? (flat - _lastPlayer) / dt : Vector2.zero;
            _lastPlayer = flat;
            _hasLast = true;
            _sim.Step(dt, flat, velocity);
            RemoveArrived();

            int target = TargetCount(_c.Game.Clock.Now.TimeOfDay.TotalHours);
            _sim.Thinning = _sim.Walkers.Count > target;
            _spawnTimer -= dt;
            if (_spawnTimer <= 0f && _sim.Walkers.Count < target)
            {
                // Far below target (after a time skip): several step out at once.
                _spawnTimer = 0.8f;
                int missing = target - _sim.Walkers.Count;
                for (int i = 0; i < Mathf.Clamp(missing / 4, 1, 4); i++) _sim.Spawn();
            }
            Sync(Time.time);
        }

        private void RemoveArrived()
        {
            _gone.Clear();
            foreach (PedestrianSimulation.Walker w in _sim.Walkers)
                if (w.State == PedestrianSimulation.WalkerState.Arrived) _gone.Add(w);
            foreach (PedestrianSimulation.Walker w in _gone)
            {
                _sim.Remove(w);
                if (_shown.TryGetValue(w, out NpcBody body))
                {
                    body.gameObject.SetActive(false);
                    _pool.Push(body);
                    _shown.Remove(w);
                }
            }
        }

        private void Sync(float time)
        {
            foreach (PedestrianSimulation.Walker w in _sim.Walkers)
            {
                if (!_shown.TryGetValue(w, out NpcBody body))
                {
                    body = _pool.Count > 0 ? _pool.Pop() : NewBody();
                    body.gameObject.SetActive(true);
                    _shown[w] = body;
                }
                bool sitting = w.State == PedestrianSimulation.WalkerState.Sitting;
                Vector2 facing = sitting ? BenchFacing(w) : w.Heading;
                body.transform.SetPositionAndRotation(new Vector3(w.Position.x, w.Y, w.Position.y), Quaternion.LookRotation(new Vector3(facing.x, 0f, facing.y)));
                NpcPose pose = sitting ? NpcPose.Sit : w.State == PedestrianSimulation.WalkerState.Walking && w.Blocked <= 0f ? NpcPose.Walk : NpcPose.Stand;
                body.Animate(pose, time, w.Speed / 1.35f);
            }
        }

        /// <summary>
        /// Takes a walker's body out of the sidewalk simulation so a fight can drive it; it returns to the pool when
        /// the fight is over (the simulation spawns someone else to keep the head count). Null if it isn't a walker.
        /// </summary>
        public NpcFighter Provoke(NpcBody body, FirstPersonController player, bool brave)
        {
            PedestrianSimulation.Walker walker = null;
            foreach (var pair in _shown)
                if (pair.Value == body) { walker = pair.Key; break; }
            if (walker == null) return null;
            _sim.Remove(walker);
            _shown.Remove(walker);
            return NpcFighter.Engage(body, player, mobile: true, brave, _ =>
            {
                body.gameObject.SetActive(false);
                _pool.Push(body);
            });
        }

        /// <summary>Walkers' bodies within <paramref name="radius"/> of <paramref name="at"/> (witnesses).</summary>
        public void Near(Vector3 at, float radius, List<NpcBody> into)
        {
            foreach (NpcBody body in _shown.Values)
                if ((body.transform.position - at).sqrMagnitude < radius * radius) into.Add(body);
        }

        /// <summary>Sit facing away from the bench back, i.e. away from the ring point it hangs off.</summary>
        private static Vector2 BenchFacing(PedestrianSimulation.Walker w)
        {
            SidewalkGraph.Node bench = w.Destination;
            if (bench.Edges.Count == 0) return w.Heading;
            Vector2 toSidewalk = bench.Edges[0].Other(bench).P - bench.P;
            return toSidewalk.sqrMagnitude > 1e-4f ? toSidewalk.normalized : w.Heading;
        }

        private NpcBody NewBody()
        {
            NpcBody body = NpcBody.Create(_c.Kit, transform, "Pedestrian", _bodySeed++);
            body.transform.position = new Vector3(0f, -100f, 0f); // not at the origin (the apartment) even for a frame
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = CityLayers.Pedestrian;
            var capsule = body.gameObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.9f, 0f);
            capsule.height = 1.8f;
            capsule.radius = 0.25f;
            body.gameObject.AddComponent<Rigidbody>().isKinematic = true;
            return body;
        }
    }
}
