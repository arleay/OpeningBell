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
        }
#endif
    }
}
