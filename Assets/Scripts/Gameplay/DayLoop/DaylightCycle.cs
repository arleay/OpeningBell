using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>
    /// Follows the game clock: window glow and window light (cool morning, bright day, gold evening, dark night),
    /// plus the outdoor sun, ambient light and fog. Others read <see cref="NightFactor"/> to switch lights on.
    /// </summary>
    public sealed class DaylightCycle : MonoBehaviour
    {
        [SerializeField] private GameBootstrap game;
        [SerializeField] private Renderer window;
        [SerializeField] private Light windowLight;
        [Tooltip("Sky colour over the day (0 = midnight, 1 = next midnight).")]
        [SerializeField] private Gradient skyColor = DefaultSky();
        [Tooltip("Window light intensity over the day.")]
        [SerializeField] private AnimationCurve lightIntensity = new AnimationCurve(
            new Keyframe(0f, 0.05f), new Keyframe(0.25f, 0.4f), new Keyframe(0.35f, 0.9f),
            new Keyframe(0.7f, 0.9f), new Keyframe(0.8f, 0.5f), new Keyframe(0.9f, 0.08f), new Keyframe(1f, 0.05f));

        [Header("Outdoors (optional)")]
        [SerializeField] private Light sun;
        [SerializeField] private float sunriseHour = 6.25f;
        [SerializeField] private float sunsetHour = 18.75f;
        [Tooltip("Noon elevation in degrees (a low winter sun reads better on flat-shaded boxes).")]
        [SerializeField] private float noonElevation = 48f;
        [SerializeField] private float sunIntensity = 1.3f;
        [SerializeField] private Color dayAmbient = new Color(0.46f, 0.48f, 0.52f);
        [Tooltip("Floor for ambient light so interiors and streets stay readable at night.")]
        [SerializeField] private Color nightAmbient = new Color(0.17f, 0.18f, 0.25f);
        [SerializeField] private float fogStart = 70f;
        [SerializeField] private float fogEnd = 380f;

        private Material _windowMaterial;

        /// <summary>0 in daylight, 1 at night; eases across dawn and dusk. Street lights and lit windows follow it.</summary>
        public float NightFactor { get; private set; }

        private void Start()
        {
            _windowMaterial = window.material;
            if (sun == null) return;
            RenderSettings.sun = sun;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = fogStart;
            RenderSettings.fogEndDistance = fogEnd;
            Apply();
        }

        private void Update() => Apply();

        private void Apply()
        {
            double hours = game.Clock.Now.TimeOfDay.TotalHours;
            float t = (float)(hours / 24.0);
            Color sky = skyColor.Evaluate(t);
            _windowMaterial.color = sky;
            windowLight.color = sky;
            windowLight.intensity = lightIntensity.Evaluate(t);
            if (sun == null) return;

            // Sun arc: rises in the east (+x), peaks south at noon, sets in the west.
            float day = Mathf.InverseLerp(sunriseHour, sunsetHour, (float)hours);
            bool up = hours > sunriseHour && hours < sunsetHour;
            float elevation = up ? Mathf.Sin(day * Mathf.PI) * noonElevation : -10f;
            float azimuth = Mathf.Lerp(90f, 270f, day); // compass degrees from north
            sun.transform.rotation = Quaternion.Euler(elevation, azimuth + 180f, 0f);

            // Daylight fades in over the first/last ~6° of elevation so dawn and dusk are gradual.
            float daylight = up ? Mathf.Clamp01(elevation / 6f) : 0f;
            NightFactor = 1f - Mathf.Clamp01(up ? elevation / 3f : 0f);
            Color warm = Color.Lerp(new Color(1f, 0.62f, 0.38f), new Color(1f, 0.96f, 0.9f), Mathf.Clamp01(elevation / 25f));
            sun.color = warm;
            sun.intensity = sunIntensity * daylight;
            sun.enabled = daylight > 0f;
            RenderSettings.ambientLight = Color.Lerp(nightAmbient, dayAmbient, daylight);
            RenderSettings.fogColor = Color.Lerp(nightAmbient * 0.6f, sky, 0.75f);
        }

        private static Gradient DefaultSky()
        {
            var g = new Gradient();
            g.SetKeys(
                new[]
                {
                    new GradientColorKey(new Color(0.04f, 0.06f, 0.12f), 0.00f), // night
                    new GradientColorKey(new Color(0.45f, 0.45f, 0.60f), 0.25f), // 6:00 dawn
                    new GradientColorKey(new Color(0.72f, 0.84f, 0.97f), 0.38f), // morning
                    new GradientColorKey(new Color(0.80f, 0.88f, 0.98f), 0.62f), // afternoon
                    new GradientColorKey(new Color(0.98f, 0.68f, 0.36f), 0.74f), // ~17:45 golden
                    new GradientColorKey(new Color(0.35f, 0.24f, 0.38f), 0.82f), // dusk
                    new GradientColorKey(new Color(0.04f, 0.06f, 0.12f), 0.90f), // night
                },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
            return g;
        }
    }
}
