using System;
using System.Collections.Generic;
using OpeningBell.Trading;

namespace OpeningBell.Home
{
    /// <summary>Where an owned item is right now.</summary>
    public enum ItemState
    {
        /// <summary>Paid for, waiting at the store's pickup bay.</summary>
        AtPickup,
        /// <summary>In the player's hands.</summary>
        Carried,
        /// <summary>Loaded in a vehicle or trailer (<see cref="OwnedItem.Vehicle"/>).</summary>
        Loaded,
        /// <summary>Standing somewhere in the world (a property, or dropped).</summary>
        Placed,
        /// <summary>Booked for home delivery (arrives at <see cref="OwnedItem.DeliverAt"/>).</summary>
        Delivering,
        /// <summary>Put away in a home's storage (<see cref="OwnedItem.Property"/>).</summary>
        Stored,
    }

    /// <summary>What a monitor shows.</summary>
    public enum MonitorView { Chart, Watchlist, Portfolio, News, Market, Blank }

    [Serializable]
    public sealed class OwnedItem
    {
        public int Uid;
        public string ItemId;
        public int Variant;
        /// <summary>1 new, 0 wrecked.</summary>
        public double Condition = 1;
        public ItemState State;
        public bool Boxed;
        /// <summary>Property it stands in (Placed), or where a delivery goes.</summary>
        public string Property = "";
        /// <summary>Vehicle or trailer id it's loaded in.</summary>
        public string Vehicle = "";
        public float X, Y, Z, Yaw;
        public long DeliverAt;
        /// <summary>Monitors on a desk or an arm; arms on a desk: the item they hang from (0 = none).</summary>
        public int MountedOn;
        // Monitors.
        public bool Power = true;
        public MonitorView View;
        public string Symbol = "";
        public bool Portrait;

        public HomeItem Item => HomeCatalog.Find(ItemId);
    }

    [Serializable]
    public sealed class BelongingsSaveData
    {
        public int NextUid = 1;
        public List<OwnedItem> Items = new List<OwnedItem>();
    }

    /// <summary>
    /// Everything the player has bought from the home stores, wherever it is (TOWN_SPEC B). Money is handled by the
    /// callers (the store and the phone): this is the list, the desk and arm rules, and resale values.
    /// </summary>
    public sealed class Belongings
    {
        /// <summary>No desk carries more than this many screens, however it's set up.</summary>
        public const int MaxMonitorsPerDesk = 6;

        private readonly List<OwnedItem> _items = new List<OwnedItem>();
        private int _nextUid = 1;

        public IReadOnlyList<OwnedItem> Items => _items;
        /// <summary>Goes up on every change, so views rebuild only then.</summary>
        public int Version { get; private set; }

        public OwnedItem Get(int uid) => _items.Find(i => i.Uid == uid);
        public List<OwnedItem> In(ItemState state) => _items.FindAll(i => i.State == state);
        public int Count(ItemState state) => _items.FindAll(i => i.State == state).Count;

        public OwnedItem Add(string itemId, int variant, ItemState state)
        {
            HomeItem spec = HomeCatalog.Find(itemId) ?? throw new ArgumentException("Unknown item " + itemId);
            var item = new OwnedItem
            {
                Uid = _nextUid++, ItemId = itemId, Variant = Math.Max(0, Math.Min(variant, spec.Variants.Length - 1)),
                State = state, Boxed = spec.Boxed, Symbol = "",
            };
            _items.Add(item);
            Touch();
            return item;
        }

        public void Remove(OwnedItem item)
        {
            // Whatever hung from it comes loose.
            foreach (OwnedItem other in _items)
                if (other.MountedOn == item.Uid) other.MountedOn = 0;
            _items.Remove(item);
            Touch();
        }

        public void Touch() => Version++;

        /// <summary>What a buyer pays: 70% of the price when new, down to 40% when worn out; less for opened budget stuff.</summary>
        public static decimal ResaleValue(OwnedItem item)
        {
            HomeItem spec = item.Item;
            if (spec == null) return 0m;
            double share = 0.4 + 0.3 * Math.Max(0, Math.Min(1, item.Condition));
            if (!item.Boxed && spec.Tier == Tier.Budget) share -= 0.05;
            return Money.RoundCents(spec.Price * (decimal)share);
        }

        // ---- desks, arms and monitors ----

        public IEnumerable<OwnedItem> MountedOn(int uid)
        {
            foreach (OwnedItem i in _items)
                if (i.MountedOn == uid) yield return i;
        }

        /// <summary>Screens a desk carries: stood on it, plus those on its arms.</summary>
        public int MonitorsOn(OwnedItem desk)
        {
            int n = 0;
            foreach (OwnedItem i in MountedOn(desk.Uid))
            {
                if (i.Item?.IsMonitor == true) n++;
                else if (i.Item?.IsArm == true) foreach (OwnedItem m in MountedOn(i.Uid)) if (m.Item?.IsMonitor == true) n++;
            }
            return n;
        }

        /// <summary>
        /// Can <paramref name="monitor"/> go on <paramref name="target"/> (a desk, or an arm clamped to one)? Null if
        /// yes, else why not. A desk stands as many screens as its slots; arms add their own count; never more than
        /// <see cref="MaxMonitorsPerDesk"/> on one desk.
        /// </summary>
        public string CanMountMonitor(OwnedItem monitor, OwnedItem target)
        {
            if (monitor.Item?.IsMonitor != true) return "That's not a monitor.";
            HomeItem t = target.Item;
            if (t == null) return "Nothing to put it on.";
            OwnedItem desk = t.IsArm ? Get(target.MountedOn) : t.IsDesk ? target : null;
            if (desk == null) return t.IsArm ? "The arm needs clamping to a desk first." : "Monitors go on a desk or a monitor arm.";
            if (MonitorsOn(desk) >= MaxMonitorsPerDesk) return $"A desk takes {MaxMonitorsPerDesk} screens at most.";
            if (t.IsArm)
            {
                int onArm = 0;
                foreach (OwnedItem m in MountedOn(target.Uid)) if (m.Item?.IsMonitor == true) onArm++;
                return onArm < t.Arms ? null : $"The {t.Name.ToLowerInvariant()} holds {t.Arms}.";
            }
            int standing = 0;
            foreach (OwnedItem m in MountedOn(desk.Uid)) if (m.Item?.IsMonitor == true) standing++;
            return standing < desk.Item.MonitorSlots ? null : $"The {desk.Item.Name.ToLowerInvariant()} has room for {desk.Item.MonitorSlots}; add a monitor arm.";
        }

        /// <summary>Can an arm clamp to this desk? Null if yes. One arm per desk.</summary>
        public string CanMountArm(OwnedItem arm, OwnedItem desk)
        {
            if (arm.Item?.IsArm != true) return "That's not a monitor arm.";
            if (desk.Item?.IsDesk != true) return "Arms clamp to a desk.";
            foreach (OwnedItem i in MountedOn(desk.Uid)) if (i.Item?.IsArm == true) return "That desk already has an arm.";
            return null;
        }

        public BelongingsSaveData CaptureState() => new BelongingsSaveData { NextUid = _nextUid, Items = new List<OwnedItem>(_items) };

        public void RestoreState(BelongingsSaveData data)
        {
            _items.Clear();
            if (data == null) return;
            foreach (OwnedItem i in data.Items)
                if (HomeCatalog.Find(i.ItemId) != null) _items.Add(i);
            _nextUid = Math.Max(data.NextUid, 1);
            foreach (OwnedItem i in _items) _nextUid = Math.Max(_nextUid, i.Uid + 1);
            Touch();
        }
    }
}
