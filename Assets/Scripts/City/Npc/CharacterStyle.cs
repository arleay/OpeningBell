using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The character creator's choices applied to a character model: which of the art library's people, and tints
    /// for skin, hair and the outfit's main colour (index 0 keeps the model's own colour). The Quaternius people
    /// use flat colour materials named "Skin", "Hair"..., so recolouring is a material tint.
    /// </summary>
    public static class CharacterStyle
    {
        public static readonly Color[] Skins =
        {
            Color.clear, new Color(0.98f, 0.84f, 0.74f), new Color(0.92f, 0.73f, 0.58f), new Color(0.78f, 0.56f, 0.4f),
            new Color(0.58f, 0.39f, 0.27f), new Color(0.38f, 0.25f, 0.17f),
        };

        public static readonly Color[] Hairs =
        {
            Color.clear, new Color(0.08f, 0.07f, 0.06f), new Color(0.24f, 0.15f, 0.09f), new Color(0.45f, 0.29f, 0.16f),
            new Color(0.86f, 0.7f, 0.42f), new Color(0.58f, 0.22f, 0.1f), new Color(0.62f, 0.62f, 0.62f),
            new Color(0.22f, 0.38f, 0.78f), new Color(0.9f, 0.45f, 0.62f),
        };

        public static readonly Color[] Tops =
        {
            Color.clear, new Color(0.92f, 0.92f, 0.9f), new Color(0.12f, 0.12f, 0.14f), new Color(0.16f, 0.22f, 0.4f),
            new Color(0.72f, 0.16f, 0.14f), new Color(0.2f, 0.42f, 0.26f), new Color(0.86f, 0.66f, 0.2f),
            new Color(0.45f, 0.66f, 0.86f), new Color(0.46f, 0.28f, 0.62f), new Color(0.9f, 0.46f, 0.16f), new Color(0.5f, 0.5f, 0.52f),
        };

        /// <summary>The person the look names: by index, unless the library was reordered (then by label).</summary>
        public static int ModelIndex(CityArt art, OpeningBell.PlayerLook look)
        {
            if (look == null || art.People.Count == 0) return 0;
            if (look.Model >= 0 && look.Model < art.People.Count && (look.ModelLabel == "" || art.PeopleLabel(look.Model) == look.ModelLabel))
                return look.Model;
            for (int i = 0; i < art.People.Count; i++)
                if (art.PeopleLabel(i) == look.ModelLabel) return i;
            return 0;
        }

        /// <summary>
        /// The skin "the model's own colour" stands for when the model has none: the Tiny set's re-rig left its Skin
        /// material near black (0.013), which drew every Tiny face and hand black.
        /// </summary>
        public static readonly Color DefaultSkin = new Color(0.94f, 0.76f, 0.62f);

        /// <summary>Tints a character instance (its own material copies) to the look (null: just fixes a missing skin).</summary>
        public static void Apply(GameObject character, OpeningBell.PlayerLook look)
        {
            look ??= new OpeningBell.PlayerLook();
            foreach (SkinnedMeshRenderer r in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
            {
                Material[] mats = r.materials; // instances: this character only
                int main = MainOutfit(r.sharedMesh, mats);
                for (int i = 0; i < mats.Length; i++)
                {
                    string n = mats[i].name;
                    if (n.Contains("Skin") && look.Skin == 0 && mats[i].color.maxColorComponent < 0.05f) mats[i].color = DefaultSkin;
                    if (n.Contains("Skin") && look.Skin > 0) mats[i].color = Skins[look.Skin % Skins.Length];
                    else if (n.Contains("Hair") && look.Hair > 0) mats[i].color = Hairs[look.Hair % Hairs.Length];
                    else if (i == main && look.Top > 0) mats[i].color = Tops[look.Top % Tops.Length];
                }
                r.materials = mats;
            }
        }

        /// <summary>The outfit's main colour: the material covering the most triangles that isn't skin, hair or eyes.</summary>
        private static int MainOutfit(Mesh mesh, IReadOnlyList<Material> mats)
        {
            int best = -1;
            uint most = 0;
            for (int i = 0; i < mats.Count && mesh != null && i < mesh.subMeshCount; i++)
            {
                string n = mats[i].name;
                if (n.Contains("Skin") || n.Contains("Hair") || n.Contains("Eye")) continue;
                uint count = mesh.GetIndexCount(i);
                if (count > most)
                {
                    most = count;
                    best = i;
                }
            }
            return best;
        }
    }
}
