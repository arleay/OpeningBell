using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Third-party models the city dresses itself with (see CREDITS.md): Kenney kits for buildings, street
    /// furniture and interiors, looked up by file name, and Quaternius people sharing one humanoid animator.
    /// Filled by the editor's "Opening Bell/Rebuild City Art" command; builders fall back to primitives when a
    /// model is missing, so layout code never depends on art being present.
    /// </summary>
    [CreateAssetMenu(menuName = "Opening Bell/City Art")]
    public sealed class CityArt : ScriptableObject
    {
        [SerializeField] private GameObject[] models = new GameObject[0];
        [SerializeField] private GameObject[] people = new GameObject[0];
        [Tooltip("Kit palette textures and their night-window masks, named \"Kit/file\" (e.g. CityCommercial/variation-a-glow).")]
        [SerializeField] private Texture2D[] palettes = new Texture2D[0];
        [SerializeField] private string[] paletteNames = new string[0];
        [SerializeField] private RuntimeAnimatorController peopleAnimator;
        [Tooltip("Metres per second the Walk state covers at speed 1 (measured from the root-motion clip).")]
        [SerializeField] private float walkClipSpeed = 1.3f;

        private Dictionary<string, GameObject> _byName;
        private Dictionary<string, Texture2D> _palettes;
        private readonly Dictionary<string, Bounds> _bounds = new Dictionary<string, Bounds>();

        public IReadOnlyList<GameObject> People => people;
        public RuntimeAnimatorController PeopleAnimator => peopleAnimator;
        public float WalkClipSpeed => walkClipSpeed;
        public bool HasPeople => people.Length > 0 && peopleAnimator != null;

        /// <summary>The model named <paramref name="name"/> (its file name without extension), or null.</summary>
        public GameObject Model(string name)
        {
            if (_byName == null)
            {
                _byName = new Dictionary<string, GameObject>();
                foreach (GameObject m in models)
                    if (m != null) _byName[m.name] = m;
            }
            return _byName.TryGetValue(name, out GameObject found) ? found : null;
        }

        /// <summary>
        /// Bounds of the model's meshes in its root's space, ignoring the root's own rotation and scale (which
        /// <see cref="Kit.Model"/> replaces). Measured from the meshes rather than taken from the art report, because
        /// some kit files scale their mesh child. Empty bounds when the model is missing.
        /// </summary>
        public Bounds ModelBounds(string name)
        {
            if (_bounds.TryGetValue(name, out Bounds cached)) return cached;
            GameObject root = Model(name);
            Bounds b = default;
            bool any = false;
            if (root != null)
                foreach (MeshFilter mf in root.GetComponentsInChildren<MeshFilter>(true))
                {
                    if (mf.sharedMesh == null) continue;
                    Matrix4x4 toRoot = root.transform.worldToLocalMatrix * mf.transform.localToWorldMatrix;
                    Bounds mb = mf.sharedMesh.bounds;
                    for (int i = 0; i < 8; i++)
                    {
                        Vector3 corner = mb.center + Vector3.Scale(mb.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
                        Vector3 p = toRoot.MultiplyPoint3x4(corner);
                        if (any) b.Encapsulate(p);
                        else b = new Bounds(p, Vector3.zero);
                        any = true;
                    }
                }
            return _bounds[name] = b;
        }

        /// <summary>A kit palette texture by "Kit/file" name, or null.</summary>
        public Texture2D Palette(string name)
        {
            if (_palettes == null)
            {
                _palettes = new Dictionary<string, Texture2D>();
                for (int i = 0; i < palettes.Length && i < paletteNames.Length; i++) _palettes[paletteNames[i]] = palettes[i];
            }
            return _palettes.TryGetValue(name, out Texture2D t) ? t : null;
        }

#if UNITY_EDITOR
        public void EditorSet(GameObject[] modelList, GameObject[] peopleList, RuntimeAnimatorController animator, float walkSpeed,
            Texture2D[] paletteList, string[] paletteNameList)
        {
            palettes = paletteList;
            paletteNames = paletteNameList;
            _palettes = null;
            models = modelList;
            people = peopleList;
            peopleAnimator = animator;
            walkClipSpeed = walkSpeed;
            _byName = null;
            _bounds.Clear();
        }
#endif
    }
}
