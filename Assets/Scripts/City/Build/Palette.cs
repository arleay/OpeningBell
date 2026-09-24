using System.Collections.Generic;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>Photographed surface finishes (CC0 ambientCG sets in Resources/Surfaces) for <see cref="Palette.Surface"/>.</summary>
    public enum Finish { Asphalt, Brick, Concrete, Corrugated, Grass, PaintedPlaster, Paving, Plaster, Tiles, WoodFloor, Siding }

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

        /// <summary>
        /// A pale surface with a little light of its own (× <paramref name="glow"/>): stands in for the light that
        /// bounces round a bright room, which the renderer doesn't compute, so ceilings and walls read white, not grey.
        /// </summary>
        public Material Bounce(Color color, float glow, float smoothness = 0.05f)
        {
            string key = $"bounce{color}{glow}{smoothness}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_litEmissive) { name = "City Bounce", color = color };
            m.SetTexture(BaseMap, Texture2D.whiteTexture);
            m.SetTexture(EmissionMap, Texture2D.whiteTexture);
            m.SetColor(EmissionColor, color * glow);
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
        /// Window glass for the outside-only facades: dark and glossy by day (it picks up the sky), and warm at night
        /// when <paramref name="litAtNight"/> (someone's home), so a building shows a scatter of lit rooms. With the
        /// Window Interior shader (Resources/Surfaces/Window) each pane shows a room behind it: its geometry must carry
        /// the room in its UVs (ModularFacade stamps them). Plain emissive glass where the asset is missing.
        /// </summary>
        public Material Pane(bool litAtNight)
        {
            string key = $"pane{litAtNight}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            var template = Resources.Load<Material>("Surfaces/Window");
            if (template != null)
            {
                m = new Material(template) { name = litAtNight ? "Pane lit" : "Pane dark" };
                m.SetColor(EmissionColor, Color.black);
                if (litAtNight) _windows.Add(m);
                return _cache[key] = m;
            }
            m = new Material(_litEmissive) { name = litAtNight ? "Pane lit" : "Pane dark", color = new Color(0.16f, 0.2f, 0.24f) };
            m.SetTexture(BaseMap, Texture2D.whiteTexture);
            m.SetTexture(EmissionMap, Texture2D.whiteTexture);
            m.SetColor(EmissionColor, Color.black);
            m.SetFloat(Smoothness, 0.88f);
            if (litAtNight) _windows.Add(m);
            return _cache[key] = m;
        }

        /// <summary>A lit material with a (tiling) texture, e.g. corrugated metal. Cached by name and tint.</summary>
        public Material Textured(string name, Texture2D texture, Color tint, float smoothness = 0.12f)
        {
            string key = $"tex{name}{tint}{smoothness}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_lit) { name = "City " + name, color = tint };
            m.SetTexture(BaseMap, texture);
            m.SetFloat(Smoothness, smoothness);
            return _cache[key] = m;
        }

        private static readonly int BumpMap = Shader.PropertyToID("_BumpMap");
        private static readonly int BumpScale = Shader.PropertyToID("_BumpScale");
        private static readonly int Contrast = Shader.PropertyToID("_Parallax");
        private static readonly int BaseMapSt = Shader.PropertyToID("_BaseMap_ST");
        private static readonly int PhotoColour = Shader.PropertyToID("_OcclusionStrength");
        private Material _triplanar;
        private bool _triplanarLoaded;

        /// <summary>
        /// A photographed CC0 finish (Resources/Surfaces) projected in world space by the triplanar shader, so it
        /// tiles at true scale on any generated box, span or road mesh. The texture is normalised to its own average
        /// colour, then tinted: <paramref name="tint"/> is the colour the surface reads as from a distance (the same
        /// colours the flat materials used), and the texture only adds the brick courses, grain and wear around it.
        /// Falls back to a flat <see cref="Lit"/> where the assets are missing (EditMode tests).
        /// </summary>
        public Material Surface(Finish finish, Color tint, float smoothness = 0.1f)
        {
            string key = $"surface{finish}{tint}{smoothness}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            if (!_triplanarLoaded)
            {
                _triplanarLoaded = true;
                _triplanar = Resources.Load<Material>("Surfaces/Triplanar");
            }
            var albedo = Resources.Load<Texture2D>($"Surfaces/{finish}_Color");
            if (_triplanar == null || albedo == null) return _cache[key] = Lit(tint, smoothness);
            (float metres, float contrast, float bump, float hue) = FinishLook(finish);
            m = new Material(_triplanar) { name = "City " + finish, color = tint };
            m.SetTexture(BaseMap, albedo);
            m.SetTexture(BumpMap, Resources.Load<Texture2D>($"Surfaces/{finish}_Normal"));
            m.SetFloat(BumpScale, bump);
            m.SetFloat(Contrast, contrast);
            m.SetFloat(PhotoColour, hue);
            m.SetFloat(Smoothness, smoothness);
            m.SetVector(BaseMapSt, new Vector4(1f / metres, 0f, 0f, 0f));
            return _cache[key] = m;
        }

        /// <summary>
        /// Metres one texture tile covers (matched to the photo: brick courses ~7.5 cm, 60 cm paving slabs, 15 cm
        /// siding boards), how much of its contrast survives (lower = calmer, more stylised), normal strength, and
        /// how much of the photo's own hue variation to keep (0 for painted finishes: the paint colour is the tint's).
        /// </summary>
        private static (float Metres, float Contrast, float Bump, float Hue) FinishLook(Finish f) => f switch
        {
            Finish.Asphalt => (3f, 0.9f, 0.6f, 0f),
            Finish.Brick => (1.6f, 0.85f, 1f, 0.7f),
            Finish.Concrete => (3f, 0.7f, 0.5f, 0f),
            Finish.Corrugated => (1.5f, 0.8f, 1f, 0f),
            Finish.Grass => (2.5f, 0.75f, 0.6f, 0.5f),
            Finish.PaintedPlaster => (2.5f, 0.6f, 0.6f, 0f),
            Finish.Paving => (2.4f, 0.75f, 0.8f, 0f),
            Finish.Plaster => (2.5f, 0.55f, 0.5f, 0f),
            Finish.Tiles => (1.2f, 0.8f, 0.7f, 0f),
            Finish.WoodFloor => (2f, 0.85f, 0.6f, 0.6f),
            Finish.Siding => (2f, 0.6f, 1f, 0f),
            _ => (2f, 0.8f, 1f, 0f),
        };

        public Material Sign(Color color)
        {
            string key = $"sign{color}";
            if (_cache.TryGetValue(key, out Material m)) return m;
            m = new Material(_sign) { name = "City Sign", color = color, mainTexture = Font.material.mainTexture };
            return _cache[key] = m;
        }

        /// <summary>0 = day, 1 = night: lamps switch on and some windows light up.</summary>
        private readonly List<(Material Material, Color Dry, float Smooth)> _wet = new List<(Material, Color, float)>();
        private float _wetness = -1f;

        /// <summary>Marks a ground material to darken and shine when it rains (roads, sidewalks, paving).</summary>
        public Material Wettable(Material m)
        {
            if (!_wet.Exists(x => x.Material == m)) _wet.Add((m, m.color, m.GetFloat(Smoothness)));
            return m;
        }

        /// <summary>0 dry … 1 soaked: darker and glossier (puddle sheen), smoothly.</summary>
        public void ApplyWetness(float wetness)
        {
            if (Mathf.Abs(wetness - _wetness) < 0.01f) return;
            _wetness = wetness;
            foreach (var (m, dry, smooth) in _wet)
            {
                m.color = Color.Lerp(dry, dry * 0.6f, wetness);
                m.SetFloat(Smoothness, Mathf.Lerp(smooth, 0.78f, wetness));
            }
        }

        public void ApplyNight(float night)
        {
            foreach (var (material, off, on) in _lamps) material.color = Color.Lerp(off, on, night);
            Color glow = new Color(1f, 0.86f, 0.66f) * (0.62f * night);
            foreach (Material w in _windows) w.SetColor(EmissionColor, glow);
        }
    }
}
