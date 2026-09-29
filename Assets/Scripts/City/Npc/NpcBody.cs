using System.Collections.Generic;
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
        /// <summary>Jogging or running (fights, fleeing); <c>stride</c> as for Walk.</summary>
        Run,
        /// <summary>Seated and talking (a call at the desk, a word with the next seat).</summary>
        SitTalk,
    }

    /// <summary>
    /// A person's body: an animated Quaternius character from <see cref="CityArt"/> (humanoid, shared animator,
    /// poses cross-fade), or a primitive stand-in (boxes, sphere head, procedural limb swings) when there's no
    /// character art. Root at the feet, facing +z; the visual is always child 0.
    /// </summary>
    public sealed class NpcBody : MonoBehaviour
    {
        // Character art
        private Animator _animator;
        private float _walkClipSpeed = 1.3f;
        private int _state;
        private Transform _heldCup;

        // Primitive stand-in
        private Transform _body, _legL, _legR, _armL, _armR, _cup;
        private float _phaseOffset;

        private static readonly int SpeedParam = Animator.StringToHash("Speed");
        private static readonly Dictionary<string, int> StateHashes = new Dictionary<string, int>();

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

        /// <summary>True when this body is an animated character rather than primitives.</summary>
        public bool IsCharacter => _animator != null;

        /// <summary>Hip height when seated (bike saddle, bench), from the root.</summary>
        public float SeatHeight => IsCharacter ? 0.5f * transform.GetChild(0).localScale.y : 0.92f;

        /// <param name="look">Preferred outfit (a character file name such as "Suit"); random when null.</param>
        /// <param name="style">Skin and hair (creator palette indices) instead of random ones; <paramref name="outfit"/> then tints the outfit.</param>
        /// <param name="height">Standing height in metres (0: random 1.55–1.8).</param>
        public static NpcBody Create(Kit kit, Transform parent, string name, int seed, Color? outfit = null, string look = null,
            OpeningBell.PlayerLook style = null, float height = 0f)
        {
            var rng = new System.Random(seed);
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var npc = root.AddComponent<NpcBody>();
            npc._phaseOffset = (float)rng.NextDouble() * 10f;
            if (kit.Art != null && kit.Art.HasPeople) npc.BuildCharacter(kit.Art, kit, rng, look, style, style != null ? outfit : null, height);
            else npc.BuildPrimitive(kit, rng, outfit);
            return npc;
        }

        private void BuildCharacter(CityArt art, Kit kit, System.Random rng, string look, OpeningBell.PlayerLook style, Color? top, float height)
        {
            IReadOnlyList<GameObject> people = art.People;
            var matches = new List<GameObject>();
            var town = new List<GameObject>();
            for (int i = 0; i < people.Count; i++)
                if (art.IsTiny(i)) town.Add(people[i]);
            if (town.Count == 0) town.AddRange(people);
            // Staff ask for an outfit by its realistic-set name ("Suit"); the Tiny set has "Suit_Male", "Suit_Female".
            string want = look == "Formal" ? "OldClassy" : look;
            foreach (GameObject p in town)
                if (want == null || p.name == want || p.name.StartsWith(want + "_")) matches.Add(p);
            if (matches.Count == 0) matches.AddRange(town);
            GameObject model = Instantiate(matches[rng.Next(matches.Count)], transform, false);
            model.name = "Body";
            _animator = model.GetComponent<Animator>();
            // A skin tone and hair colour of their own (the creator's palette), so a crowd isn't one person repeated.
            CharacterStyle.Apply(model, style ?? new OpeningBell.PlayerLook { Skin = 1 + rng.Next(CharacterStyle.Skins.Length - 1), Hair = rng.Next(3) == 0 ? 0 : 1 + rng.Next(6) }, top);
            // Sized by the head rather than a fixed factor (the costume set varies: a pug, a wizard): 1.55–1.8 m tall.
            Transform headBone = _animator.GetBoneTransform(HumanBodyBones.Head);
            float headY = headBone != null ? model.transform.InverseTransformPoint(headBone.position).y : 1.62f;
            float tall = 1.55f + 0.25f * (float)rng.NextDouble();
            model.transform.localScale = Vector3.one * CharacterStyle.Scale(height > 0f ? height : tall, headY);
            _animator.runtimeAnimatorController = art.PeopleAnimator;
            _animator.applyRootMotion = false;
            _animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            _walkClipSpeed = art.WalkClipSpeed;
            // Everyone starts at a different point in their idle so a crowd doesn't breathe in unison.
            _state = Hash("Idle");
            _animator.Play(_state, 0, (float)rng.NextDouble());

            Transform hand = _animator.GetBoneTransform(HumanBodyBones.RightHand);
            if (hand != null)
            {
                _heldCup = kit.Cylinder(hand, "Cup", Vector3.zero, 0.08f, 0.12f, kit.P.Lit(new Color(0.9f, 0.88f, 0.84f))).transform;
                // Bones are scaled with the model; keep the cup a real-world size, just off the palm.
                _heldCup.localScale = new Vector3(0.08f, 0.06f, 0.08f) / Mathf.Max(0.01f, hand.lossyScale.x);
                _heldCup.localPosition = new Vector3(0f, 0.08f / Mathf.Max(0.01f, hand.lossyScale.x), 0f);
                _heldCup.gameObject.SetActive(false);
            }
        }

        private static int Hash(string state)
        {
            if (!StateHashes.TryGetValue(state, out int h)) StateHashes[state] = h = Animator.StringToHash(state);
            return h;
        }

        /// <summary>
        /// Set while a fight (<see cref="NpcFighter"/>) drives this body: its owner's <see cref="Animate"/> calls are
        /// ignored until it's handed back.
        /// </summary>
        public bool Overridden { get; set; }

        internal Animator Animator => _animator;

        /// <summary>Poses the body. <paramref name="time"/> drives primitive walk cycles and fidgets (any running clock).</summary>
        public void Animate(NpcPose pose, float time, float stride = 1f)
        {
            if (!Overridden) Pose(pose, time, stride);
        }

        /// <summary>Plays an animator state directly (reactions, falls); a no-op for primitive bodies.</summary>
        internal void PlayState(string state, float fade, float normalizedTime = 0f)
        {
            if (_animator == null) return;
            _state = Hash(state);
            _animator.CrossFadeInFixedTime(_state, fade, 0, normalizedTime);
        }

        internal void Pose(NpcPose pose, float time, float stride = 1f)
        {
            if (IsCharacter) AnimateCharacter(pose, stride);
            else AnimatePrimitive(pose, time, stride);
        }

        private void AnimateCharacter(NpcPose pose, float stride)
        {
            string state;
            float speed = 1f;
            switch (pose)
            {
                case NpcPose.Walk:
                    state = "Walk";
                    // stride 1 is a 1.35 m/s stroll (PedestrianView's convention); match the clip's pace to it.
                    speed = stride * 1.35f / _walkClipSpeed;
                    break;
                case NpcPose.Run:
                    state = "Jog";
                    speed = Mathf.Max(0.6f, stride * 1.35f / 4.83f); // the jog clip covers 4.83 m/s
                    break;
                case NpcPose.Sit: state = "Sit"; break;
                case NpcPose.SitTalk: state = "SitTalk"; break;
                case NpcPose.Typing: state = "Interact"; break;
                case NpcPose.Phone: state = "Talk"; break;
                case NpcPose.Cycle: state = "Drive"; break;
                default: state = "Idle"; break; // Stand, Drink
            }
            int hash = Hash(state);
            if (hash != _state)
            {
                _state = hash;
                _animator.CrossFadeInFixedTime(hash, 0.25f, 0, _phaseOffset % 1f);
            }
            _animator.SetFloat(SpeedParam, speed);
            bool cup = pose == NpcPose.Drink;
            if (_heldCup != null && _heldCup.gameObject.activeSelf != cup) _heldCup.gameObject.SetActive(cup);
        }

        private void BuildPrimitive(Kit kit, System.Random rng, Color? outfit)
        {
            Color skin = Skin[rng.Next(Skin.Length)];
            Color shirt = outfit ?? Clothes[rng.Next(Clothes.Length)];
            Color pants = Color.Lerp(Clothes[rng.Next(Clothes.Length)], new Color(0.12f, 0.12f, 0.14f), 0.6f);
            Color hair = Hair[rng.Next(Hair.Length)];
            float height = 0.93f + 0.14f * (float)rng.NextDouble();

            _body = Kit.Group(transform, "Body");
            _body.localScale = Vector3.one * height;
            Transform b = _body;

            _legL = Limb(kit, b, "LegL", new Vector3(-0.1f, 0.9f, 0f), new Vector3(0.15f, 0.9f, 0.18f), kit.P.Lit(pants));
            _legR = Limb(kit, b, "LegR", new Vector3(0.1f, 0.9f, 0f), new Vector3(0.15f, 0.9f, 0.18f), kit.P.Lit(pants));
            kit.Box(b, "Torso", new Vector3(0f, 1.21f, 0f), new Vector3(0.44f, 0.64f, 0.26f), kit.P.Lit(shirt), collider: false);
            _armL = Limb(kit, b, "ArmL", new Vector3(-0.28f, 1.49f, 0f), new Vector3(0.11f, 0.62f, 0.12f), kit.P.Lit(shirt));
            _armR = Limb(kit, b, "ArmR", new Vector3(0.28f, 1.49f, 0f), new Vector3(0.11f, 0.62f, 0.12f), kit.P.Lit(shirt));
            kit.Sphere(b, "Head", new Vector3(0f, 1.69f, 0f), 0.26f, kit.P.Lit(skin));
            kit.Box(b, "Hair", new Vector3(0f, 1.78f, -0.02f), new Vector3(0.25f, 0.1f, 0.25f), kit.P.Lit(hair), collider: false);
            _cup = kit.Cylinder(_armR, "Cup", new Vector3(0f, -0.62f, 0.06f), 0.08f, 0.12f, kit.P.Lit(new Color(0.9f, 0.88f, 0.84f))).transform;
            _cup.gameObject.SetActive(false);
        }

        private static Transform Limb(Kit kit, Transform parent, string name, Vector3 pivot, Vector3 size, Material m)
        {
            Transform joint = Kit.Group(parent, name, pivot);
            kit.Box(joint, name + "Mesh", new Vector3(0f, -size.y / 2f, 0f), size, m, collider: false);
            return joint;
        }

        private void AnimatePrimitive(NpcPose pose, float time, float stride)
        {
            float t = time + _phaseOffset;
            float legL = 0f, legR = 0f, armL = 0f, armR = 0f, drop = 0f;
            bool cup = false;
            switch (pose)
            {
                case NpcPose.Walk:
                case NpcPose.Run:
                    float swing = Mathf.Sin(t * 7.5f * stride) * (pose == NpcPose.Run ? 40f : 26f);
                    legL = swing;
                    legR = -swing;
                    armL = -swing * 0.7f;
                    armR = swing * 0.7f;
                    break;
                case NpcPose.Sit:
                case NpcPose.SitTalk:
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
