using System;
using System.Collections.Generic;
using System.Linq;
using OpeningBell.Home;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Online orders in the world (see <see cref="HomeShop"/>). A pickup is set out in the bays behind the stores when
    /// it's ready. A delivery goes out on a store truck: it pulls up at the kerb nearest your home's entrance, two movers
    /// carry it to your door and set it down there, or with the move-in service carry it inside: furniture packed in a
    /// moving box, tech on a cart; at Harborview up the lift, set down in front of it on the penthouse floor. Seen from
    /// nearby, the truck drives in and away; unseen, it's simply there. The simulation's clock decides when; if time
    /// jumps (sleep), the goods are simply there.
    /// </summary>
    public sealed class ShopDeliveries : MonoBehaviour
    {
        /// <summary>After this long past the arrival time, a delivery still under way just finishes.</summary>
        private const int GiveUpMinutes = 45;

        private CityContext _c;
        private HomeWorld _w;
        private float _next;
        private readonly Dictionary<int, DeliveryRun> _runs = new Dictionary<int, DeliveryRun>();

        internal CityContext City => _c;
        internal HomeWorld World => _w;

        public static ShopDeliveries Build(CityContext c, HomeWorld w)
        {
            var go = new GameObject("Shop deliveries");
            go.transform.SetParent(c.Dynamic, false);
            var d = go.AddComponent<ShopDeliveries>();
            d._c = c;
            d._w = w;
            c.Game.ShopDestinations = d.Destinations;
            c.Game.OrderTracking = d.Tracking;
            c.Game.TrackOrder = d.Track;
            Instance = d;
            return d;
        }

        /// <summary>Your homes, by name and kind (not the office, not the stores).</summary>
        private IReadOnlyList<(string Id, string Name)> Destinations()
        {
            var list = new List<(string, string)>();
            foreach (HomeSpec h in _w.Homes)
            {
                if (!_w.Owns(h)) continue; // the office counts once the fund holds Level 26
                string kind = h.Kind switch
                {
                    HomeKind.Apartment => "apartment",
                    HomeKind.Penthouse => "high-rise",
                    HomeKind.Office => "office, Harborview 26",
                    HomeKind.Mansion => "mansion",
                    _ => "house",
                };
                list.Add((h.Id, $"{h.Name} ({kind})"));
            }
            return list;
        }

        private void Update()
        {
            if (Time.time < _next) return;
            _next = Time.time + 0.5f;
            DateTime now = _c.Game.Clock.Now;
            HomeShop shop = _c.Game.Shop;
            foreach (ShopOrder o in shop.Orders)
            {
                var due = new DateTime(o.Due);
                if (o.How == Fulfilment.Pickup)
                {
                    if (o.State == ShopOrderStatus.Preparing && now >= due) SetOut(o);
                    continue;
                }
                switch (o.State)
                {
                    case ShopOrderStatus.Preparing:
                        // Packed and loaded, then the drive over: out the door for the last part of the wait.
                        if (now >= due.AddMinutes(-DriveMinutes(o)))
                        {
                            shop.SetStatus(o, ShopOrderStatus.OnTheWay);
                            Notify(o, $"Order #{o.Id} is out for delivery to {o.DestinationName}, arriving around {due:h:mm tt}. Tap to track it.");
                        }
                        break;
                    case ShopOrderStatus.OnTheWay:
                    case ShopOrderStatus.Unloading:
                        // The truck leaves the stores when the order goes out and drives the streets to your kerb.
                        if (!_runs.ContainsKey(o.Id)) Begin(o);
                        else if (now > due.AddMinutes(GiveUpMinutes)) _runs[o.Id].Finish();
                        if (o.State == ShopOrderStatus.OnTheWay && now >= due.AddMinutes(-5) && _notified.Add(o.Id * 10 + 1))
                            Notify(o, $"Your delivery is about {Math.Max(1, (int)Math.Round((due - now).TotalMinutes))} minutes away.");
                        break;
                }
            }
        }

        internal static int DriveMinutes(ShopOrder o) => (int)Math.Min(20, (o.Due - o.Placed) / TimeSpan.TicksPerMinute / 2);

        /// <summary>A pickup order is ready: out in the bays behind the stores.</summary>
        private void SetOut(ShopOrder o)
        {
            var goods = _w.Belongings.Items.Where(i => i.Order == o.Id && i.State == ItemState.AtPickup).ToList();
            HomeStores.SetOut(_w, goods);
            foreach (OwnedItem i in goods) i.Order = 0;
            _c.Game.Shop.SetStatus(o, ShopOrderStatus.Ready);
            _w.Say($"Order #{o.Id} is ready: it's out in the pickup bays behind the stores, off Grove St.");
            Notify(o, $"Order #{o.Id} is ready for pickup in the bays behind the stores, off Grove St.");
        }

        private void Begin(ShopOrder o)
        {
            var go = new GameObject("Delivery #" + o.Id);
            go.transform.SetParent(transform, false);
            var run = go.AddComponent<DeliveryRun>();
            _runs[o.Id] = run;
            run.Configure(this, o);
        }

        internal void Done(ShopOrder o)
        {
            _runs.Remove(o.Id);
            _c.Game.Shop.SetStatus(o, ShopOrderStatus.Delivered);
            string text = o.MoveIn ? $"Order #{o.Id} is in: the movers left it {(IsPenthouse(o) ? "by the lift on your floor" : "inside the front door")}."
                : $"Order #{o.Id} was delivered to your door at {o.DestinationName}.";
            _w.Say(text);
            Notify(o, text);
        }

        // ------------------------------------------------------------------ tracking and notifications

        private readonly HashSet<int> _notified = new HashSet<int>();
        private Phone _phone;

        /// <summary>The live deliveries: running now, for the map and <see cref="HomeShop"/>'s tracking.</summary>
        public static ShopDeliveries Instance { get; private set; }

        /// <summary>Trucks on the road or at a kerb, by order, for the phone's map.</summary>
        public IEnumerable<(int Order, Vector3 At)> Trucks()
        {
            foreach (var pair in _runs)
                if (pair.Value != null && pair.Value.TruckAt(out Vector3 at)) yield return (pair.Key, at);
        }

        /// <summary>A line on where an order's truck is: distance and roughly when it arrives.</summary>
        private string Tracking(int id)
        {
            ShopOrder o = _c.Game.Shop.Find(id);
            if (o == null || !_runs.TryGetValue(id, out DeliveryRun run) || run == null || !run.TruckAt(out Vector3 at)) return null;
            if (o.State != ShopOrderStatus.OnTheWay) return null;
            float km = Vector2.Distance(new Vector2(at.x, at.z), new Vector2(_entranceOf(o).x, _entranceOf(o).z)) / 1000f;
            int minutes = Math.Max(0, (int)Math.Round((new DateTime(o.Due) - _c.Game.Clock.Now).TotalMinutes));
            return $"Truck {km:0.0} km away · about {minutes} min";
        }

        private Vector3 _entranceOf(ShopOrder o) => Entrance(_w.Find(o.Destination) ?? _w.MainHome);

        /// <summary>A banner on the phone from the store's app; tapping it opens the map on the truck.</summary>
        private void Notify(ShopOrder o, string text)
        {
            if (_phone == null) _phone = FindAnyObjectByType<Phone>();
            if (_phone == null) return;
            int id = o.Id;
            _phone.Notify(o.StoreKind == HomeStore.Tech ? PhoneAppId.CircuitStop : PhoneAppId.Timberline,
                o.StoreKind == HomeStore.Tech ? "Circuit Stop" : "Timberline Home", text, () => Track(id));
        }

        /// <summary>Opens the phone's map on an order's truck.</summary>
        private void Track(int id)
        {
            if (_phone == null) _phone = FindAnyObjectByType<Phone>();
            if (_phone == null) return;
            _phone.TrackOnMap(() => _runs.TryGetValue(id, out DeliveryRun r) && r != null && r.TruckAt(out Vector3 at) ? at : (Vector3?)null);
        }

        /// <summary>Up Harborview's lift: the penthouse or the office on 26.</summary>
        internal bool IsPenthouse(ShopOrder o) => InTower(_w.Find(o.Destination));

        private static bool InTower(HomeSpec h) => h != null && (h.Kind == HomeKind.Penthouse || h.Kind == HomeKind.Office);

        /// <summary>The lift stop a Harborview delivery rides to.</summary>
        internal int LiftStop(ShopOrder o) => _w.Find(o.Destination)?.Kind == HomeKind.Office ? HarborviewTower.StopOffice : HarborviewTower.StopPenthouse;

        /// <summary>In front of the lift on the delivery's floor (world), and a step back into the car.</summary>
        private static Vector3 LiftFront(HomeSpec h) => h.Kind == HomeKind.Office
            ? HarborviewOffice.World(HarborviewOffice.LiftLanding.x, HarborviewOffice.LiftLanding.y - 0.6f) : HarborviewTower.PenthouseLiftFront;

        private static Vector3 InCar(HomeSpec h) => h.Kind == HomeKind.Office
            ? HarborviewOffice.World(HarborviewOffice.LiftLanding.x, HarborviewOffice.LiftLanding.y + 1.4f) : HarborviewTower.PenthouseLiftFront + new Vector3(0f, 0f, 2f);

        // ------------------------------------------------------------------ where things go

        /// <summary>Just outside the home's entrance (world): the kerbside drop and where the movers go in.</summary>
        internal Vector3 Entrance(HomeSpec home)
        {
            if (InTower(home)) return HarborviewTower.LobbyDoorOutside;
            if (home.Kind == HomeKind.Apartment && _c.Anchors.TryGetValue("apartment_front_out", out Vector3 a)) return a;
            Vector3 p = home.Root.TransformPoint(home.DoorLocal + new Vector3(0f, 0f, -1.4f));
            p.y = HomeWorld.GroundAt(p);
            return p;
        }

        /// <summary>The movers' way in from the entrance, to where they go out of sight (the lift, the unit's door).</summary>
        internal List<Vector3> WayIn(HomeSpec home)
        {
            var path = new List<Vector3>();
            if (InTower(home))
            {
                Vector3 inside = HarborviewTower.LobbyDoorOutside + new Vector3(0f, 0f, 3.5f);
                path.Add(inside);
                path.Add(HarborviewTower.LobbyLiftFront);
            }
            else if (home.Kind == HomeKind.Apartment && _c.Anchors.TryGetValue("apartment_front_in", out Vector3 hall)
                     && _c.Anchors.TryGetValue("apartment_door_hall", out Vector3 unit))
            {
                path.Add(hall);
                path.Add(unit);
            }
            else path.Add(home.Root.TransformPoint(home.DoorLocal));
            return path;
        }

        /// <summary>Where the crew comes back into view inside: out of the lift on the penthouse floor, or the front door.</summary>
        internal Vector3 InnerEntry(HomeSpec home) => InTower(home)
            ? InCar(home)
            : home.Root.TransformPoint(home.DoorLocal + new Vector3(0f, 0f, 0.5f));

        /// <summary>A move-in's n-th spot: in front of the penthouse lift, or in rows inside the front door.</summary>
        internal Vector3 MoveInSpot(HomeSpec home, int n)
        {
            if (InTower(home))
            {
                // Rows stepping away from the lift, across its front.
                Vector3 front = LiftFront(home), away = front - InCar(home);
                away.y = 0f;
                away.Normalize();
                return front + Vector3.Cross(Vector3.up, away) * ((n % 3 - 1) * 1.1f) + away * (n / 3 * 1.1f);
            }
            Vector3 p = home.Root.TransformPoint(home.DoorLocal + new Vector3((n % 3 - 1) * 1.1f, 0f, 1.6f + n / 3 * 1.2f));
            p.y = HomeWorld.GroundAt(p);
            return p;
        }

        /// <summary>
        /// The kerb nearest the entrance: the closest road lane, pulled in toward the entrance's side. Returns where the
        /// truck stops and the way it's heading. False without roads.
        /// </summary>
        internal bool Kerb(Vector3 entrance, out Vector3 stop, out Vector3 heading) => Kerb(entrance, out stop, out heading, out _, out _);

        internal bool Kerb(Vector3 entrance, out Vector3 stop, out Vector3 heading, out RoadNetwork.Lane lane, out float at)
        {
            stop = entrance;
            heading = Vector3.forward;
            lane = null;
            at = 0f;
            RoadNetwork roads = _c.Roads;
            if (roads == null) return false;
            var p = new Vector2(entrance.x, entrance.z);
            float best = float.MaxValue;
            foreach (RoadNetwork.Lane l in roads.Lanes)
            {
                if (l.IsStem || l.Length < 12f) continue;
                float s = Mathf.Clamp(Vector2.Dot(p - l.Start, l.Dir), 6f, l.Length - 6f);
                float d = (l.At(s) - p).sqrMagnitude;
                if (d < best)
                {
                    best = d;
                    lane = l;
                    at = s;
                }
            }
            if (lane == null) return false;
            Vector2 on = lane.At(at);
            Vector2 toDoor = p - on;
            float gap = toDoor.magnitude;
            Vector2 pulled = on + (gap > 0.01f ? toDoor / gap * Mathf.Min(1.2f, Mathf.Max(0f, gap - 3f)) : Vector2.zero);
            stop = new Vector3(pulled.x, lane.HeightAt(at), pulled.y);
            heading = new Vector3(lane.Dir.x, 0f, lane.Dir.y);
            return true;
        }

        /// <summary>Where the store trucks pull out: the stores' driveway onto Grove St.</summary>
        private static readonly Vector2 Depot = new Vector2(-288f, 70f);

        /// <summary>
        /// The truck's way from the stores to the kerb: along road lanes and through junction turns (a breadth-first
        /// search over the lane graph, so the fewest lanes), ending at <paramref name="stop"/>. Null if there's no way.
        /// </summary>
        internal List<Vector3> Route(RoadNetwork.Lane goal, float goalAt, Vector3 stop)
        {
            RoadNetwork roads = _c.Roads;
            if (roads == null || goal == null) return null;
            RoadNetwork.Lane start = null;
            float startAt = 0f, best = float.MaxValue;
            foreach (RoadNetwork.Lane l in roads.Lanes)
            {
                if (l.IsStem || l.Length < 8f) continue;
                float s = Mathf.Clamp(Vector2.Dot(Depot - l.Start, l.Dir), 0f, l.Length);
                float d = (l.At(s) - Depot).sqrMagnitude;
                if (d < best) { best = d; start = l; startAt = s; }
            }
            if (start == null) return null;
            var via = new Dictionary<RoadNetwork.Lane, RoadNetwork.Movement> { [start] = null };
            var queue = new Queue<RoadNetwork.Lane>();
            queue.Enqueue(start);
            bool found = start == goal && startAt <= goalAt;
            while (!found && queue.Count > 0)
            {
                RoadNetwork.Lane l = queue.Dequeue();
                foreach (RoadNetwork.Movement m in l.Exits)
                {
                    if (m.Out == null || via.ContainsKey(m.Out)) continue;
                    via[m.Out] = m;
                    if (m.Out == goal) { found = true; break; }
                    queue.Enqueue(m.Out);
                }
            }
            if (!found) return null;
            var turns = new List<RoadNetwork.Movement>();
            for (RoadNetwork.Lane l = goal; l != start; l = via[l].In) turns.Add(via[l]);
            turns.Reverse();
            var path = new List<Vector3>();
            void Add(Vector2 p, float y) => path.Add(new Vector3(p.x, y, p.y));
            Add(start.At(startAt), start.HeightAt(startAt));
            RoadNetwork.Lane on = start;
            foreach (RoadNetwork.Movement m in turns)
            {
                Add(on.End, on.EndY);
                for (int k = 1; k < 6; k++) Add(m.At(m.Length * k / 6f), m.HeightAt(m.Length * k / 6f));
                on = m.Out;
                Add(on.Start, on.StartY);
            }
            Add(goal.At(goalAt), goal.HeightAt(goalAt));
            path.Add(stop);
            return path;

        /// <summary>
        /// The goods arrive: at the door (kerbside), or moved in (furniture packed in an open moving box, tech on the
        /// building's cart at Harborview if it's free, otherwise set down in rows). They're ordinary belongings after.
        /// </summary>
        internal void PlaceGoods(ShopOrder o, Vector3 entrance, Vector3 street, RollCart cart)
        {
            HomeSpec home = _w.Find(o.Destination) ?? _w.MainHome;
            List<OwnedItem> goods = _w.Belongings.Items.Where(i => i.Order == o.Id && i.State == ItemState.Delivering).ToList();
            if (goods.Count == 0) return;
            float yaw = home.Root.eulerAngles.y;
            if (!o.MoveIn)
            {
                // Kerbside: in rows on the path just outside the entrance, on the street side.
                Vector3 outward = street - entrance;
                outward.y = 0f;
                outward = outward.sqrMagnitude > 0.01f ? outward.normalized : Vector3.back;
                Vector3 side = Vector3.Cross(Vector3.up, outward);
                for (int n = 0; n < goods.Count; n++)
                {
                    Vector3 p = entrance + outward * (1.2f + n / 3 * 1.1f) + side * ((n % 3 - 1) * 1.1f);
                    p.y = HomeWorld.GroundAt(p);
                    Put(goods[n], p, Quaternion.LookRotation(outward).eulerAngles.y);
                }
            }
            else if (o.StoreKind == HomeStore.Furniture)
            {
                Vector3 p = MoveInSpot(home, 0);
                OwnedItem box = _w.Belongings.Add("moving_box", 0, ItemState.Placed);
                Put(box, p, yaw);
                foreach (OwnedItem i in goods)
                {
                    i.Order = 0;
                    _w.Belongings.Pack(i, box);
                }
            }
            else if (cart != null)
            {
                for (int n = 0; n < goods.Count; n++)
                {
                    OwnedItem i = goods[n];
                    Vector3 slot = RollCart.Slot(n);
                    i.State = ItemState.Loaded;
                    i.Vehicle = RollCart.BedId;
                    i.Property = "";
                    i.X = slot.x; i.Y = slot.y; i.Z = slot.z; i.Yaw = 0f;
                    i.Order = 0;
                }
            }
            else
                for (int n = 0; n < goods.Count; n++) Put(goods[n], MoveInSpot(home, n), yaw);
            _w.Belongings.Touch();
        }

        private void Put(OwnedItem i, Vector3 p, float yaw)
        {
            i.State = ItemState.Placed;
            i.Property = _w.HomeAt(p)?.Id ?? "curb";
            i.X = p.x; i.Y = p.y; i.Z = p.z;
            i.Yaw = yaw;
            i.Order = 0;
        }

        /// <summary>The store truck (Timberline's box truck, in the store's colours) as a prop: parked, no physics.</summary>
        internal GameObject Truck(ShopOrder o, Transform parent)
        {
            bool tech = o.StoreKind == HomeStore.Tech;
            Color brand = tech ? OpeningBell.UI.ShopView.TechBrand : OpeningBell.UI.ShopView.FurnitureBrand;
            Kit k = _c.Kit;
            GameObject prefab = null;
            if (_c.Game.Vehicles.Catalog.TryGetModel(Loaner.Model, out VehicleModel model) && _c.Game.VehicleLibrary != null)
                prefab = _c.Game.VehicleLibrary.CarMesh(model.Mesh);
            GameObject root;
            float rear, width = 2f;
            if (prefab != null)
            {
                root = CarFactory.Model(parent, prefab, "Delivery truck", out _, out _, out Bounds body);
                rear = body.min.z;
                width = Mathf.Min(2.2f, body.size.x);
            }
            else
            {
                // No art: a cab and a box on primitives.
                root = new GameObject("Delivery truck");
                root.transform.SetParent(parent, false);
                k.Box(root.transform, "Cab", new Vector3(0f, 1.2f, 2.2f), new Vector3(2f, 2f, 1.6f), _c.P.Lit(Color.white, 0.4f), collider: false);
                rear = -2.4f;
            }
            // The cargo box on the flatbed, white with the store's name and a band in its colour.
            float floor = 0.74f, top = 2.75f, length = 3.4f, mid = rear + length / 2f, hw = width / 2f;
            Material white = _c.P.Lit(new Color(0.93f, 0.93f, 0.9f), 0.25f);
            k.Box(root.transform, "Cargo box", new Vector3(0f, (floor + top) / 2f, mid), new Vector3(width, top - floor, length), white, collider: false);
            foreach (float s in new[] { -1f, 1f })
            {
                k.Box(root.transform, "Band", new Vector3(s * (hw + 0.002f), floor + 0.35f, mid), new Vector3(0.004f, 0.3f, length - 0.05f), _c.P.Lit(brand, 0.3f), collider: false);
                k.Text(root.transform, tech ? "CIRCUIT STOP" : "TIMBERLINE HOME", new Vector3(s * (hw + 0.01f), floor + (top - floor) * 0.62f, mid), s > 0f ? -90f : 90f, 0.22f, brand);
                k.Text(root.transform, "DELIVERY", new Vector3(s * (hw + 0.01f), floor + (top - floor) * 0.36f, mid), s > 0f ? -90f : 90f, 0.1f, new Color(0.25f, 0.25f, 0.27f));
            }
            var solid = root.AddComponent<BoxCollider>();
            solid.center = new Vector3(0f, 1.4f, rear + 3f);
            solid.size = new Vector3(width, 2.6f, 6f);
            root.transform.position = new Vector3(0f, -100f, 0f);
            return root;
        }

        /// <summary>A mover in the store's colours.</summary>
        internal NpcBody Mover(ShopOrder o, Transform parent, int n)
        {
            Color brand = o.StoreKind == HomeStore.Tech ? OpeningBell.UI.ShopView.TechBrand : OpeningBell.UI.ShopView.FurnitureBrand;
            NpcBody body = NpcBody.Create(_c.Kit, parent, "Mover", 7100 + o.Id * 7 + n, brand, "Worker");
            body.transform.position = new Vector3(0f, -100f, 0f);
            return body;
        }

        /// <summary>What a mover carries: a moving box, or a stack of boxed tech.</summary>
        internal Transform Load(ShopOrder o, NpcBody body)
        {
            Transform load = Kit.Group(body.transform, "Load", new Vector3(0f, 0.95f, 0.38f));
            Material card = _c.P.Lit(new Color(0.66f, 0.52f, 0.36f), 0.05f);
            if (o.StoreKind == HomeStore.Furniture)
                _c.Kit.Box(load, "Box", Vector3.zero, new Vector3(0.6f, 0.45f, 0.45f), card, collider: false);
            else
            {
                _c.Kit.Box(load, "Box", new Vector3(0f, -0.08f, 0f), new Vector3(0.55f, 0.16f, 0.4f), card, collider: false);
                _c.Kit.Box(load, "Box", new Vector3(0f, 0.08f, 0f), new Vector3(0.45f, 0.16f, 0.35f), _c.P.Lit(new Color(0.9f, 0.9f, 0.88f), 0.1f), collider: false);
            }
            return load;
        }

        /// <summary>Is the player close enough to see this (on the same level)?</summary>
        internal bool Seen(Vector3 p, float range)
        {
            if (_c.Player == null) return false;
            Vector3 d = _c.Player.position - p;
            return Mathf.Abs(d.y) < 6f && new Vector2(d.x, d.z).sqrMagnitude < range * range;
        }
    }

    /// <summary>
    /// One delivery, played out: the truck pulls up, two movers carry the order to the entrance (and, for a move-in,
    /// inside and on up), set it down, walk back, and the truck drives off.
    /// </summary>
    internal sealed class DeliveryRun : MonoBehaviour
    {
        private enum Phase { EnRoute, DriveIn, Parked, CarryIn, Inside, InnerWalk, Return, Leave, Done }

        private sealed class Walker
        {
            public NpcBody Body;
            public Transform Load;
            public readonly List<Vector3> Path = new List<Vector3>();
            public int Next;
            public bool Arrived => Next >= Path.Count;
        }

        private const float WalkSpeed = 1.4f, TruckSpeed = 7f, Approach = 45f;

        private ShopDeliveries _d;
        private ShopOrder _o;
        private HomeSpec _home;
        private Phase _phase;
        private GameObject _truck;
        private Vector3 _stop, _heading, _entrance;
        private readonly List<Walker> _crew = new List<Walker>();
        private Walker _inner;
        private RollCart _cart;
        private float _wait;
        private bool _placed;
        /// <summary>The streets from the stores to the kerb, with the distance run at each point.</summary>
        private List<Vector3> _route;
        private float[] _run;
        private long _depart;

        public void Configure(ShopDeliveries d, ShopOrder o)
        {
            _d = d;
            _o = o;
            _home = d.World.Find(o.Destination) ?? d.World.MainHome;
            _entrance = d.Entrance(_home);
            RoadNetwork.Lane lane = null;
            float at = 0f;
            if (!d.Kerb(_entrance, out _stop, out _heading, out lane, out at))
            {
                _stop = _entrance + Vector3.back * 6f;
                _heading = Vector3.right;
            }
            _truck = d.Truck(o, transform);
            Quaternion facing = Quaternion.LookRotation(_heading);
            DateTime now = d.City.Game.Clock.Now;
            _route = o.State == ShopOrderStatus.OnTheWay && now.Ticks < o.Due ? d.Route(lane, at, _stop) : null;
            if (_route != null && _route.Count >= 2)
            {
                // Timed by the clock, so it pulls up at the estimated time whatever the game's speed.
                _depart = o.Due - ShopDeliveries.DriveMinutes(o) * TimeSpan.TicksPerMinute;
                _run = new float[_route.Count];
                for (int i = 1; i < _route.Count; i++) _run[i] = _run[i - 1] + Vector3.Distance(_route[i - 1], _route[i]);
                _phase = Phase.EnRoute;
                Drive();
            }
            else if (o.State == ShopOrderStatus.OnTheWay && now.Ticks < o.Due)
            {
                // No way through the streets: out of sight until it's due.
                _truck.SetActive(false);
                _phase = Phase.EnRoute;
            }
            else if (d.Seen(_stop, 90f))
            {
                _truck.transform.SetPositionAndRotation(_stop - _heading * Approach, facing);
                _phase = Phase.DriveIn;
            }
            else
            {
                _truck.transform.SetPositionAndRotation(_stop, facing);
                _phase = Phase.Parked;
                _wait = 1f;
            }
            if (_phase != Phase.EnRoute) d.City.Game.Shop.SetStatus(o, ShopOrderStatus.Unloading);
        }

        /// <summary>Where the truck is (on the road or at the kerb), for the map. False before it's anywhere to be seen.</summary>
        public bool TruckAt(out Vector3 at)
        {
            at = _truck != null ? _truck.transform.position : Vector3.zero;
            return _truck != null && _truck.activeSelf && _phase != Phase.Done;
        }

        /// <summary>On the way: the point along the route the clock says, facing along it. Arrived: parked.</summary>
        private void Drive()
        {
            long now = _d.City.Game.Clock.Now.Ticks;
            if (now >= _o.Due || _route == null)
            {
                _truck.SetActive(true);
                _truck.transform.SetPositionAndRotation(_stop, Quaternion.LookRotation(_heading));
                _d.City.Game.Shop.SetStatus(_o, ShopOrderStatus.Unloading);
                _phase = Phase.Parked;
                _wait = 1.5f;
                return;
            }
            float t = Mathf.Clamp01((float)((now - _depart) / (double)Math.Max(1L, _o.Due - _depart)));
            float want = t * _run[_run.Length - 1];
            int i = 1;
            while (i < _run.Length - 1 && _run[i] < want) i++;
            float span = Mathf.Max(0.001f, _run[i] - _run[i - 1]);
            Vector3 p = Vector3.Lerp(_route[i - 1], _route[i], Mathf.Clamp01((want - _run[i - 1]) / span));
            Vector3 dir = _route[i] - _route[i - 1];
            dir.y = 0f;
            Quaternion facing = dir.sqrMagnitude > 1e-4f ? Quaternion.LookRotation(dir) : _truck.transform.rotation;
            // Turns are eased so the truck doesn't snap round at each corner.
            _truck.transform.SetPositionAndRotation(p, Quaternion.Slerp(_truck.transform.rotation, facing, 0.2f));
        }

        /// <summary>Time ran on without us (a sleep): the goods are there, the truck and crew gone.</summary>
        public void Finish()
        {
            if (_phase == Phase.Done) return;
            if (!_placed) Place();
            End();
        }

        private void Update()
        {
            if (_d == null || _o == null) return;
            float dt = Time.deltaTime;
            switch (_phase)
            {
                case Phase.EnRoute:
                    Drive();
                    break;
                case Phase.DriveIn:
                {
                    // Slows as it nears the kerb.
                    Vector3 to = _stop - _truck.transform.position;
                    float left = to.magnitude;
                    float speed = Mathf.Clamp(left * 0.6f, 1.2f, TruckSpeed);
                    if (left <= speed * dt) { _truck.transform.position = _stop; _phase = Phase.Parked; _wait = 1.5f; }
                    else _truck.transform.position += to / left * speed * dt;
                    break;
                }
                case Phase.Parked:
                    if ((_wait -= dt) > 0f) break;
                    SendCrew();
                    _phase = Phase.CarryIn;
                    break;
                case Phase.CarryIn:
                    if (!Walk(dt)) break;
                    if (!_o.MoveIn)
                    {
                        Place();
                        foreach (Walker w in _crew) Drop(w);
                        Back();
                    }
                    else
                    {
                        // Out of sight: through the door, up the lift.
                        foreach (Walker w in _crew) w.Body.gameObject.SetActive(false);
                        _wait = _d.IsPenthouse(_o) ? 14f : 5f;
                        // They call the lift; it's at the penthouse, doors open, when they step out.
                        if (_d.IsPenthouse(_o)) HarborviewTower.ResidentsLift?.Request(_d.LiftStop(_o));
                        _phase = Phase.Inside;
                    }
                    break;
                case Phase.Inside:
                    if ((_wait -= dt) > 0f) break;
                    StartInner();
                    _phase = Phase.InnerWalk;
                    break;
                case Phase.InnerWalk:
                    if (StepInner(dt)) break;
                    foreach (Walker w in _crew) { w.Body.gameObject.SetActive(true); Drop(w); }
                    Back();
                    break;
                case Phase.Return:
                    if (!Walk(dt)) break;
                    foreach (Walker w in _crew) Destroy(w.Body.gameObject);
                    _crew.Clear();
                    _phase = Phase.Leave;
                    _wait = 0f;
                    break;
                case Phase.Leave:
                    if (!_d.Seen(_truck.transform.position, 90f)) { End(); break; }
                    // Pulls away and on up the street.
                    _wait += dt;
                    _truck.transform.position += _heading * Mathf.Min(TruckSpeed, 1f + _wait * 2.5f) * dt;
                    if ((_truck.transform.position - _stop).sqrMagnitude > Approach * Approach) End();
                    break;
            }
        }

        /// <summary>Two movers from the back of the truck to the entrance (and on in for a move-in), carrying the order.</summary>
        private void SendCrew()
        {
            Vector3 side = Vector3.Cross(Vector3.up, _heading);
            Vector3 back = _stop - _heading * 3.6f;
            for (int n = 0; n < 2; n++)
            {
                var w = new Walker { Body = _d.Mover(_o, transform, n) };
                Vector3 from = back + side * (n == 0 ? -0.5f : 0.5f);
                w.Body.transform.position = from;
                w.Load = _d.Load(_o, w.Body);
                Vector3 offset = side * (n == 0 ? -0.45f : 0.45f);
                w.Path.Add(_entrance + offset + (_stop - _entrance).normalized * 0.8f);
                if (_o.MoveIn)
                    foreach (Vector3 p in _d.WayIn(_home)) w.Path.Add(p + offset * 0.5f);
                _crew.Add(w);
            }
        }

        /// <summary>Back the way they came, empty-handed, to the truck.</summary>
        private void Back()
        {
            foreach (Walker w in _crew)
            {
                w.Path.Reverse();
                w.Path.Add(_stop - _heading * 3.6f);
                w.Next = 1;
            }
            _phase = Phase.Return;
        }

        private static void Drop(Walker w)
        {
            if (w.Load != null) Destroy(w.Load.gameObject);
            w.Load = null;
        }

        /// <summary>
        /// Inside: one mover comes out of the lift (or in at the front door) with the box, or pushing the building's cart
        /// loaded with the tech, to the spot by the lift or inside the door; sets it down and goes back.
        /// </summary>
        private void StartInner()
        {
            Vector3 entry = _d.InnerEntry(_home);
            Vector3 spot = _d.MoveInSpot(_home, 0);
            _inner = new Walker { Body = _d.Mover(_o, transform, 5) };
            _inner.Body.transform.position = entry;
            _inner.Path.Add(spot + (entry - spot).normalized * (_o.StoreKind == HomeStore.Tech ? 1.9f : 0.6f));
            if (_o.StoreKind == HomeStore.Tech && _d.IsPenthouse(_o) && _d.World.Cart != null && _d.World.Cart.Borrow())
            {
                _cart = _d.World.Cart;
                _d.PlaceGoods(_o, _entrance, _stop, _cart); // loaded on the cart as it rolls out of the lift
                _placed = true;
            }
            else _inner.Load = _d.Load(_o, _inner.Body);
        }

        /// <summary>The inside walk: there, set it down, back out of sight. False when it's over.</summary>
        private bool StepInner(float dt)
        {
            if (_inner == null) return false;
            bool done = Step(_inner, dt);
            if (_cart != null)
            {
                // Pushed ahead: the cart's push end at the mover's hands.
                Transform t = _inner.Body.transform;
                _cart.MoveTo(t.position + t.forward * 0.45f, t.eulerAngles.y);
            }
            if (!done) return true;
            if (_inner.Path.Count == 1)
            {
                // Arrived: set down, then back to where they came from.
                if (!_placed) Place();
                Drop(_inner);
                _cart = null;
                _inner.Path.Add(_d.InnerEntry(_home));
                return true;
            }
            Destroy(_inner.Body.gameObject);
            _inner = null;
            return false;
        }

        private void Place()
        {
            _d.PlaceGoods(_o, _entrance, _stop, null);
            _placed = true;
        }

        /// <summary>Moves the crew along; true when they've all arrived.</summary>
        private bool Walk(float dt)
        {
            bool all = true;
            foreach (Walker w in _crew) all &= Step(w, dt);
            return all;
        }

        private static bool Step(Walker w, float dt)
        {
            Transform t = w.Body.transform;
            if (w.Arrived)
            {
                w.Body.Animate(NpcPose.Stand, Time.time);
                return true;
            }
            Vector3 to = w.Path[w.Next] - t.position;
            to.y = 0f;
            float left = to.magnitude;
            if (left <= WalkSpeed * dt)
            {
                t.position = new Vector3(w.Path[w.Next].x, w.Path[w.Next].y, w.Path[w.Next].z);
                w.Next++;
            }
            else
            {
                Vector3 step = to / left * WalkSpeed * dt;
                t.position += step;
                // Follow the ground (kerbs, the stoop).
                t.position = new Vector3(t.position.x, Mathf.MoveTowards(t.position.y, w.Path[w.Next].y, 2f * dt), t.position.z);
                t.rotation = Quaternion.Slerp(t.rotation, Quaternion.LookRotation(to), 0.25f);
            }
            w.Body.Animate(NpcPose.Walk, Time.time, 1f);
            return w.Arrived;
        }

        private void End()
        {
            _phase = Phase.Done;
            if (_cart != null) _cart = null;
            _d.Done(_o);
            Destroy(gameObject);
        }
    }
}
