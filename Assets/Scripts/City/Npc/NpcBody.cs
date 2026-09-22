using UnityEngine;

namespace OpeningBell.City
{
    public enum NpcPose
    {
        Stand,
        Walk,
        Sit,
        Typing,
        Phone,
        Drink,
        /// <summary>Seated on a bike; <c>time</c> is the crank angle in radians.</summary>
        Cycle,
        /// <summary>Standing on a board, knees soft.</summary>
        Skate,
        /// <summary>Kicking off the ground on a board.</summary>
        Push,
    }

    /// <summary>
    /// TODO(art): primitive stand-in person (boxes, sphere head) with procedural limb swings until character art
    /// exists. Root at the feet, facing +z.
    /// </summary>
    public sealed class NpcBody : MonoBehaviour
    {
        private Transform _body, _legL, _legR, _armL, _armR, _cup;
        private float _phaseOffset;

        private static readonly Color[] Skin =
        {
            new Color(0.96f, 0.8f, 0.69f), new Color(0.87f, 0.67f, 0.52f), new Color(0.72f, 0.52f, 0.38f),
            new Color(0.55f, 0.38f, 0.26f), new Color(0.38f, 0.26f, 0.18f),
        };

        private static readonly Color[] Clothes =
        {
            new Color(0.2f, 0.28f, 0.45f), new Color(0.55f, 0.18f, 0.16f), new Color(0.85f, 0.85f, 0.82f),
            new Color(0.25f, 0.4f, 0.3f), new Color(0.6f, 0.5f, 0.3f), new Color(0.15f, 0.15f, 0.17f),
            new Color(0.45f, 0.45f, 0.5f), new Color(0.7f, 0.55f, 0.6f),
        };

        private static readonly Color[] Hair =
        {
            new Color(0.1f, 0.08f, 0.06f), new Color(0.3f, 0.2f, 0.12f), new Color(0.6f, 0.45f, 0.25f), new Color(0.55f, 0.55f, 0.55f),
        };

        public static NpcBody Create(Kit kit, Transform parent, string name, int seed, Color? outfit = null)
        {
            var rng = new System.Random(seed);
            Color skin = Skin[rng.Next(Skin.Length)];
            Color shirt = outfit ?? Clothes[rng.Next(Clothes.Length)];
            Color pants = Color.Lerp(Clothes[rng.Next(Clothes.Length)], new Color(0.12f, 0.12f, 0.14f), 0.6f);
            Color hair = Hair[rng.Next(Hair.Length)];
            float height = 0.93f + 0.14f * (float)rng.NextDouble();

            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var npc = root.AddComponent<NpcBody>();
            npc._phaseOffset = (float)rng.NextDouble() * 10f;
            npc._body = Kit.Group(root.transform, "Body");
            npc._body.localScale = Vector3.one * height;
            Transform b = npc._body;

            npc._legL = Limb(kit, b, "LegL", new Vector3(-0.1f, 0.9f, 0f), new Vector3(0.15f, 0.9f, 0.18f), kit.P.Lit(pants));
            npc._legR = Limb(kit, b, "LegR", new Vector3(0.1f, 0.9f, 0f), new Vector3(0.15f, 0.9f, 0.18f), kit.P.Lit(pants));
            kit.Box(b, "Torso", new Vector3(0f, 1.21f, 0f), new Vector3(0.44f, 0.64f, 0.26f), kit.P.Lit(shirt), collider: false);
            npc._armL = Limb(kit, b, "ArmL", new Vector3(-0.28f, 1.49f, 0f), new Vector3(0.11f, 0.62f, 0.12f), kit.P.Lit(shirt));
            npc._armR = Limb(kit, b, "ArmR", new Vector3(0.28f, 1.49f, 0f), new Vector3(0.11f, 0.62f, 0.12f), kit.P.Lit(shirt));
            kit.Sphere(b, "Head", new Vector3(0f, 1.69f, 0f), 0.26f, kit.P.Lit(skin));
            kit.Box(b, "Hair", new Vector3(0f, 1.78f, -0.02f), new Vector3(0.25f, 0.1f, 0.25f), kit.P.Lit(hair), collider: false);
            npc._cup = kit.Cylinder(npc._armR, "Cup", new Vector3(0f, -0.62f, 0.06f), 0.08f, 0.12f, kit.P.Lit(new Color(0.9f, 0.88f, 0.84f))).transform;
            npc._cup.gameObject.SetActive(false);
            return npc;
        }

        private static Transform Limb(Kit kit, Transform parent, string name, Vector3 pivot, Vector3 size, Material m)
        {
            Transform joint = Kit.Group(parent, name, pivot);
            kit.Box(joint, name + "Mesh", new Vector3(0f, -size.y / 2f, 0f), size, m, collider: false);
            return joint;
        }

        /// <summary>Poses the limbs. <paramref name="time"/> drives walk cycles and fidgets (any running clock).</summary>
        public void Animate(NpcPose pose, float time, float stride = 1f)
        {
            float t = time + _phaseOffset;
            float legL = 0f, legR = 0f, armL = 0f, armR = 0f, drop = 0f;
            bool cup = false;
            switch (pose)
            {
                case NpcPose.Walk:
                    float swing = Mathf.Sin(t * 7.5f * stride) * 26f;
                    legL = swing;
                    legR = -swing;
                    armL = -swing * 0.7f;
                    armR = swing * 0.7f;
                    break;
                case NpcPose.Sit:
                    legL = legR = -85f;
                    armL = armR = -25f;
                    drop = 0.43f;
                    break;
                case NpcPose.Typing:
                    armL = -68f + Mathf.Sin(t * 13f) * 3f;
                    armR = -68f + Mathf.Sin(t * 11f + 1f) * 3f;
                    break;
                case NpcPose.Phone:
                    armR = -150f;
                    armL = Mathf.Sin(t * 0.8f) * 4f;
                    break;
                case NpcPose.Drink:
                    cup = true;
                    armR = Mathf.Repeat(t, 6f) < 1.5f ? -130f : -60f;
                    break;
                case NpcPose.Cycle:
                    // Thighs forward, pedalling with the crank; arms out to the bars.
                    legL = -70f + 28f * Mathf.Sin(time);
                    legR = -70f + 28f * Mathf.Sin(time + Mathf.PI);
                    armL = armR = -62f;
                    drop = 0f;
                    break;
                case NpcPose.Skate:
                    legL = -8f;
                    legR = 8f;
                    armL = -20f + Mathf.Sin(t * 1.1f) * 4f;
                    armR = 15f;
                    break;
                case NpcPose.Push:
                    legL = -8f;
                    legR = 45f * Mathf.Sin(t * 7f);
                    armL = -30f;
                    armR = 30f;
                    break;
                default:
                    armL = Mathf.Sin(t * 1.3f) * 2f;
                    armR = -armL;
                    break;
            }
            _legL.localRotation = Quaternion.Euler(legL, 0f, 0f);
            _legR.localRotation = Quaternion.Euler(legR, 0f, 0f);
            _armL.localRotation = Quaternion.Euler(armL, 0f, pose == NpcPose.Walk ? 0f : 4f);
            _armR.localRotation = Quaternion.Euler(armR, 0f, pose == NpcPose.Walk ? 0f : -4f);
            _body.localPosition = new Vector3(0f, -drop * _body.localScale.y, 0f);
            if (_cup.gameObject.activeSelf != cup) _cup.gameObject.SetActive(cup);
        }
    }
}
