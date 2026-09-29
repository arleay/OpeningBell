using System.Collections.Generic;
using OpeningBell.Gameplay;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Homes on the market (TOWN_SPEC B): a sign out front with the price (buy with a second press; the money comes
    /// from the bank), open-house viewings by day, and once it's yours: the front door locks from a keypad, a switch
    /// by the door for the lights, a garage with a roll-up door behind the family houses and mansions. The Harborview
    /// penthouse (<see cref="HarborviewTower"/>) is the tower's top floor, up the residents' lift.
    /// </summary>
    public static class HomeSales
    {
        public static readonly Hours OpenHouse = Hours.Of(10, 18);
        public const string PenthouseId = "penthouse";

        public static void Build(CityContext c, HomeWorld world)
        {
            Transform root = Kit.Group(c.Static, "Homes for sale");
            Transform dyn = Kit.Group(c.Dynamic, "Homes for sale");
            c.Homes.Add(HarborviewTower.Penthouse(c, world, root, dyn));
            c.Homes.Add(HarborviewOffice.Build(c, world, root, dyn));
            var doors = new List<Door>(Object.FindObjectsByType<Door>(FindObjectsSortMode.None));
            foreach (HomeSpec h in c.Homes)
            {
                if (h.Id == HomeSpec.ApartmentId) continue;
                Door door = Nearest(doors, h.Root.TransformPoint(h.DoorLocal));
                if (door != null)
                {
                    HomeSpec spec = h;
                    // Level 26 is open for viewings until a fund takes it; then its keypad decides.
                    door.LockReason = () => world.Owns(spec)
                        ? (world.Estate.Locked(spec.Id) ? "locked (keypad)" : null)
                        : spec.Kind == HomeKind.Office || OpenHouse.Contains(world.Game.Clock.Now) ? null : "for sale · open house 10 AM–6 PM";
                }
                if (h.Kind != HomeKind.Penthouse && h.Kind != HomeKind.Office) Sign(c, world, dyn, h);
                // Every home's frame faces its way in along -z (the penthouse's faces the lift).
                const float s = 1f;
                float yaw = h.Root.eulerAngles.y;
                Keypad(c, world, dyn, h, h.Root.TransformPoint(h.DoorLocal + new Vector3(1.1f, 1.3f, -0.12f * s)), yaw);
                Switch(c, world, dyn, h, h.Root.TransformPoint(h.DoorLocal + new Vector3(-0.9f, 1.3f, 0.2f * s)), yaw + 180f);
                Transform cupboard = Kit.Group(dyn, "Storage " + h.Id, h.Root.TransformPoint(h.DoorLocal + new Vector3(-1.5f, 0f, 0.35f * s)), yaw + 180f);
                c.Kit.Box(cupboard, "Cupboard", new Vector3(0f, 1f, 0f), new Vector3(0.6f, 2f, 0.3f), c.P.Lit(new Color(0.8f, 0.78f, 0.72f), 0.2f))
                    .AddComponent<StorageCupboard>().Configure(world, h);
                if (h.Kind == HomeKind.Family || h.Kind == HomeKind.Mansion) Garage(c, world, root, dyn, h);
            }
        }

        private static Door Nearest(List<Door> doors, Vector3 p)
        {
            Door best = null;
            float bestD = 2.5f;
            foreach (Door d in doors)
            {
                float dist = Vector2.Distance(new Vector2(d.transform.position.x, d.transform.position.z), new Vector2(p.x, p.z));
                if (dist < bestD && Mathf.Abs(d.transform.position.y - p.y) < 1.5f) { best = d; bestD = dist; }
            }
            return best;
        }

        private static void Sign(CityContext c, HomeWorld world, Transform dyn, HomeSpec h)
        {
            Kit k = c.Kit;
            Vector3 local = new Vector3(h.LotWidth / 2f - 1.3f, 0f, 1.3f);
            Transform g = Kit.Group(dyn, "For sale " + h.Id, h.Plot.TransformPoint(local), h.Plot.eulerAngles.y);
            Material post = c.P.Lit(new Color(0.95f, 0.95f, 0.93f), 0.2f);
            k.Box(g, "Post", new Vector3(0f, 0.7f, 0f), new Vector3(0.08f, 1.4f, 0.08f), post, collider: false);
            k.Box(g, "Arm", new Vector3(-0.35f, 1.35f, 0f), new Vector3(0.7f, 0.06f, 0.06f), post, collider: false);
            GameObject board = k.Box(g, "Board", new Vector3(-0.35f, 1f, 0f), new Vector3(0.62f, 0.45f, 0.03f), c.P.Lit(new Color(0.75f, 0.12f, 0.1f), 0.3f));
            TextMesh text = k.Text(g, "FOR SALE", new Vector3(-0.35f, 1.05f, -0.02f), 0f, 0.07f, Color.white);
            k.Text(g, "KELL VALLEY REALTY", new Vector3(-0.35f, 0.88f, -0.02f), 0f, 0.035f, Color.white);
            board.AddComponent<ForSaleSign>().Configure(world, h, text);
        }

        private static void Keypad(CityContext c, HomeWorld world, Transform dyn, HomeSpec h, Vector3 at, float yaw)
        {
            Transform g = Kit.Group(dyn, "Keypad " + h.Id, at, yaw);
            GameObject pad = c.Kit.Box(g, "Keypad", Vector3.zero, new Vector3(0.12f, 0.18f, 0.03f), c.P.Lit(new Color(0.15f, 0.15f, 0.16f), 0.5f));
            pad.AddComponent<HomeKeypad>().Configure(world, h);
        }

        private static void Switch(CityContext c, HomeWorld world, Transform dyn, HomeSpec h, Vector3 at, float yaw)
        {
            Transform g = Kit.Group(dyn, "Light switch " + h.Id, at, yaw);
            GameObject plate = c.Kit.Box(g, "Switch", Vector3.zero, new Vector3(0.08f, 0.12f, 0.02f), c.P.Lit(new Color(0.95f, 0.95f, 0.93f), 0.2f));
            plate.AddComponent<LightSwitch>().Configure(world, h);
        }

        /// <summary>A single garage at the back of the lot, its roll-up door facing the street.</summary>
        private static void Garage(CityContext c, HomeWorld world, Transform root, Transform dyn, HomeSpec h)
        {
            const float w = 4.4f, d = 6.8f, height = 2.9f;
            if (h.LotDepth < 34f) return; // not enough garden behind the house
            Kit k = c.Kit;
            Vector3 origin = new Vector3(0f, 0f, h.LotDepth - d - 1f);
            Transform g = Kit.Group(root, "Garage " + h.Id, h.Plot.TransformPoint(origin), h.Plot.eulerAngles.y);
            Material wall = c.P.Lit(new Color(0.85f, 0.83f, 0.78f), 0.08f);
            k.Span(g, "Slab", new Vector3(-w / 2f, -0.05f, 0f), new Vector3(w / 2f, 0.05f, d), c.P.Lit(new Color(0.6f, 0.6f, 0.58f), 0.1f));
            k.Span(g, "Wall L", new Vector3(-w / 2f, 0f, 0f), new Vector3(-w / 2f + 0.15f, height, d), wall);
            k.Span(g, "Wall R", new Vector3(w / 2f - 0.15f, 0f, 0f), new Vector3(w / 2f, height, d), wall);
            k.Span(g, "Back", new Vector3(-w / 2f, 0f, d - 0.15f), new Vector3(w / 2f, height, d), wall);
            k.Span(g, "Roof", new Vector3(-w / 2f - 0.2f, height, -0.2f), new Vector3(w / 2f + 0.2f, height + 0.2f, d + 0.2f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f)));
            Transform door = Kit.Group(dyn, "Garage door " + h.Id, h.Plot.TransformPoint(origin), h.Plot.eulerAngles.y);
            GameObject panel = k.Box(door, "Roll-up door", new Vector3(0f, (height - 0.1f) / 2f, 0.05f), new Vector3(w - 0.3f, height - 0.1f, 0.06f), c.P.Lit(new Color(0.8f, 0.8f, 0.8f), 0.3f));
            GameObject button = k.Box(g, "Opener", new Vector3(w / 2f + 0.05f, 1.2f, 0.2f), new Vector3(0.05f, 0.14f, 0.1f), c.P.Lit(new Color(0.2f, 0.2f, 0.22f), 0.4f));
            _ = panel;
            button.AddComponent<GarageButton>().Configure(world, h, door, height);
        }
    }

    /// <summary>The realtor's sign: price and rooms; [E] twice buys it (from the bank).</summary>
    public sealed class ForSaleSign : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;
        private TextMesh _text;
        private float _armedUntil;

        public void Configure(HomeWorld world, HomeSpec home, TextMesh text)
        {
            _w = world;
            _home = home;
            _text = text;
        }

        private bool Owned => _w.Owns(_home);

        public override string Prompt => Owned ? $"{_home.Name} · yours" : $"Buy {_home.Name} · {HomeWorld.Dollars(_home.Price)}";

        public override string Details => _home.Kind switch
        {
            HomeKind.Starter => "One storey: living and kitchen, a bedroom, a bathroom. Sold empty.",
            HomeKind.Family => "Two storeys: living, kitchen, bath; two bedrooms upstairs; garage out back. Sold empty.",
            HomeKind.Mansion => "Modern villa: foyer, living and dining wings, three bedrooms, a pool and a garage. Sold empty.",
            HomeKind.Penthouse => "The whole top floor of Harborview Tower, 88 m up: glass all round, great room over the harbour, marble kitchen, office, master suite and bath; stairs to a roof terrace with a pool. Private lift. Comes furnished.",
            _ => null,
        } + (Owned ? "" : "\nOpen house 10 AM–6 PM: go and have a look first.");

        private void Update()
        {
            if (_text != null) _text.text = Owned ? "SOLD" : _home.Kind == HomeKind.Penthouse ? "PENTHOUSE" : "FOR SALE";
        }

        public override void Interact()
        {
            if (Owned) { _w.Say("It's yours. Keys work, the keypad's by the front door."); return; }
            if (Time.unscaledTime > _armedUntil)
            {
                _armedUntil = Time.unscaledTime + 6f;
                _w.Say($"Buy {_home.Name} for {HomeWorld.Dollars(_home.Price)}? It comes out of the bank. [E] again to sign.");
                return;
            }
            _armedUntil = 0f;
            string error = _w.Buy(_home);
            _w.Say(error ?? $"Congratulations: {_home.Name} is yours.");
        }
    }

    /// <summary>Front-door keypad: lock or unlock (yours only).</summary>
    public sealed class HomeKeypad : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;

        public void Configure(HomeWorld world, HomeSpec home)
        {
            _w = world;
            _home = home;
        }

        public override bool CanInteract => base.CanInteract && _w.Owns(_home);
        public override string Prompt => _w.Estate.Locked(_home.Id) ? "Unlock the door" : "Lock the door";
        public override void Interact() => _w.Estate.SetLocked(_home.Id, !_w.Estate.Locked(_home.Id));
    }

    /// <summary>The hall cupboard: what you've put away at this home, one thing at a time back into your hands.</summary>
    public sealed class StorageCupboard : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;

        public void Configure(HomeWorld world, HomeSpec home)
        {
            _w = world;
            _home = home;
        }

        private OwnedItem Next => _w.Belongings.In(ItemState.Stored).Find(i => i.Property == _home.Id);
        public override bool CanInteract => base.CanInteract && _w.Owns(_home) && !_w.Hands.Holding;
        public override string Prompt => Next is OwnedItem i ? $"Take out the {i.Item.Name}" : "Storage (empty · [B] while carrying to put away)";

        public override void Interact()
        {
            if (Next is OwnedItem i) _w.Hands.TakeNew(i);
        }
    }

    /// <summary>The lights in a home you own: all on or off from the switch by the door.</summary>
    public sealed class LightSwitch : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;
        private readonly Dictionary<Light, float> _full = new Dictionary<Light, float>();
        private int _applied = -1;

        public void Configure(HomeWorld world, HomeSpec home)
        {
            _w = world;
            _home = home;
        }

        public override bool CanInteract => base.CanInteract && _w.Owns(_home);
        public override string Prompt => _w.Estate.LightsOn(_home.Id) ? "Lights off" : "Lights on";
        public override void Interact() => _w.Estate.SetLights(_home.Id, !_w.Estate.LightsOn(_home.Id));

        private void Update()
        {
            if (_w == null || _applied == _w.Estate.Version) return;
            _applied = _w.Estate.Version;
            bool on = !_w.Owns(_home) || _w.Estate.LightsOn(_home.Id);
            // Intensity, not enabled: the distance gating owns that switch.
            foreach (Light l in _home.Root.GetComponentsInChildren<Light>(true))
            {
                if (!_full.ContainsKey(l)) _full[l] = l.intensity;
                l.intensity = on ? _full[l] : 0f;
            }
        }
    }

    /// <summary>The garage door opener: rolls the door up into the roof and back down.</summary>
    public sealed class GarageButton : Interactable
    {
        private HomeWorld _w;
        private HomeSpec _home;
        private Transform _door;
        private Vector3 _closedAt;
        private float _height, _open;

        public void Configure(HomeWorld world, HomeSpec home, Transform door, float height)
        {
            _w = world;
            _home = home;
            _door = door;
            _closedAt = door.localPosition;
            _height = height;
            _open = world.Estate.GarageOpen(home.Id) ? 1f : 0f;
            Apply();
        }

        public override bool CanInteract => base.CanInteract && _w.Owns(_home);
        public override string Prompt => _w.Estate.GarageOpen(_home.Id) ? "Close the garage" : "Open the garage";
        public override void Interact() => _w.Estate.SetGarage(_home.Id, !_w.Estate.GarageOpen(_home.Id));
        public bool IsOpen => _open > 0.95f;

        private void Update()
        {
            float want = _w.Estate.GarageOpen(_home.Id) ? 1f : 0f;
            if (Mathf.Approximately(_open, want)) return;
            _open = Mathf.MoveTowards(_open, want, Time.deltaTime / 3f);
            Apply();
        }

        private void Apply()
        {
            // Rolls up: the panel shortens from the bottom into the drum under the roof.
            _door.localScale = new Vector3(1f, Mathf.Max(0.02f, 1f - _open), 1f);
            _door.localPosition = _closedAt + Vector3.up * ((_height - 0.1f) * _open);
        }
    }
}
