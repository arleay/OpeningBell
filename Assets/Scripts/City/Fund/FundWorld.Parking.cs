using System.Collections.Generic;
using OpeningBell.Fund;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Staff parking and commutes (FUND_SPEC §8, brief §24): the reserved row on Harborview's P1. Someone who drives has
    /// their own car (the one their savings bought, reviewed monthly) in a bay of their own while they're at work. When the
    /// player can see the garage, arrivals drive in off Harbor Ave, down the ramp and into the bay, and the driver walks
    /// to the lift; at the end of the day they walk back, reverse out and drive up the ramp. Unseen, the car is simply
    /// there or not, and nothing ever appears or vanishes in view. Staff without a car, or beyond the reserved bays, come
    /// by transit.
    /// </summary>
    public sealed partial class FundWorld
    {
        private sealed class Bay
        {
            public long Employee;
            public string Model = "";
            public GameObject Car;
            /// <summary>A drive in or out is under way: the bay is spoken for.</summary>
            public Trip Trip;
        }

        private enum Leg { WalkToCar, Drive, WalkToLift }

        /// <summary>A watched commute: the car along a path (some of it in reverse), and the driver on foot.</summary>
        private sealed class Trip
        {
            public Bay Bay;
            public bool Leaving;
            public Leg Leg;
            public readonly List<(Vector3 P, bool Reverse)> Path = new List<(Vector3, bool)>();
            public int Next;
            public NpcBody Walker;
            public readonly List<Vector3> Walk = new List<Vector3>();
            public int WalkNext;
        }

        private const float DriveSpeed = 4.2f, WalkPace = 1.3f;

        private Bay[] _bays;
        private float _nextParking;
        private readonly Plane[] _planes = new Plane[6];
        private readonly List<Vector2> _playerCars = new List<Vector2>();
        private readonly List<Trip> _trips = new List<Trip>();
        private readonly Dictionary<long, Activity> _lastActivity = new Dictionary<long, Activity>();

        private void UpdateParking()
        {
            for (int i = _trips.Count - 1; i >= 0; i--)
                if (Move(_trips[i])) _trips.RemoveAt(i);

            if (Time.time < _nextParking) return;
            _nextParking = Time.time + 1f;
            var spots = HarborviewTower.StaffBays;
            if (spots.Count == 0) return;
            _bays ??= NewBays(spots.Count);

            // Who should be parked: drivers who are in the building (arriving to leaving).
            var here = new Dictionary<long, Employee>();
            if (Fund.Exists)
                foreach (Employee e in Fund.Employees)
                    if (!e.Former && !string.IsNullOrEmpty(e.Person.Car) && e.Activity >= Activity.Arriving && e.Activity != Activity.Former)
                        here[e.Id] = e;

            Camera cam = Camera.main;
            if (cam != null) GeometryUtility.CalculateFrustumPlanes(cam, _planes);
            _playerCars.Clear();
            _c.FleetView?.ParkedCarPositions(_playerCars);

            // Keep people in the bay they have; the ones who've gone home drive off (seen) or are simply gone (unseen).
            for (int i = 0; i < _bays.Length; i++)
            {
                Bay b = _bays[i];
                if (b.Employee == 0 || b.Trip != null) continue;
                if (here.TryGetValue(b.Employee, out Employee stay) && stay.Person.Car == b.Model)
                {
                    here.Remove(b.Employee);
                    continue;
                }
                if (b.Car != null && i < HarborviewTower.ReservedBays && GarageWatched() && !Seen(RampTop(), cam)) Depart(b, spots[i]);
                else if (!Seen(spots[i].P, cam)) Clear(b);
            }
            // New arrivals: a free bay; watched, they drive in; otherwise the car is just there.
            foreach (Employee e in here.Values)
                for (int i = 0; i < _bays.Length; i++)
                {
                    Bay b = _bays[i];
                    if (b.Employee != 0 || Taken(spots[i].P)) continue;
                    bool justArrived = _lastActivity.TryGetValue(e.Id, out Activity was) && was < Activity.Arriving;
                    // The drive in is laid out for the reserved row; overflow cars are just there, out of view.
                    if (justArrived && i < HarborviewTower.ReservedBays && GarageWatched() && !Seen(RampTop(), cam)) Arrive(b, e, spots[i]);
                    else if (!Seen(spots[i].P, cam)) Park(b, e, spots[i]);
                    else break; // in view: wait until the player looks away
                    break;
                }
            if (Fund.Exists) foreach (Employee e in Fund.Employees) _lastActivity[e.Id] = e.Activity;
        }

        private static Bay[] NewBays(int n)
        {
            var bays = new Bay[n];
            for (int i = 0; i < n; i++) bays[i] = new Bay();
            return bays;
        }

        private void Park(Bay b, Employee e, (Vector3 P, float Yaw) spot)
        {
            b.Employee = e.Id;
            b.Model = e.Person.Car;
            b.Car = Car(e.Person.Car, e.Id, spot.P, spot.Yaw);
        }

        private static void Clear(Bay b)
        {
            if (b.Car != null) Destroy(b.Car);
            b.Car = null;
            b.Employee = 0;
            b.Model = "";
            b.Trip = null;
        }

        // ------------------------------------------------------------------ watched commutes

        private static float Lane => HarborviewTower.StaffLaneZ;

        /// <summary>The top of the ramp, where cars turn in off Harbor Ave.</summary>
        private static Vector3 RampTop() => new Vector3(HarborviewTower.RampTopX - 10f, HarborviewTower.Grade, Lane);

        /// <summary>Is the player down in P1 (or on the ramp) where a commute could be watched?</summary>
        private bool GarageWatched()
        {
            if (_c.Player == null) return false;
            Vector3 p = _c.Player.position, g = HarborviewTower.GarageCentre;
            return p.y < HarborviewTower.Grade - 1f && Mathf.Abs(p.x - g.x) < 30f && Mathf.Abs(p.z - g.z) < 20f;
        }

        /// <summary>Off Harbor Ave, down the ramp, east along the lane, a turn into the bay nose-first.</summary>
        private void Arrive(Bay b, Employee e, (Vector3 P, float Yaw) spot)
        {
            float f = HarborviewTower.Grade + HarborviewTower.GarageFloor;
            var t = new Trip { Bay = b, Leaving = false, Leg = Leg.Drive };
            t.Path.Add((RampTop(), false));
            t.Path.Add((new Vector3(HarborviewTower.RampTopX, HarborviewTower.Grade, Lane), false));
            t.Path.Add((new Vector3(HarborviewTower.RampFootX, f, Lane), false));
            t.Path.Add((new Vector3(spot.P.x - 3.5f, f, Lane), false));
            t.Path.Add((new Vector3(spot.P.x - 0.8f, f, Lane - 1.8f), false));
            t.Path.Add((spot.P, false));
            b.Employee = e.Id;
            b.Model = e.Person.Car;
            b.Car = Car(e.Person.Car, e.Id, t.Path[0].P, 90f);
            b.Trip = t;
            // Then out of the driver's door and along the walkway to the lift.
            Vector3 door = spot.P + Quaternion.Euler(0f, spot.Yaw, 0f) * new Vector3(-1.2f, 0f, 0f);
            t.Walk.Add(door);
            t.Walk.Add(new Vector3(door.x, f, Lane - 1.2f));
            t.Walk.Add(HarborviewTower.GarageLiftDoor);
            t.Walker = WalkerFor(e, door, spot.Yaw);
            t.Walker.gameObject.SetActive(false);
            _trips.Add(t);
        }

        /// <summary>From the lift to the car, then back out of the bay and up the ramp to Harbor Ave.</summary>
        private void Depart(Bay b, (Vector3 P, float Yaw) spot)
        {
            Employee e = Fund.Find(b.Employee);
            float f = HarborviewTower.Grade + HarborviewTower.GarageFloor;
            var t = new Trip { Bay = b, Leaving = true, Leg = Leg.WalkToCar };
            Vector3 door = spot.P + Quaternion.Euler(0f, spot.Yaw, 0f) * new Vector3(-1.2f, 0f, 0f);
            t.Walk.Add(HarborviewTower.GarageLiftDoor);
            t.Walk.Add(new Vector3(door.x, f, Lane - 1.2f));
            t.Walk.Add(door);
            t.Path.Add((spot.P, false));
            t.Path.Add((new Vector3(spot.P.x, f, Lane - 0.5f), true));
            t.Path.Add((new Vector3(spot.P.x - 3f, f, Lane), false));
            t.Path.Add((new Vector3(HarborviewTower.RampFootX, f, Lane), false));
            t.Path.Add((new Vector3(HarborviewTower.RampTopX, HarborviewTower.Grade, Lane), false));
            t.Path.Add((RampTop(), false));
            t.Walker = e != null ? WalkerFor(e, HarborviewTower.GarageLiftDoor, 180f) : null;
            if (t.Walker == null) t.Leg = Leg.Drive;
            b.Trip = t;
            _trips.Add(t);
        }

        /// <summary>One frame of a trip. True when it's over.</summary>
        private bool Move(Trip t)
        {
            if (t.Bay.Car == null && t.Leg == Leg.Drive) { EndTrip(t); return true; }
            switch (t.Leg)
            {
                case Leg.WalkToCar:
                case Leg.WalkToLift:
                    if (t.Walker == null || StepWalker(t))
                    {
                        if (t.Walker != null) Destroy(t.Walker.gameObject);
                        t.Walker = null;
                        if (t.Leg == Leg.WalkToLift) { EndTrip(t); return true; }
                        t.Leg = Leg.Drive;
                    }
                    return false;
                default:
                    if (!StepCar(t)) return false;
                    if (t.Leaving) { Clear(t.Bay); EndTrip(t); return true; }
                    t.Leg = Leg.WalkToLift;
                    if (t.Walker != null) t.Walker.gameObject.SetActive(true);
                    return false;
            }
        }

        private static void EndTrip(Trip t)
        {
            if (t.Walker != null) Destroy(t.Walker.gameObject);
            t.Walker = null;
            if (t.Bay.Trip == t) t.Bay.Trip = null;
        }

        /// <summary>The car toward its next point: nose first, or backing out (facing away from where it's going).</summary>
        private static bool StepCar(Trip t)
        {
            Transform car = t.Bay.Car.transform;
            if (t.Next >= t.Path.Count) return true;
            var (to, reverse) = t.Path[t.Next];
            bool last = t.Next == t.Path.Count - 1;
            float speed = reverse || (t.Leaving ? t.Next <= 2 : t.Next >= t.Path.Count - 2) ? 1.6f : DriveSpeed;
            car.position = Vector3.MoveTowards(car.position, to, speed * Time.deltaTime);
            Vector3 dir = to - car.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 0.01f)
            {
                Quaternion face = Quaternion.LookRotation(reverse ? -dir.normalized : dir.normalized);
                car.rotation = Quaternion.RotateTowards(car.rotation, face, 90f * Time.deltaTime);
            }
            if ((car.position - to).sqrMagnitude > 0.0025f) return false;
            t.Next++;
            if (last && !t.Leaving)
            {
                // Squared up in the bay.
                car.rotation = Quaternion.Euler(0f, HarborviewTower.StaffBays.Count > 0 ? HarborviewTower.StaffBays[0].Yaw : 180f, 0f);
            }
            return t.Next >= t.Path.Count;
        }

        private static bool StepWalker(Trip t)
        {
            if (t.WalkNext >= t.Walk.Count) return true;
            Transform w = t.Walker.transform;
            if (!t.Walker.gameObject.activeSelf) t.Walker.gameObject.SetActive(true);
            Vector3 to = t.Walk[t.WalkNext];
            w.position = Vector3.MoveTowards(w.position, to, WalkPace * Time.deltaTime);
            Vector3 dir = to - w.position;
            dir.y = 0f;
            if (dir.sqrMagnitude > 1e-4f) w.rotation = Quaternion.Slerp(w.rotation, Quaternion.LookRotation(dir.normalized), 0.2f);
            t.Walker.Animate(NpcPose.Walk, Time.time);
            if ((w.position - to).sqrMagnitude < 0.0025f) t.WalkNext++;
            return t.WalkNext >= t.Walk.Count;
        }

        private NpcBody WalkerFor(Employee e, Vector3 at, float yaw)
        {
            NpcBody body = FundPerson.Body(this, e, transform);
            body.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            return body;
        }

        // ------------------------------------------------------------------ helpers

        /// <summary>In view and close enough to notice a car appearing.</summary>
        private bool Seen(Vector3 p, Camera cam)
        {
            if (cam == null || _c.Player == null) return false;
            if ((p - _c.Player.position).sqrMagnitude > 70f * 70f) return false;
            return GeometryUtility.TestPlanesAABB(_planes, new Bounds(p + Vector3.up, new Vector3(2.4f, 2f, 5f)));
        }

        /// <summary>One of the player's own cars is in the bay.</summary>
        private bool Taken(Vector3 p)
        {
            foreach (Vector2 o in _playerCars)
                if ((o - new Vector2(p.x, p.z)).sqrMagnitude < 3f * 3f) return true;
            return false;
        }

        /// <summary>Their car as scenery: the catalog model's body in a paint of their own (keyed by who they are).</summary>
        private GameObject Car(string name, long id, Vector3 at, float yaw)
        {
            VehicleModel model = null;
            foreach (VehicleModel m in _c.Game.Vehicles.Catalog.Models) if (m.Name == name) model = m;
            GameObject mesh = model != null && _c.Game.VehicleLibrary != null ? _c.Game.VehicleLibrary.CarMesh(model.Mesh) : null;
            if (mesh == null) return null;
            GameObject car = Instantiate(mesh, transform, false);
            car.name = $"Staff car: {name}";
            CarFactory.Respray(car, model.Mesh, new System.Random((int)(id * 7919 % int.MaxValue)));
            car.transform.localScale = Vector3.one * CarFactory.Scale;
            car.transform.SetPositionAndRotation(at, Quaternion.Euler(0f, yaw, 0f));
            var box = car.AddComponent<BoxCollider>();
            box.center = new Vector3(0f, 0.55f, 0f);
            box.size = new Vector3(1.45f, 1.1f, 3.3f);
            MeshMerge.MergeIntoOne(car.transform);
            return car;
        }
    }
}
