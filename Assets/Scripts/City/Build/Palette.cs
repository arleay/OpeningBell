using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Material instances for the generated city, cloned from a few template assets (so their shader variants
    /// ship in builds) and shared per colour so static batching can merge them. Also tracks what glows at night.
    /// </summary>
    public sealed class Palette
    {
        private readonly Material _lit, _litEmissive, _glass, _unlit, _sign;
        private readonly Dictionary<string, Material> _cache = new Dictionary<string, Material>();
        private readonly List<(Material Material, Color Off, Color On)> _lamps = new List<(Material, Color, Color)>();
        private readonly List<Material> _windows = new List<Material>();
        private readonly List<Material> _kitWindows = new List<Material>();
        private static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseMap = Shader.PropertyToID("_BaseMap");
        private static readonly int EmissionMap = Shader.PropertyToID("_EmissionMap");
        private static readonly int Smoothness = Shader.PropertyToID("_Smoothness");

        public Font Font { get; }

        public Palette(Material lit, Material litEmissive, Material glass, Material unlit, Material sign)
        {
            _lit = lit;
            _litEmissive = litEmissive;
            _glass = glass;
            _unlit = unlit;
            _sign = sign;
            Font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            // The font atlas can be rebuilt (new glyphs, new size); keep sign materials pointing at the live one.
            Font.textureRebuilt += OnFontRebuilt;
        }

        public void Dispose() => Font.textureRebuilt -= OnFontRebuilt;

        private void OnFontRebuilt(Font font)
        {
            if (font != Font) return;
            foreach (Material m in _cache.Values)
                if (m.shader == _sign.shader) m.mainTexture = font.material.mainTexture;
        }

        public Material Lit(Color color, float smoothness = 0.12f)
        {
            string key = $"lit{color}{smoothness}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_lit) { name = "City Lit", color = color };
            m.SetFloat(Smoothness, smoothness);
            return _cache[key] = m;
        }

        /// <summary>Always-bright surface: lamp heads, signal lenses, screens.</summary>
        public Material Unlit(Color color)
        {
            string key = $"unlit{color}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_unlit) { name = "City Unlit", color = color };
            return _cache[key] = m;
        }

        /// <summary>
        /// A light that is <paramref name="off"/> by day and <paramref name="on"/> at night. The night colour is
        /// pushed into HDR (× <paramref name="glow"/>) so bloom picks it up.
        /// </summary>
        public Material Lamp(Color off, Color on, float glow = 3f)
        {
            string key = $"lamp{off}{on}{glow}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_unlit) { name = "City Lamp", color = off };
            _lamps.Add((m, off, on * glow));
            return _cache[key] = m;
        }

        /// <summary>Always-on glowing surface (signal lenses): HDR so it blooms.</summary>
        public Material Glow(Color color, float glow = 2.2f) => Unlit(color * glow);

        public Material Glass(Color tint)
        {
            string key = $"glass{tint}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_glass) { name = "City Glass", color = tint };
            return _cache[key] = m;
        }

        public Material Facade(FacadeStyle style, bool storefront)
        {
            string key = $"facade{style}{storefront}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            FacadeTextures.Create(style, storefront, out Texture2D albedo, out Texture2D glow);
            m = new Material(_litEmissive) { name = "Facade " + style, color = Color.white };
            m.SetTexture(BaseMap, albedo);
            m.SetTexture(EmissionMap, glow);
            m.SetColor(EmissionColor, Color.black);
            m.SetFloat(Smoothness, style == FacadeStyle.Glass ? 0.55f : 0.08f);
            _windows.Add(m);
            return _cache[key] = m;
        }

        /// <summary>
        /// A Kenney kit palette texture, optionally tinted. With a window mask, the glass glows warm at night
        /// (every window, dimmer than the facades' scattered lit ones).
        /// </summary>
        public Material KitPalette(Texture2D palette, Texture2D windows, Color tint)
        {
            string key = $"kit{palette.GetHashCode()}{tint}"; // Unity objects hash by identity; kits all name theirs "colormap"
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_litEmissive) { name = "Kit " + palette.name, color = tint };
            m.SetTexture(BaseMap, palette);
            m.SetTexture(EmissionMap, windows);
            m.SetColor(EmissionColor, Color.black);
            m.SetFloat(Smoothness, 0.15f);
            if (windows != null) _kitWindows.Add(m);
            return _cache[key] = m;
        }

        public Material Sign(Color color)
        {
            string key = $"sign{color}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_sign) { name = "City Sign", color = color, mainTexture = Font.material.mainTexture };
            return _cache[key] = m;
        }

        /// <summary>0 = day, 1 = night: lamps switch on and some windows light up.</summary>
        public void ApplyNight(float night)
        {
            foreach (var (material, off, on) in _lamps) material.color = Color.Lerp(off, on, night);
            Color glow = new Color(1f, 0.86f, 0.66f) * (0.62f * night);
            foreach (Material w in _windows) w.SetColor(EmissionColor, glow);
            Color kitGlow = new Color(1f, 0.82f, 0.58f) * (0.5f * night);
            foreach (Material w in _kitWindows) w.SetColor(EmissionColor, kitGlow);
        }
    }
}
