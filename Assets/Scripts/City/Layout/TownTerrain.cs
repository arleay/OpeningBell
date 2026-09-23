using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The land the town sits on (TOWN_SPEC): a flat basin for the old core (exactly 0 m, so the existing buildings
    /// stand where they always did), a residential slope rising north to the forest, the Crest hill across the
    /// canal, wooded hills west and east, the bay to the south, the canal and the river. Pure functions of (x, z):
    /// the terrain, the roads' grades and the map all read the same numbers.
    /// </summary>
    public static class TownTerrain
    {
        /// <summary>Terrain covers this (x, z) area; the town's playable land is well inside it.</summary>
        public static readonly Rect Extent = Rect.MinMaxRect(-820f, -440f, 780f, 560f);
        public const float Base = -20f;   // terrain object's y
        public const float Span = 80f;    // heights from Base to Base + Span

        public const float SeaLevel = -2.5f;
        /// <summary>The canal is held low (a tide gate at the bay, a weir at the river) so its towpath passes under the bridges.</summary>
        public const float CanalWater = -4.6f;
        public const float CanalFloor = -6.3f;
        public const float RiverWater = -3f;

        /// <summary>The canal: a walled channel from the bay north to the river.</summary>
        public static readonly Rect Canal = Rect.MinMaxRect(300f, -320f, 330f, 452f);
        /// <summary>Canal walls stop here; north of it the banks are natural slopes.</summary>
        public const float CanalWallsNorth = 320f;

        /// <summary>The bay's shoreline (z) at x: a gentle wobble, pulling back east of the canal.</summary>
        public static float Shore(float x) => -302f + 7f * Mathf.Sin(x * 0.013f) + 115f * Smooth((x - 320f) / 90f);

        /// <summary>The river's centre line (z) at x, along the north edge of the town.</summary>
        public static float River(float x) => 455f + 14f * Mathf.Sin(x * 0.006f + 1.3f);
        public const float RiverHalfWidth = 16f;

        private static float Smooth(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static float Gauss(float dx, float dz, float sx, float sz) => Mathf.Exp(-0.5f * (dx * dx / (sx * sx) + dz * dz / (sz * sz)));

        /// <summary>How "wild" the ground is: 0 in the developed town (no bumps), 1 out in the forest.</summary>
        public static float Wildness(float x, float z)
        {
            float west = Smooth((-x - 520f) / 60f);
            float east = Smooth((x - 590f) / 50f);
            float north = Smooth((z - 330f) / 40f);
            return Mathf.Max(west, Mathf.Max(east, north));
        }

        /// <summary>
        /// Natural ground height before roads, lots and water are cut in. 0 across the core
        /// (x -470..300, z -300..110) exactly.
        /// </summary>
        public static float Natural(float x, float z)
        {
            float h = 0f;
            // Residential slope north of Grove, up to the forest; faded out across the canal.
            h += 16f * Smooth((z - 110f) / 250f) * (1f - Smooth((x - 300f) / 40f));
            // East bank sits a little higher than the west (the canal is cut between them).
            h += 1.5f * Smooth((x - 330f) / 12f) * (1f - Smooth((x - 590f) / 50f));
            // Crest hill, north-east, only north of the entertainment district.
            h += 30f * Gauss(x - 650f, z - 380f, 130f, 130f) * Smooth((z - 120f) / 60f);
            // Wooded hills: west (the highway and rail tunnels) and east (the rail tunnel).
            h += 34f * Smooth((-x - 560f) / 170f);
            h += 26f * Smooth((x - 600f) / 150f) * Smooth((z + 200f) / 80f);
            // North forest keeps climbing gently to the river bluff.
            h += 6f * Smooth((z - 330f) / 80f);
            // Rolling forest floor.
            float wild = Wildness(x, z);
            if (wild > 0f) h += wild * (Mathf.PerlinNoise(x * 0.012f + 31.7f, z * 0.012f + 7.3f) - 0.45f) * 9f;
            return h;
        }

        /// <summary>Ground height including the bay, the river and the canal.</summary>
        public static float Height(float x, float z)
        {
            float h = Natural(x, z);
            // Bay: beach down to the waterline, then the seabed.
            float shore = Shore(x);
            if (z < shore + 6f) h = Mathf.Lerp(h, -9f, Smooth((shore + 6f - z) / 40f));
            // River: banks slope down to the water, deep in the middle.
            float d = Mathf.Abs(z - River(x));
            if (d < RiverHalfWidth + 22f) h = Mathf.Lerp(-5.5f, h, Smooth((d - RiverHalfWidth + 4f) / 26f));
            // Canal: walled in town (a sharp cut), sloped banks north of the walls.
            if (InCanal(x, z)) h = CanalFloor;
            else if (z > CanalWallsNorth && z < Canal.yMax + 10f)
            {
                float edge = x < Canal.xMin ? Canal.xMin - x : x - Canal.xMax;
                if (edge > 0f && edge < 14f) h = Mathf.Lerp(CanalFloor, h, Smooth(edge / 14f));
            }
            return h;
        }

        public static bool InCanal(float x, float z) => x > Canal.xMin && x < Canal.xMax && z > Canal.yMin && z < Canal.yMax;

        /// <summary>Deep enough water to swim in (the player can't): bay, river, canal.</summary>
        public static bool IsWater(float x, float z, out float level)
        {
            if (InCanal(x, z)) { level = CanalWater; return true; }
            if (Mathf.Abs(z - River(x)) < RiverHalfWidth + 2f) { level = RiverWater; return true; }
            level = SeaLevel;
            return Height(x, z) < SeaLevel - 0.3f;
        }
    }
}
