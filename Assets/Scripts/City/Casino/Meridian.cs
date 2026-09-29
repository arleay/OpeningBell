using System.Collections.Generic;
using OpeningBell.Casino;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The Meridian casino resort (CASINO_SPEC C1–C10), on the old Silver Tide lot. Ground floor: through the glass
    /// doors a marble lobby round a bronze whale; the cashier's cage and restrooms on the left, slot banks either side,
    /// the pit at the back (blackjack, roulette, baccarat), the poker room back left, the Tide Bar and lounge back right,
    /// and the hotel lobby with its own door, café, front desk and elevators on the right. Upstairs (in the same
    /// shell) the Sixty-Two steakhouse and the VIP salon; above the roof a hotel tower with guest rooms, the penthouse
    /// and a helipad (<see cref="MeridianUpstairs"/>, <see cref="MeridianHotel"/>). Built in its own frame: x across
    /// the front (±<see cref="HalfWidth"/>), z from the front wall inward to <see cref="Depth"/>, y up from the lot.
    /// Split over several files by area (MeridianFloor, MeridianGames, MeridianBar).
    /// </summary>
    public static partial class Meridian
    {
        public const string Name = "The Meridian";
        public static readonly Vector3 Origin = new Vector3(515f, 1.5f, 8f);
        public const float HalfWidth = 37f, Depth = 32f, Wall = 0.4f, Ceiling = 6.2f, Height = 11f;
        /// <summary>The hotel's own street door (x), beside the casino's.</summary>
        public const float HotelDoorX = 31f;

        private static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);

        public static void Build(CityContext c, Transform parent)
        {
            Transform m = Kit.Group(parent, Name, Origin);
            Transform dyn = Kit.Group(c.Dynamic, Name + " (live)", Origin);
            CasinoAudio audio = CasinoAudio.Build(dyn, c.Player);
            CasinoMenu.Build(c, dyn);
            var inside = new Bounds(m.TransformPoint(V(0f, 18f, Depth / 2f)), new Vector3(HalfWidth * 2f, 40f, Depth));
            CasinoLife life = CasinoLife.Build(c, m, dyn, inside);

            Shell(c, m);
            Transform inner = Kit.Group(m, "Interior");
            Floor(c, inner);
            Lobby(c, inner, dyn);
            Cage(c, inner, dyn);
            Restrooms(c, inner);
            Slots(c, inner, dyn, life);
            Pit(c, inner, dyn, life);
            PokerRoom(c, inner, dyn, life);
            Bar(c, inner, dyn, life);
            Signs(c, inner);
            Lights(c, m);
            MeridianUpstairs.Build(c, m, dyn, life, audio);
            MeridianHotel.Build(c, m, inner, dyn, life);
            CityLayers.Set(inner, CityLayers.Interior);
            Parking(c, parent);

            // Sound: the floor's murmur and a bright pad, the lounge's piano.
            audio.Bed("Floor murmur", m.TransformPoint(V(0f, 2f, 16f)), CasinoArt.Murmur(), 0.35f, 45f);
            audio.Bed("Floor music", m.TransformPoint(V(-10f, 4f, 18f)), CasinoArt.Music(false), 0.25f, 35f);
            audio.Bed("Lounge piano", m.TransformPoint(V(19f, 2f, 21f)), CasinoArt.Music(true), 0.4f, 14f);

            c.Place(m.TransformPoint(V(0f, 0f, -6f)), PlaceKind.Door, Name);
            c.Anchor("casino_front", m.TransformPoint(V(0f, 0f, -10f)));
            c.Anchor("casino_lobby", m.TransformPoint(V(0f, 0f, 3f)));
        }

        /// <summary>Street-facing windows of the upstairs restaurant and landing (between the fins).</summary>
        public static readonly float[] UpstairsWindows = { 11f, 15f, 19f, 23f, 31f };

        private static void Shell(CityContext c, Transform m)
        {
            Kit k = c.Kit;
            Material stone = c.P.Surface(Finish.Concrete, new Color(0.3f, 0.28f, 0.27f), 0.3f);
            Material bronze = c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f);
            Material warm = c.P.Glow(new Color(1f, 0.78f, 0.42f), 1.8f);
            float hw = HalfWidth, d = Depth;

            // Walls: the casino entrance (8 m of glass), the hotel door, and the upstairs windows onto the street.
            var front = new List<Opening> { new Opening(0f, 8f, 0f, 3.8f), new Opening(HotelDoorX, 2.6f, 0f, 3f) };
            foreach (float x in UpstairsWindows) front.Add(new Opening(x, 3f, 7.2f, 9.8f));
            k.WallX(m, "Front wall", -hw, hw, 0f, 0f, Height, Wall, stone, front.ToArray());
            foreach (float x in UpstairsWindows)
                k.Pane(m, "Upstairs window", V(x - 1.5f, 7.2f, -0.03f), V(x + 1.5f, 9.8f, 0.03f), new Color(0.35f, 0.3f, 0.2f, 0.45f));
            k.WallX(m, "Back wall", -hw, hw, d, 0f, Height, Wall, stone);
            k.WallZ(m, "Side wall", 0f, d, -hw, 0f, Height, Wall, stone);
            k.WallZ(m, "Side wall", 0f, d, hw, 0f, Height, Wall, stone);
            k.Span(m, "Roof", V(-hw, Height - 0.2f, 0f), V(hw, Height, d), c.P.Lit(new Color(0.16f, 0.16f, 0.17f)), collider: false);
            k.Span(m, "Cornice", V(-hw - 0.25f, Height - 0.7f, -0.45f), V(hw + 0.25f, Height - 0.3f, 0f), bronze, collider: false);
            k.Span(m, "Plinth", V(-hw - 0.1f, 0f, -0.32f), V(-4.6f, 0.5f, 0f), bronze, collider: false);
            k.Span(m, "Plinth", V(4.6f, 0f, -0.32f), V(HotelDoorX - 1.5f, 0.5f, 0f), bronze, collider: false);
            k.Span(m, "Plinth", V(HotelDoorX + 1.5f, 0f, -0.32f), V(hw + 0.1f, 0.5f, 0f), bronze, collider: false);

            // Bronze fins down the front, each with a warm light strip, framing the name.
            for (float x = -hw + 2f; x <= hw - 2f; x += 4f)
            {
                if (Mathf.Abs(x) < 6.5f) continue;
                k.Box(m, "Fin", V(x, Height / 2f - 0.4f, -0.4f), V(0.3f, Height - 1.2f, 0.4f), bronze, collider: false);
                k.Box(m, "Fin light", V(x, Height / 2f - 0.4f, -0.61f), V(0.07f, Height - 1.8f, 0.02f), warm, collider: false);
                if (Mathf.Abs(x + 2f - HotelDoorX) > 2f) k.Box(m, "Uplight", V(x + 2f, 0.08f, -0.45f), V(0.6f, 0.12f, 0.3f), warm, collider: false);
            }

            // The casino entrance: bronze portal under the marquee, glass either side of the sliding doors and above them.
            k.Box(m, "Portal", V(-4.6f, 2.1f, -0.6f), V(0.9f, 4.2f, 1.2f), bronze);
            k.Box(m, "Portal", V(4.6f, 2.1f, -0.6f), V(0.9f, 4.2f, 1.2f), bronze);
            Color glass = new Color(0.55f, 0.6f, 0.62f, 0.35f);
            k.Pane(m, "Entrance glass", V(-4f, 0f, -0.04f), V(-1.6f, 3.8f, 0.04f), glass);
            k.Pane(m, "Entrance glass", V(1.6f, 0f, -0.04f), V(4f, 3.8f, 0.04f), glass);
            k.Pane(m, "Transom", V(-1.6f, 2.98f, -0.04f), V(1.6f, 3.8f, 0.04f), glass, collider: false);
            c.SlidingDoor(m, Name, V(0f, 0f, 0f), 3.2f, 2.8f);

            // The hotel's door: its own canopy and name.
            c.SlidingDoor(m, "Meridian Hotel", V(HotelDoorX, 0f, 0f), 2.4f, 2.7f);
            k.Pane(m, "Hotel transom", V(HotelDoorX - 1.3f, 2.8f, -0.03f), V(HotelDoorX + 1.3f, 3f, 0.03f), glass, collider: false);
            k.Span(m, "Hotel canopy", V(HotelDoorX - 2.2f, 3.3f, -2.4f), V(HotelDoorX + 2.2f, 3.5f, 0f), bronze, collider: false);
            k.Span(m, "Hotel canopy light", V(HotelDoorX - 2.1f, 3.28f, -2.3f), V(HotelDoorX + 2.1f, 3.3f, -0.1f), c.P.Glow(new Color(1f, 0.85f, 0.6f), 1.3f), collider: false);
            TextMesh hotel = k.Text(m, "MERIDIAN HOTEL", V(HotelDoorX, 3.9f, -0.26f), 0f, 0.3f, Color.white);
            hotel.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(1f, 0.9f, 0.7f));

            // The name, big and gold over the marquee; a sub-line; hours.
            TextMesh name = k.Text(m, "THE MERIDIAN", V(0f, 8.1f, -0.25f), 0f, 1.5f, Color.white);
            name.GetComponent<MeshRenderer>().sharedMaterial = c.P.Sign(new Color(1f, 0.82f, 0.45f));
            k.Text(m, "CASINO  ·  HOTEL  ·  STEAKHOUSE", V(0f, 6.55f, -0.25f), 0f, 0.42f, new Color(0.95f, 0.9f, 0.8f));
            k.Text(m, "OPEN 24 HOURS · 21+", V(-9f, 2.2f, -0.05f), 0f, 0.14f, new Color(0.95f, 0.85f, 0.6f));
        }

        /// <summary>Where the valet keeps a car (world): the last west bay.</summary>
        public static readonly Vector3 ValetBay = new Vector3(459.5f, 1.5f, 37.4f);

        private static void Parking(CityContext c, Transform parent)
        {
            // West side of the lot, beside the sign: two rows of bays nose-in to the edges, an aisle between.
            Kit k = c.Kit;
            Transform lot = Kit.Group(parent, "Meridian parking", V(0f, Origin.y, 0f));
            Material paint = c.P.Lit(new Color(0.9f, 0.88f, 0.8f), 0.1f);
            const float westRow = 459.5f, eastRow = 474f, z0 = 11f, z1 = 38.5f;
            for (float z = z0; z <= z1 + 0.01f; z += 2.75f)
            {
                k.Decal(lot, "Bay line", V(westRow, 0.012f, z), new Vector2(5f, 0.1f), 0f, paint);
                k.Decal(lot, "Bay line", V(eastRow, 0.012f, z), new Vector2(5f, 0.1f), 0f, paint);
                if (z + 2.75f > z1 + 0.01f) break;
                // The last west bay is kept for the valet.
                if (z + 2.75f <= z1 - 2.7f) c.ParkingSpots.Add((V(westRow, Origin.y, z + 1.375f), 270f, ParkingKind.Lot));
                c.ParkingSpots.Add((V(eastRow, Origin.y, z + 1.375f), 90f, ParkingKind.Lot));
            }
            k.Text(lot, "GUEST PARKING", V(466.75f, 2.4f, 9.6f), 0f, 0.3f, new Color(0.95f, 0.85f, 0.55f));
            k.Box(lot, "Sign post", V(466.75f, 1.1f, 9.7f), V(0.12f, 2.2f, 0.12f), c.P.Metal(new Color(0.72f, 0.53f, 0.3f), 0.75f), collider: true);
            k.Text(lot, "VALET", V(westRow, 1.2f, 37.2f), 90f, 0.25f, new Color(0.95f, 0.85f, 0.55f));
            c.Anchor("casino_parking", V(466.75f, Origin.y, 16f));
        }
    }
}
