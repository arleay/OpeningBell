using System.Globalization;
using OpeningBell.Gameplay;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Tidewater Fuel (spec §27, §64): a forecourt behind the Maple shops with a driveway off Exchange St, a
    /// canopy and two pumps. Pull up, get out, take the nozzle and fill the tank; jerry cans on a rack by the pumps;
    /// the Corner Mart next door is the shop. Open all day and night (card pumps).
    /// </summary>
    public static class FuelStation
    {
        public const decimal PricePerLiter = 1.65m;

        public static void Build(CityContext c)
        {
            Kit k = c.Kit;
            Transform root = Kit.Group(c.Static, "Tidewater Fuel");
            Transform dyn = Kit.Group(c.Dynamic, "Tidewater Fuel");
            Material slab = c.P.Lit(new Color(0.36f, 0.36f, 0.37f), 0.1f);
            Material white = c.P.Lit(new Color(0.92f, 0.92f, 0.9f), 0.2f);
            Material brand = c.P.Lit(new Color(0.1f, 0.45f, 0.55f), 0.3f);

            // Forecourt and driveway (a ramp up the kerb from Exchange St, like the crosswalk ramps but wide).
            k.Span(root, "Forecourt", new Vector3(106f, -0.05f, 17f), new Vector3(126.5f, 0.005f, 41f), slab).AddComponent<SurfaceTag>().Roughness = 0.3f;
            k.Span(root, "Driveway", new Vector3(126.5f, -0.05f, 25f), new Vector3(130f, 0.005f, 33f), slab).AddComponent<SurfaceTag>().Roughness = 0.3f;
            const float run = 1.4f, rise = -CityPlan.RoadY;
            GameObject ramp = k.Box(root, "Driveway ramp", new Vector3(130f + run / 2f, CityPlan.RoadY + rise / 2f - 0.03f, 29f),
                new Vector3(8f, 0.06f, Mathf.Sqrt(run * run + rise * rise)), slab);
            ramp.transform.localRotation = Quaternion.Euler(-Mathf.Atan2(rise, run) * Mathf.Rad2Deg, -90f, 0f);
            ramp.AddComponent<SurfaceTag>().Roughness = 0.3f;

            // Canopy with lights underneath.
            foreach (float x in new[] { 110.5f, 123.5f })
            foreach (float z in new[] { 22f, 34f })
                k.Box(root, "Column", new Vector3(x, 2.2f, z), new Vector3(0.45f, 4.4f, 0.45f), white);
            k.Span(root, "Canopy", new Vector3(109.5f, 4.4f, 21f), new Vector3(124.5f, 4.85f, 35f), white, collider: false);
            k.Span(root, "Fascia", new Vector3(109.4f, 4.35f, 20.9f), new Vector3(124.6f, 4.9f, 21f), brand, collider: false);
            k.Text(root, "TIDEWATER FUEL", new Vector3(117f, 4.62f, 20.85f), 0f, 0.3f, Color.white);
            foreach (float x in new[] { 113f, 121f })
            {
                k.Box(root, "Canopy light", new Vector3(x, 4.38f, 28f), new Vector3(1.6f, 0.03f, 4f), c.P.Lamp(new Color(0.75f, 0.75f, 0.72f), new Color(1f, 0.98f, 0.95f), 2f), collider: false);
                var lamp = new GameObject("Canopy spot");
                lamp.transform.SetParent(root, false);
                lamp.transform.localPosition = new Vector3(x, 4.3f, 28f);
                lamp.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
                var light = lamp.AddComponent<Light>();
                light.type = LightType.Spot;
                light.range = 12f;
                light.spotAngle = 120f;
                light.intensity = 26f;
                light.color = new Color(0.95f, 0.97f, 1f);
                light.enabled = false;
                c.NightLights.Add(light);
            }
            k.Box(root, "Price sign post", new Vector3(128.4f, 1.6f, 36f), new Vector3(0.2f, 3.2f, 0.2f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f)));
            k.Box(root, "Price sign", new Vector3(128.4f, 3.4f, 36f), new Vector3(0.15f, 1.3f, 2.2f), brand, collider: false);
            string price = "REGULAR\n$" + PricePerLiter.ToString("0.00", CultureInfo.InvariantCulture) + "/L";
            k.Text(root, price, new Vector3(128.5f, 3.4f, 36f), -90f, 0.2f, Color.white);

            // Islands and pumps.
            foreach (float x in new[] { 114f, 120f })
            {
                k.Span(root, "Island", new Vector3(x - 0.6f, 0f, 25f), new Vector3(x + 0.6f, 0.15f, 31f), c.P.Lit(new Color(0.62f, 0.6f, 0.56f)));
                Transform pump = Kit.Group(dyn, "Pump", new Vector3(x, 0.15f, 28f));
                Dispenser(c, pump, white, brand);
                pump.gameObject.AddComponent<FuelPump>().Configure(c.Game, c.Hud);
            }

            // The jerry can rack at the end of the west island.
            Transform rack = Kit.Group(dyn, "Jerry can rack", new Vector3(114f, 0.15f, 32.2f));
            k.Box(rack, "Shelf", new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.4f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.3f))
                .AddComponent<JerryCanRack>().Configure(c.Game);
            for (int i = 0; i < 2; i++)
                if (k.Real(rack, i == 0 ? "metal_jerrycan" : "plastic_jerrycan", new Vector3(-0.2f + i * 0.4f, 0.9f, 0f), 90f) == null)
                    k.Box(rack, "Jerry can", new Vector3(-0.28f + i * 0.28f, 1.05f, 0f), new Vector3(0.12f, 0.3f, 0.26f), c.P.Lit(new Color(0.75f, 0.1f, 0.08f), 0.35f), collider: false);

            c.Anchor("fuel_pump_west", new Vector3(114f, 0f, 28f));
            c.Anchor("fuel_bay_west", new Vector3(116.4f, 0f, 28f));
            c.Anchor("fuel_driveway", new Vector3(128f, 0f, 29f));
        }

        /// <summary>
        /// A modern fuel dispenser, both faces (±x) alike: a plinth, the steel cabinet with a lit brand header, on each
        /// face a price/litre display with its digits, a card reader and keypad, and a nozzle in its holster on a hose
        /// that loops down to the ground and back up. The cabinet is the solid (and what you aim at).
        /// </summary>
        private static void Dispenser(CityContext c, Transform pump, Material white, Material brand)
        {
            Kit k = c.Kit;
            Material steel = c.P.Metal(new Color(0.75f, 0.76f, 0.78f), 0.6f);
            Material black = c.P.Lit(new Color(0.05f, 0.05f, 0.06f), 0.5f);
            Material rubber = c.P.Lit(new Color(0.03f, 0.03f, 0.03f), 0.25f);
            k.Box(pump, "Plinth", new Vector3(0f, 0.08f, 0f), new Vector3(0.8f, 0.16f, 0.55f), steel, collider: false);
            k.Box(pump, "Body", new Vector3(0f, 0.95f, 0f), new Vector3(0.66f, 1.6f, 0.45f), white);
            k.Box(pump, "Header", new Vector3(0f, 1.95f, 0f), new Vector3(0.72f, 0.36f, 0.5f), brand, collider: false);
            k.Box(pump, "Header glow", new Vector3(0f, 1.95f, 0f), new Vector3(0.74f, 0.08f, 0.52f), c.P.Glow(new Color(1f, 1f, 1f), 1.2f), collider: false);
            k.Box(pump, "Kick plate", new Vector3(0f, 0.3f, 0f), new Vector3(0.68f, 0.24f, 0.47f), steel, collider: false);
            string price = FuelStation.PricePerLiter.ToString("0.00", CultureInfo.InvariantCulture);
            foreach (float side in new[] { -1f, 1f })
            {
                float face = side * 0.335f;
                float yaw = side > 0f ? -90f : 90f; // text faces out of each side (TextMesh reads toward its -z)
                // Display: dark glass with lit readouts.
                k.Box(pump, "Display", new Vector3(face, 1.45f, 0f), new Vector3(0.012f, 0.34f, 0.36f), black, collider: false);
                k.Text(pump, "$  0.00", new Vector3(face + side * 0.008f, 1.54f, 0f), yaw, 0.05f, new Color(1f, 0.55f, 0.2f));
                k.Text(pump, "L  0.000", new Vector3(face + side * 0.008f, 1.46f, 0f), yaw, 0.04f, new Color(1f, 0.55f, 0.2f));
                k.Text(pump, "$" + price + "/L", new Vector3(face + side * 0.008f, 1.37f, 0f), yaw, 0.035f, new Color(0.4f, 1f, 0.5f));
                // Card reader and keypad.
                k.Box(pump, "Card reader", new Vector3(face, 1.15f, 0.1f), new Vector3(0.03f, 0.14f, 0.12f), black, collider: false);
                for (int i = 0; i < 12; i++)
                    k.Box(pump, "Key", new Vector3(face + side * 0.016f, 1.12f - (i / 3) * 0.028f, -0.06f + (i % 3) * 0.028f), new Vector3(0.006f, 0.02f, 0.02f), steel, collider: false);
                // Holster, nozzle, and the hose looping to the ground.
                Vector3 holster = new Vector3(face + side * 0.03f, 0.95f, -0.12f);
                k.Box(pump, "Holster", holster, new Vector3(0.06f, 0.12f, 0.08f), black, collider: false);
                var f = new Forge(2);
                f.Use(1).Block(holster + new Vector3(side * 0.05f, 0.03f, 0f), Vector3.forward, Vector3.up, new Vector3(0.05f, 0.07f, 0.14f));
                f.Use(1).Sweep(new System.Collections.Generic.List<Vector3>
                {
                    holster + new Vector3(side * 0.06f, 0.08f, 0.05f), holster + new Vector3(side * 0.07f, 0.13f, 0.14f),
                }, 0.01f, 0.01f, false, 6);
                var hose = new System.Collections.Generic.List<Vector3>();
                Vector3 from = new Vector3(face + side * 0.02f, 1.7f, 0.18f), to = holster + new Vector3(side * 0.06f, 0f, -0.05f);
                for (int i = 0; i <= 16; i++)
                {
                    float t = i / 16f;
                    Vector3 p0 = Vector3.Lerp(from, to, t);
                    p0.y = Mathf.Lerp(from.y, to.y, t) - Mathf.Sin(t * Mathf.PI) * 1.3f;
                    p0.y = Mathf.Max(0.2f, p0.y);
                    p0 += Vector3.right * (side * Mathf.Sin(t * Mathf.PI) * 0.12f);
                    hose.Add(p0);
                }
                f.Use(0).Sweep(hose, 0.016f, 0.016f, false, 8);
                f.Build(pump, "Nozzle and hose", rubber, c.P.Lit(new Color(0.1f, 0.35f, 0.15f), 0.5f));
            }
        }
    }

    /// <summary>
    /// A pump: [E] takes the nozzle (walk it to your car and hold the left button to fill; [E] here again hangs it up
    /// and pays); with the jerry can in your hands, [E] fills the can instead.
    /// </summary>
    public sealed class FuelPump : Interactable
    {
        private GameBootstrap _game;
        private InteractionHud _hud;
        private FuelHands _hands;

        public void Configure(GameBootstrap game, InteractionHud hud)
        {
            _game = game;
            _hud = hud;
        }

        private FuelHands Hands => _hands != null ? _hands : _hands = FindAnyObjectByType<FuelHands>();

        private static string Money(decimal m) => "$" + m.ToString("N2", CultureInfo.InvariantCulture);

        public override bool CanInteract => base.CanInteract && Hands != null &&
            (Hands.Held == FuelHands.Tool.Can || (Hands.Held == FuelHands.Tool.Nozzle && Hands.Pump == this) || Hands.CanTake);

        public override string Prompt
        {
            get
            {
                FuelHands h = Hands;
                if (h == null) return "Fuel pump";
                if (h.Held == FuelHands.Tool.Nozzle && h.Pump == this)
                    return h.Pumped > 0.05 ? $"Hang up and pay · {h.Pumped.ToString("0.0", CultureInfo.InvariantCulture)} L · {Money(h.PumpedCost)}" : "Hang up the nozzle";
                if (h.Held == FuelHands.Tool.Can)
                {
                    double liters = FuelHands.CanCapacity - _game.Vehicles.JerryCan;
                    return liters < 0.05 ? "Jerry can: full" : $"Fill the jerry can · {liters.ToString("0.0", CultureInfo.InvariantCulture)} L · {Money(Trading.Money.RoundCents((decimal)liters * FuelStation.PricePerLiter))}";
                }
                return "Take the nozzle";
            }
        }

        public override string Details => $"Regular ${FuelStation.PricePerLiter.ToString("0.00", CultureInfo.InvariantCulture)}/L · card only · pay when you hang up";

        public override void Interact()
        {
            FuelHands h = Hands;
            if (h == null) return;
            if (h.Held == FuelHands.Tool.Nozzle && h.Pump == this) h.HangUp();
            else if (h.Held == FuelHands.Tool.Can)
            {
                string result = h.FillCan();
                if (result != null) _hud.ShowToast(result);
            }
            else h.TakeNozzle(this);
        }
    }

    /// <summary>A rack of red jerry cans by the pumps: $24.99 for one (it comes empty).</summary>
    public sealed class JerryCanRack : Interactable
    {
        private GameBootstrap _game;
        private FuelHands _hands;

        public void Configure(GameBootstrap game) => _game = game;

        private FuelHands Hands => _hands != null ? _hands : _hands = FindAnyObjectByType<FuelHands>();

        public override bool CanInteract => base.CanInteract && Hands != null && _game.Vehicles.JerryCan < 0 && Hands.CanTake;
        public override string Prompt => _game.Vehicles.JerryCan < 0
            ? $"Buy a jerry can · ${FuelHands.CanPrice.ToString("0.00", CultureInfo.InvariantCulture)}"
            : "Jerry cans ([J] takes yours out)";
        public override string Details => "10 litres, comes empty: fill it at a pump. For when you run dry out of town.";
        public override void Interact() => Hands?.BuyCan();
    }
}
