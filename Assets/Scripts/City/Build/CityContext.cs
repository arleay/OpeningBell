using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>Shared state while the city is generated: geometry kit, roots, game references and named spots.</summary>
    public sealed class CityContext
    {
        public const string OfficeLeaseId = "office_suite_204";

        public Kit Kit;
        public Palette P => Kit.P;
        /// <summary>Never moves: statically batched after generation.</summary>
        public Transform Static;
        /// <summary>Doors, signals, NPCs: anything that moves or changes material.</summary>
        public Transform Dynamic;
        public GameBootstrap Game;
        public InteractionHud Hud;
        public Transform Player;
        public WorkstationController Workstation;
        public RoadNetwork Roads;

        /// <summary>Named world positions (tests, spawn points, directions).</summary>
        public readonly Dictionary<string, Vector3> Anchors = new Dictionary<string, Vector3>();
        /// <summary>Doors and benches pedestrians can use.</summary>
        public readonly List<(Vector2 P, PlaceKind Kind, string Tag)> Places = new List<(Vector2, PlaceKind, string)>();
        /// <summary>Building pads levelled into the terrain (added by builders before the streets are built).</summary>
        public readonly List<Pad> Pads = new List<Pad>();
        /// <summary>Walkways off the sidewalks (park paths, towpath, pier): points as (x, height, z).</summary>
        public readonly List<Vector3[]> WalkPaths = new List<Vector3[]>();
        public Terrain Terrain;

        /// <summary>Ground height at (x, z): the terrain where there is one, else the plan's land.</summary>
        public float GroundY(float x, float z) =>
            Terrain != null ? Terrain.SampleHeight(new Vector3(x, 0f, z)) + Terrain.transform.position.y : TownTerrain.Height(x, z);

        public bool IsTenant => Game.Economy.Owns(OfficeLeaseId);

        /// <summary>0 day … 1 night (from the daylight cycle, updated every frame).</summary>
        public float Night;
        /// <summary>Real lights that only burn at night (street lamps).</summary>
        public readonly List<Light> NightLights = new List<Light>();

        public void Anchor(string name, Vector3 p) => Anchors[name] = p;

        public void Place(Vector3 p, PlaceKind kind, string tag) => Places.Add((new Vector2(p.x, p.z), kind, tag));

        public Light PointLight(Transform parent, Vector3 position, float range, float intensity, Color color)
        {
            var go = new GameObject("Light");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = position;
            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.range = range;
            // Callers give a "feel" intensity; URP attenuates with distance², so scale for ~3 m ceilings.
            light.intensity = intensity * 4.5f;
            light.color = color;
            light.shadows = LightShadows.None;
            return light;
        }

        /// <summary>Hinged door in an opening: hinge at (x0, y, z), leaf spans +x by <paramref name="width"/>.</summary>
        public Door SwingDoor(Transform parent, string label, Vector3 hinge, float width, float height, Material leaf, float yaw = 0f, bool glass = false)
        {
            // Frame carries the orientation; the hinge child rotates from identity.
            Transform frame = Kit.Group(Dynamic, "Door " + label, parent.TransformPoint(hinge), parent.eulerAngles.y + yaw);
            Transform pivot = Kit.Group(frame, "Hinge");
            GameObject slab = Kit.Box(pivot, "Leaf", new Vector3(width / 2f, height / 2f, 0f), new Vector3(width - 0.04f, height, 0.05f), leaf);
            if (glass) slab.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Material metal = P.Lit(new Color(0.75f, 0.75f, 0.72f), 0.7f);
            Kit.Box(pivot, "Handle", new Vector3(width - 0.12f, 1.02f, 0f), new Vector3(0.04f, 0.04f, 0.16f), metal, collider: false);
            var door = frame.gameObject.AddComponent<Door>();
            door.Configure(DoorKind.Swing, pivot, null, label, Player);
            return door;
        }

        /// <summary>Automatic sliding glass doors centred at <paramref name="center"/> (in parent space), sliding along x.</summary>
        public Door SlidingDoor(Transform parent, string label, Vector3 center, float width, float height, float yaw = 0f)
        {
            Transform frame = Kit.Group(Dynamic, "Doors " + label, parent.TransformPoint(center), parent.eulerAngles.y + yaw);
            Material glass = P.Glass(new Color(0.7f, 0.82f, 0.88f, 0.3f));
            GameObject left = Kit.Box(frame, "Left", new Vector3(-width / 4f, height / 2f, 0f), new Vector3(width / 2f, height, 0.04f), glass);
            GameObject right = Kit.Box(frame, "Right", new Vector3(width / 4f, height / 2f, 0f), new Vector3(width / 2f, height, 0.04f), glass);
            left.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            right.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            Kit.Box(frame, "Header", new Vector3(0f, height + 0.08f, 0f), new Vector3(width + 0.2f, 0.16f, 0.12f),
                P.Lit(new Color(0.3f, 0.31f, 0.33f), 0.6f), collider: false);
            var door = frame.gameObject.AddComponent<Door>();
            door.Configure(DoorKind.AutoSlide, left.transform, right.transform, label, Player);
            return door;
        }
    }
}
