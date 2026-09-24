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
                k.Box(pump, "Body", new Vector3(0f, 0.85f, 0f), new Vector3(0.7f, 1.7f, 0.45f), white);
                k.Box(pump, "Band", new Vector3(0f, 1.45f, 0f), new Vector3(0.72f, 0.35f, 0.47f), brand, collider: false);
                k.Box(pump, "Screen", new Vector3(0.36f, 1.1f, 0f), new Vector3(0.02f, 0.25f, 0.3f), c.P.Unlit(new Color(0.2f, 0.6f, 0.4f)), collider: false);
                k.Box(pump, "Screen", new Vector3(-0.36f, 1.1f, 0f), new Vector3(0.02f, 0.25f, 0.3f), c.P.Unlit(new Color(0.2f, 0.6f, 0.4f)), collider: false);
                pump.gameObject.AddComponent<FuelPump>().Configure(c.Game, c.Hud);
            }

            // The jerry can rack at the end of the west island.
            Transform rack = Kit.Group(dyn, "Jerry can rack", new Vector3(114f, 0.15f, 32.2f));
            Material red = c.P.Lit(new Color(0.75f, 0.1f, 0.08f), 0.35f);
            k.Box(rack, "Shelf", new Vector3(0f, 0.45f, 0f), new Vector3(0.9f, 0.9f, 0.4f), c.P.Lit(new Color(0.3f, 0.3f, 0.32f), 0.3f))
                .AddComponent<JerryCanRack>().Configure(c.Game);
            for (int i = 0; i < 3; i++)
                k.Box(rack, "Jerry can", new Vector3(-0.28f + i * 0.28f, 1.05f, 0f), new Vector3(0.12f, 0.3f, 0.26f), red, collider: false);

            c.Anchor("fuel_pump_west", new Vector3(114f, 0f, 28f));
            c.Anchor("fuel_bay_west", new Vector3(116.4f, 0f, 28f));
            c.Anchor("fuel_driveway", new Vector3(128f, 0f, 29f));
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
