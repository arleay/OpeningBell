using UnityEngine;

namespace OpeningBell.Gameplay
{
    /// <summary>Window glow and window light follow the game clock: cool morning, bright day, gold evening, dark night.</summary>
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

        private Material _windowMaterial;

        private void Start() => _windowMaterial = window.material;

        private void Update()
        {
            float t = (float)(game.Clock.Now.TimeOfDay.TotalHours / 24.0);
            Color sky = skyColor.Evaluate(t);
            _windowMaterial.color = sky;
            windowLight.color = sky;
            windowLight.intensity = lightIntensity.Evaluate(t);
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
