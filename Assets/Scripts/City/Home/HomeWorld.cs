using System;
using System.Collections.Generic;
using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Keeps the world in step with what the player owns (TOWN_SPEC B): an object for every item that's standing
    /// somewhere or riding in a vehicle, the homes and which are yours, deliveries arriving, and the loaner's
    /// reminders. The carrying and placing is <see cref="Carrier"/>'s.
    /// </summary>
    public sealed class HomeWorld : MonoBehaviour
    {
        private CityContext _c;
        private Transform _root;
        private readonly Dictionary<int, ItemView> _views = new Dictionary<int, ItemView>();
        private readonly List<CargoBed> _beds = new List<CargoBed>();
        private int _shownVersion = -1;
        private float _nextTick;

        public CityContext City => _c;
        public GameBootstrap Game => _c.Game;
        public Belongings Belongings => _c.Game.Belongings;
        public Estate Estate => _c.Game.Estate;
        public Carrier Hands { get; private set; }
        public Loaner Loaner { get; set; }
        public IReadOnlyList<HomeSpec> Homes => _c.Homes;
        /// <summary>The store's collection yard: things may be set down here before loading.</summary>
        public Rect PickupYard { get; set; }
        public IReadOnlyList<CargoBed> Beds => _beds;
        public event Action<string> Said;

        public void Configure(CityContext c, FirstPersonController player, PlayerInteractor interactor)
        {
            _c = c;
            _root = Kit.Group(c.Dynamic, "Belongings");
            Hands = player.gameObject.AddComponent<Carrier>();
            Hands.Configure(this, player, interactor);
        }

        public void Say(string line)
        {
            _c.Hud?.ShowToast(line, 4f);
            Said?.Invoke(line);
        }

        public static string Dollars(decimal d) => "$" + d.ToString("N0", CultureInfo.InvariantCulture);

        // ---- homes ----

        public bool Owns(HomeSpec h) => h != null && (h.Id == HomeSpec.ApartmentId || Estate.Owns(h.Id));

        /// <summary>The home of yours that <paramref name="world"/> is in (indoors or on its lot), or null.</summary>
        public HomeSpec HomeAt(Vector3 world)
        {
            foreach (HomeSpec h in _c.Homes)
                if (Owns(h) && h.Contains(world)) return h;
            return null;
        }

        /// <summary>Any home (yours or on the market) that <paramref name="world"/> is in, or null.</summary>
        public HomeSpec AnyHomeAt(Vector3 world) => _c.Homes.Find(h => h.Contains(world));

        public HomeSpec Find(string id) => _c.Homes.Find(h => h.Id == id);

        /// <summary>Buys a home outright from the bank. An error, or null.</summary>
        public string Buy(HomeSpec h)
        {
            if (Owns(h)) return "It's already yours.";
            string error = Game.Economy.Spend(h.Price, "Home: " + h.Name, Game.Clock.Now);
            if (error != null) return error;
            Estate.Acquire(h.Id);
            foreach (var (id, variant, at, yaw) in h.Staging)
            {
                OwnedItem item = Belongings.Add(id, variant, ItemState.Placed);
                item.Boxed = false;
                item.Property = h.Id;
                item.X = at.x; item.Y = at.y; item.Z = at.z;
                item.Yaw = yaw;
            }
            if (h.Staging.Count > 0) Belongings.Touch();
            return null;
        }

        /// <summary>Where deliveries go: the last home you bought, else the apartment.</summary>
        public HomeSpec MainHome
        {
            get
            {
                HomeSpec best = Find(HomeSpec.ApartmentId);
                foreach (HomeSpec h in _c.Homes)
                    if (h.Id != HomeSpec.ApartmentId && Estate.Owns(h.Id)) best = h;
                return best;
            }
        }

        // ---- cargo ----

        public void AddBed(CargoBed bed) { if (!_beds.Contains(bed)) _beds.Add(bed); _shownVersion = -1; }
        public void RemoveBed(CargoBed bed) { _beds.Remove(bed); _shownVersion = -1; }
        public CargoBed Bed(string id) => _beds.Find(b => b != null && b.Id == id);

        /// <summary>Cubic metres already loaded on a bed.</summary>
        public float Loaded(CargoBed bed)
        {
            float v = 0f;
            foreach (OwnedItem i in Belongings.Items)
                if (i.State == ItemState.Loaded && i.Vehicle == bed.Id) v += i.Item.Volume;
            return v;
        }

        public bool HasMounted(OwnedItem item)
        {
            foreach (OwnedItem _ in Belongings.MountedOn(item.Uid)) return true;
            return false;
        }

        // ---- the objects ----

        public ItemView View(int uid) => _views.TryGetValue(uid, out ItemView v) ? v : null;
        public IEnumerable<ItemView> Views => _views.Values;

        /// <summary>Makes (or remakes) the object for an item, posed where the item says.</summary>
        public ItemView Show(OwnedItem item)
        {
            if (_views.TryGetValue(item.Uid, out ItemView old) && old != null) Destroy(old.gameObject);
            var go = new GameObject("Item " + item.Uid + " " + item.ItemId);
            go.transform.SetParent(_root, false);
            HomeModels.Build(_c, go.transform, item.Item, item.Variant, item.Boxed);
            var view = go.AddComponent<ItemView>();
            view.Configure(this, item);
            if (item.Item.IsMonitor && !item.Boxed) go.AddComponent<MonitorScreen>().Configure(this, view);
            _views[item.Uid] = view;
            Pose(view);
            return view;
        }

        public void Hide(int uid)
        {
            if (!_views.TryGetValue(uid, out ItemView v)) return;
            if (v != null) Destroy(v.gameObject);
            _views.Remove(uid);
        }

        /// <summary>Puts an item's object where its record says: in the world, or in a vehicle's frame.</summary>
        public void Pose(ItemView view)
        {
            OwnedItem i = view.Item;
            Transform t = view.transform;
            var local = new Vector3(i.X, i.Y, i.Z);
            if (i.State == ItemState.Loaded && Bed(i.Vehicle) is CargoBed bed && bed.Vehicle != null)
            {
                t.SetParent(bed.Vehicle, false);
                t.localPosition = local;
                t.localRotation = Quaternion.Euler(0f, i.Yaw, 0f);
                view.Vehicle = bed.Vehicle;
                // Cargo is part of the vehicle now: a trigger (still aimable) so it doesn't reshape its physics.
                view.Solid.isTrigger = true;
            }
            else
            {
                t.SetParent(_root, true);
                t.SetPositionAndRotation(local, Quaternion.Euler(0f, i.Yaw, 0f));
                view.Vehicle = null;
                view.Solid.isTrigger = false;
            }
            // Monitors on an arm hang by their backs: no stand.
            bool onArm = i.MountedOn != 0 && Belongings.Get(i.MountedOn)?.Item?.IsArm == true;
            foreach (Transform part in t.GetComponentsInChildren<Transform>(true))
                if (part.name == "Base" || part.name == "Neck") part.gameObject.SetActive(!onArm);
            Transform panel = FindChild(t, "Panel");
            if (panel != null) panel.localRotation = Quaternion.Euler(0f, 0f, i.Portrait ? 90f : 0f);
        }

        private static Transform FindChild(Transform t, string name)
        {
            foreach (Transform c in t.GetComponentsInChildren<Transform>(true)) if (c.name == name) return c;
            return null;
        }

        /// <summary>Objects for everything placed or loaded; nothing for what's at the store or on its way.</summary>
        private void Sync()
        {
            _shownVersion = Belongings.Version;
            var keep = new HashSet<int>();
            foreach (OwnedItem i in Belongings.Items)
            {
                bool shown = i.State == ItemState.Placed || i.State == ItemState.Carried
                             || (i.State == ItemState.Loaded && Bed(i.Vehicle) != null);
                if (!shown) continue;
                keep.Add(i.Uid);
                if (!_views.ContainsKey(i.Uid) || _views[i.Uid] == null)
                {
                    ItemView v = Show(i);
                    if (i.State == ItemState.Carried) Hands.Hold(v);
                }
            }
            var gone = new List<int>();
            foreach (int uid in _views.Keys) if (!keep.Contains(uid)) gone.Add(uid);
            foreach (int uid in gone) Hide(uid);
            Seats();
        }

        /// <summary>
        /// Any desk with a screen on it is somewhere to trade: a seat in front of it that sits you down at the terminal
        /// (the terminal fills your view; the desk's monitors keep showing what they show).
        /// </summary>
        private void Seats()
        {
            foreach (ItemView v in _views.Values)
            {
                if (v == null || !v.Spec.IsDesk) continue;
                Transform seat = v.transform.Find("Trading seat");
                bool wants = v.Item.State == ItemState.Placed && Belongings.MonitorsOn(v.Item) > 0 && _c.Workstation != null;
                if (!wants)
                {
                    if (seat != null) Destroy(seat.gameObject);
                    continue;
                }
                if (seat != null) continue;
                seat = Kit.Group(v.transform, "Trading seat", new Vector3(0f, 0f, -v.Spec.Depth / 2f - 0.55f));
                var box = seat.gameObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.center = new Vector3(0f, 0.9f, 0f);
                box.size = new Vector3(Mathf.Min(1.2f, v.Spec.Width), 0.6f, 0.5f);
                Transform view = Kit.Group(seat, "Seat view", new Vector3(0f, 1.22f, -0.35f));
                view.localRotation = Quaternion.LookRotation(new Vector3(0f, -0.28f, 1f));
                Transform stand = Kit.Group(seat, "Stand point", new Vector3(0f, 0.05f, -0.7f));
                // The desk wants a renderer for the terminal image; the monitors keep their own pictures.
                GameObject dummy = GameObject.CreatePrimitive(PrimitiveType.Quad);
                Destroy(dummy.GetComponent<Collider>());
                dummy.name = "Terminal target";
                dummy.transform.SetParent(seat, false);
                dummy.GetComponent<MeshRenderer>().enabled = false;
                var desk = seat.gameObject.AddComponent<Desk>();
                desk.Configure(_c.Workstation, view, stand, dummy.GetComponent<MeshRenderer>());
                seat.gameObject.AddComponent<SeatInteractable>().Configure(_c.Workstation, desk, "Trade at this desk");
            }
        }

        private void Update()
        {
            if (_c == null) return;
            if (Belongings.Version != _shownVersion) Sync();
            if (Time.unscaledTime < _nextTick) return;
            _nextTick = Time.unscaledTime + 0.5f;
            Deliveries();
            Reminders();
        }

        // ---- deliveries ----

        /// <summary>Home delivery: the fee and when it arrives (next morning at nine, or four hours if it's early).</summary>
        public const decimal DeliveryFee = 79m;

        public static DateTime DeliveryTime(DateTime now) =>
            now.Hour < 13 ? now.Date.AddHours(Math.Max(now.Hour + 4, 9)) : now.Date.AddDays(1).AddHours(9);

        /// <summary>Everything waiting at the store goes on the van. An error, or null.</summary>
        public string BookDelivery()
        {
            var waiting = Belongings.In(ItemState.AtPickup);
            if (waiting.Count == 0) return "Nothing's waiting to be delivered.";
            HomeSpec home = MainHome;
            string error = Game.Economy.Spend(DeliveryFee, "Timberline Home delivery", Game.Clock.Now);
            if (error != null) return error;
            DateTime at = DeliveryTime(Game.Clock.Now);
            foreach (OwnedItem i in waiting)
            {
                i.State = ItemState.Delivering;
                i.DeliverAt = at.Ticks;
                i.Property = home.Id;
            }
            Belongings.Touch();
            Say($"{waiting.Count} item{(waiting.Count == 1 ? "" : "s")} to {home.Name}, {at:ddd h tt}.");
            return null;
        }

        private void Deliveries()
        {
            DateTime now = Game.Clock.Now;
            int row = 0;
            foreach (OwnedItem i in Belongings.Items)
            {
                if (i.State != ItemState.Delivering || now.Ticks < i.DeliverAt) continue;
                HomeSpec home = Find(i.Property) ?? MainHome;
                // Stacked just inside the front door, in rows.
                Vector3 spot = home.Root.TransformPoint(home.DoorLocal + new Vector3((row % 3 - 1) * 1.1f, 0f, 1.6f + row / 3 * 1.2f));
                i.State = ItemState.Placed;
                i.X = spot.x; i.Y = GroundAt(spot); i.Z = spot.z;
                i.Yaw = home.Root.eulerAngles.y;
                row++;
            }
            if (row == 0) return;
            Belongings.Touch();
            Say($"Your delivery is in: {row} item{(row == 1 ? "" : "s")} inside the front door.");
        }

        private static float GroundAt(Vector3 p) =>
            Physics.Raycast(p + Vector3.up * 1.5f, Vector3.down, out RaycastHit hit, 4f, ~0, QueryTriggerInteraction.Ignore) ? hit.point.y : p.y;

        private void Reminders()
        {
            foreach (int minutes in Game.Rental.RemindersDue(Game.Clock.Now))
                Say(minutes >= 60 ? "Timberline Home: the loaner's due back in an hour." : $"Timberline Home: the loaner's due back in {minutes} minutes.");
        }
    }
}
