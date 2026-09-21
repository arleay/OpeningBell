using UnityEngine;

namespace OpeningBell
{
    /// <summary>Per-machine player preferences (PlayerPrefs), separate from save games.</summary>
    public static class GameSettings
    {
        private const string SensitivityKey = "settings.mouseSensitivity";
        private const string VolumeKey = "settings.masterVolume";

        private static float? _sensitivity;

        /// <summary>Multiplier on the base look sensitivity (1 = default).</summary>
        public static float MouseSensitivity
        {
            get => _sensitivity ??= PlayerPrefs.GetFloat(SensitivityKey, 1f);
            set
            {
                _sensitivity = Mathf.Clamp(value, 0.1f, 5f);
                PlayerPrefs.SetFloat(SensitivityKey, _sensitivity.Value);
                PlayerPrefs.Save();
            }
        }

        public static float MasterVolume
        {
            get => PlayerPrefs.GetFloat(VolumeKey, 0.8f);
            set
            {
                PlayerPrefs.SetFloat(VolumeKey, Mathf.Clamp01(value));
                PlayerPrefs.Save();
                Apply();
            }
        }

        public static void Apply() => AudioListener.volume = MasterVolume;
    }
}
