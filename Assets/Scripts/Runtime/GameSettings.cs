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

        // Accessibility (CASINO_SPEC §100). Read often, so cached.
        private static bool? _reduceMotion, _reduceFlashing, _hints;
        private static float? _casinoVolume;

        /// <summary>Shorter camera glides, reels and wheels that settle quickly.</summary>
        public static bool ReduceMotion
        {
            get => _reduceMotion ??= PlayerPrefs.GetInt("settings.reduceMotion", 0) == 1;
            set => SetFlag("settings.reduceMotion", ref _reduceMotion, value);
        }

        /// <summary>No chasing bulbs, strobing wins or flashing signs.</summary>
        public static bool ReduceFlashing
        {
            get => _reduceFlashing ??= PlayerPrefs.GetInt("settings.reduceFlashing", 0) == 1;
            set => SetFlag("settings.reduceFlashing", ref _reduceFlashing, value);
        }

        /// <summary>Basic-strategy hints at the blackjack table (a learning aid; off by default).</summary>
        public static bool GameHints
        {
            get => _hints ??= PlayerPrefs.GetInt("settings.gameHints", 0) == 1;
            set => SetFlag("settings.gameHints", ref _hints, value);
        }

        /// <summary>Casino ambience and machine sounds, relative to the master volume.</summary>
        public static float CasinoVolume
        {
            get => _casinoVolume ??= PlayerPrefs.GetFloat("settings.casinoVolume", 0.7f);
            set
            {
                _casinoVolume = Mathf.Clamp01(value);
                PlayerPrefs.SetFloat("settings.casinoVolume", _casinoVolume.Value);
                PlayerPrefs.Save();
            }
        }

        private static void SetFlag(string key, ref bool? cache, bool value)
        {
            cache = value;
            PlayerPrefs.SetInt(key, value ? 1 : 0);
            PlayerPrefs.Save();
        }
    }
}
