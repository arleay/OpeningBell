using System;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>Kinds of weather. No rain: the town is always dry (the player asked for it gone).</summary>
    public enum Weather
    {
        Clear,
        Cloudy,
        Overcast,
        Fog,
    }

    /// <summary>
    /// The weather (TOWN_SPEC A14, rain removed). A forecast per day from the world seed (so it needs no save data):
    /// the day is cut into four six-hour spells, each drawn from odds that lean grey, with neighbouring spells alike.
    /// It changes gradually (about twenty game minutes to turn) and shows in the sun, ambient, sky and fog.
    /// </summary>
    public sealed class WeatherSystem : MonoBehaviour
    {
        /// <summary>Tests pin the weather (null: the forecast).</summary>
        public static Weather? Forced;

        private CityContext _c;
        private DaylightCycle _daylight;
        private ulong _seed;

        // Current (smoothed) levels, 0–1.
        private float _cloud, _fog;

        public Weather Current { get; private set; }
        public float Cloud => _cloud;

        public static WeatherSystem Build(CityContext c, DaylightCycle daylight)
        {
            var w = new GameObject("Weather").AddComponent<WeatherSystem>();
            w.transform.SetParent(c.Dynamic, false);
            w._c = c;
            w._daylight = daylight;
            w._seed = c.Game.Seed;
            w.Snap(w.At(c.Game.Clock.Now));
            return w;
        }

        // ---- forecast ----

        /// <summary>The weather at a moment: four spells a day, each a draw from the day's stream.</summary>
        public Weather At(DateTime t)
        {
            if (Forced.HasValue) return Forced.Value;
            int day = (int)(t.Date - new DateTime(2000, 1, 1)).TotalDays;
            Weather[] spells = Spells(day);
            return spells[Mathf.Clamp(t.Hour / 6, 0, 3)];
        }

        public Weather[] Spells(int day)
        {
            var random = new SeededRandomService(_seed);
            // Carry the previous day's last spell so days join up.
            Weather prev = Draw(random.CreateStream($"weather/{day - 1}/3").NextDouble());
            SeededRandom r = random.CreateStream($"weather/{day}");
            var spells = new Weather[4];
            for (int i = 0; i < 4; i++)
            {
                // Mostly the same as the last spell, sometimes a change.
                prev = r.NextDouble() < 0.55 ? prev : Draw(r.NextDouble());
                spells[i] = prev;
            }
            return spells;
        }

        /// <summary>Pacific Northwest greys without the rain: clear a fifth of the time, fog now and then.</summary>
        private static Weather Draw(double roll)
        {
            if (roll < 0.22) return Weather.Clear;
            if (roll < 0.52) return Weather.Cloudy;
            if (roll < 0.88) return Weather.Overcast;
            return Weather.Fog;
        }

        /// <summary>Target levels for a kind of weather: cloud cover and fog.</summary>
        public static (float Cloud, float Fog) Levels(Weather w) => w switch
        {
            Weather.Clear => (0f, 0f),
            Weather.Cloudy => (0.35f, 0f),
            Weather.Overcast => (0.75f, 0.1f),
            _ => (0.7f, 1f),
        };

        private void Snap(Weather w)
        {
            Current = w;
            (_cloud, _fog) = Levels(w);
            Push();
        }

        /// <summary>Changes the weather right away (dev key, tests); the forecast takes over at the next spell.</summary>
        public void Set(Weather w) => Snap(w);

        // ---- each frame ----

        private void Update()
        {
            if (_c == null) return;
            float dt = Time.deltaTime;
            // Game minutes per real second, so weather turns in game time (and a sleep skips it along).
            double scale = _c.Game.IsPaused ? 0 : _c.Game.Clock.TimeScale;
            float gameMinutes = (float)(dt * scale / 60.0);
            Current = At(_c.Game.Clock.Now);
            var (cloud, fog) = Levels(Current);
            // About twenty game minutes to change over (and never slower than a few real seconds).
            float k = 1f - Mathf.Exp(-Mathf.Max(gameMinutes / 20f, dt / 8f));
            _cloud = Mathf.Lerp(_cloud, cloud, k);
            _fog = Mathf.Lerp(_fog, fog, k);
            Push();
        }

        /// <summary>Everything that shows the weather.</summary>
        private void Push()
        {
            if (_daylight == null) return;
            _daylight.Grey = Mathf.Clamp01(_cloud);
            _daylight.SunScale = Mathf.Lerp(1f, 0.18f, _cloud);
            // Clear days see across the valley; fog closes it in.
            _daylight.FogScale = Mathf.Lerp(2.4f, 0.9f, _cloud) * Mathf.Lerp(1f, 0.28f, _fog);
        }
    }
}
