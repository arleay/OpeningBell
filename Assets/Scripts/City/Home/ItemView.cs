using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// An owned item standing in the world. [E] picks it up; aiming at a monitor, [F] switches it on or off, [V] changes
    /// what it shows, [T] the symbol, [P] portrait/landscape (handled by the <see cref="Carrier"/>).
    /// </summary>
    public sealed class ItemView : Interactable
    {
        private HomeWorld _world;
        public OwnedItem Item { get; private set; }
        public HomeItem Spec => Item.Item;
        public BoxCollider Solid { get; private set; }
        /// <summary>The vehicle it rides in, if loaded.</summary>
        public Transform Vehicle { get; set; }

        public void Configure(HomeWorld world, OwnedItem item)
        {
            _world = world;
            Item = item;
            Solid = gameObject.AddComponent<BoxCollider>();
            HomeItem s = item.Item;
            float pad = item.Boxed ? 0.08f : 0f;
            Solid.size = new Vector3(s.Width + pad, Mathf.Max(0.05f, s.Height + pad), Mathf.Max(0.05f, s.Depth + pad));
            Solid.center = new Vector3(0f, Solid.size.y / 2f, 0f);
        }

        public override bool CanInteract => base.CanInteract && _world != null && !_world.Hands.Holding;

        public override string Prompt
        {
            get
            {
                if (Item.IsBox)
                {
                    if (Item.Closed && OpensHere) return "Open the box";
                    return "Carry the box";
                }
                if (Spec.IsDesk && _world.HasMounted(Item)) return "Clear the desk to move it";
                return (Item.Boxed ? "Pick up boxed " : "Pick up ") + Spec.Name;
            }
        }

        public override string Details
        {
            get
            {
                if (Item.IsBox)
                {
                    int n = _world.Belongings.Contents(Item).Count;
                    string inside = n == 0 ? "Empty" : $"{n} thing{(n == 1 ? "" : "s")} inside, {_world.Belongings.Contents(Item)[n - 1].Item.Name} on top";
                    if (Item.Closed) return inside + "\n[LMB] open the flaps";
                    string keys = (n > 0 ? "[LMB] take the top one out  " : "[F] throw it away  ") + "[Q] fold the flaps shut";
                    return inside + "\n" + keys + "\nHolding something: [LMB] on the box packs it";
                }
                string condition = Item.Condition >= 0.99 ? "new" : $"{Item.Condition * 100:0}% condition";
                string colour = Spec.Variants.Length > 1 ? Spec.Variants[Item.Variant] + " · " : "";
                string line = $"{colour}{Spec.Tier} · {condition}";
                if (Spec.IsDesk) line += $"\n{_world.Belongings.MonitorsOn(Item)} of {Belongings.MaxMonitorsPerDesk} screens · takes {Spec.MonitorSlots} standing";
                if (Spec.IsMonitor && !Item.Boxed)
                    line += $"\n{(Item.Power ? "On" : "Off")} · {Item.View}{(Item.View == MonitorView.Chart ? " " + Item.Symbol : "")}{(Item.Portrait ? " · portrait" : "")}\n[F] power  [V] view  [T] symbol  [P] rotate";
                return line + "\nSells for about $" + Belongings.ResaleValue(Item).ToString("N0", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>A closed box standing in one of your homes other than where it was shut: [E] opens it (unpacking).</summary>
        private bool OpensHere
        {
            get
            {
                if (Item.State != ItemState.Placed) return false;
                HomeSpec home = _world.Find(Item.Property);
                return home != null && _world.Owns(home) && Item.ClosedAt != home.Id;
            }
        }

        public override void Interact()
        {
            if (Item.IsBox && Item.Closed && OpensHere)
            {
                _world.Hands.OpenBox(Item);
                return;
            }
            if (Spec.IsDesk && _world.HasMounted(Item))
            {
                _world.Say("Take the screens and things off it first.");
                return;
            }
            _world.Hands.PickUp(this);
        }
    }
}
