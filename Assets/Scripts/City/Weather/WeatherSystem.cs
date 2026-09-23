using System;
using OpeningBell.Core;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    public enum Weather
    {
        Clear,
        Cloudy,
        Overcast,
        Rain,
        HeavyRain,
        Fog,
        Storm,
    }

    /// <summary>
    /// The weather (TOWN_SPEC A14). A forecast per day from the world seed (so it needs no save data): the day is
    /// cut into four six-hour spells, each drawn from Pacific Northwest odds that lean overcast and wet, with
    /// neighbouring spells alike. It changes gradually (about twenty game minutes to turn), and it reaches
    /// everything: sun, ambient, sky and fog; rain streaks round the camera and its sound; roads that darken and
    /// shine when wet and dry off slowly; thunder and lightning in storms; fewer people out, slower traffic, and
    /// less grip on the tyres.
    /// </summary>
    public sealed class WeatherSystem : MonoBehaviour
    {
        /// <summary>Tests pin the weather (null: the forecast).</summary>
        public static Weather? Forced;

        private CityContext _c;
        private DaylightCycle _daylight;
        private PedestrianView _people;
        private TrafficView _traffic;
        private Camera _camera;
        private ParticleSystem _rain;
        private AudioSource _rainSound, _thunder;
        private ulong _seed;

        // Current (smoothed) levels, 0–1.
        private float _cloud, _rainLevel, _fog, _storm;
        private float _flash, _nextStrike = 20f;
        private System.Random _strikes = new System.Random(71);

        public Weather Current { get; private set; }
        /// <summary>How wet the ground is (lags the rain, dries off over about an hour).</summary>
        public float Wetness { get; private set; }
        public float Rain => _rainLevel;
        public float Cloud => _cloud;

        public static WeatherSystem Build(CityContext c, DaylightCycle daylight, PedestrianView people, TrafficView traffic, Camera camera)
        {
            var w = new GameObject("Weather").AddComponent<WeatherSystem>();
            w.transform.SetParent(c.Dynamic, false);
            w._c = c;
            w._daylight = daylight;
            w._people = people;
            w._traffic = traffic;
            w._camera = camera;
            w._seed = c.Game.Seed;
            w.BuildRain();
            Weather now = w.At(c.Game.Clock.Now);
            w.Snap(now);
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
            Weather prev = Draw(random.CreateStream($"weather/{day - 1}/3").NextDouble(), Weather.Overcast);
            SeededRandom r = random.CreateStream($"weather/{day}");
            var spells = new Weather[4];
            for (int i = 0; i < 4; i++)
            {
                // Mostly the same as the last spell, sometimes a change.
                prev = r.NextDouble() < 0.55 ? prev : Draw(r.NextDouble(), prev);
                spells[i] = prev;
            }
            return spells;
        }

        /// <summary>PNW odds: overcast and rain are common, fog in the morning spells, storms rare.</summary>
        private static Weather Draw(double roll, Weather from)
        {
            if (roll < 0.14) return Weather.Clear;
            if (roll < 0.34) return Weather.Cloudy;
            if (roll < 0.58) return Weather.Overcast;
            if (roll < 0.78) return Weather.Rain;
            if (roll < 0.87) return Weather.HeavyRain;
            if (roll < 0.96) return Weather.Fog;
            return Weather.Storm;
        }

        /// <summary>Target levels for a kind of weather: cloud cover, rain, fog, storm.</summary>
        public static (float Cloud, float Rain, float Fog, float Storm) Levels(Weather w) => w switch
        {
            Weather.Clear => (0f, 0f, 0f, 0f),
            Weather.Cloudy => (0.35f, 0f, 0f, 0f),
            Weather.Overcast => (0.75f, 0f, 0.1f, 0f),
            Weather.Rain => (0.85f, 0.55f, 0.25f, 0f),
            Weather.HeavyRain => (0.95f, 1f, 0.45f, 0f),
            Weather.Fog => (0.7f, 0f, 1f, 0f),
            _ => (1f, 1f, 0.4f, 1f),
        };

        private void Snap(Weather w)
        {
            Current = w;
            (_cloud, _rainLevel, _fog, _storm) = Levels(w);
            Wetness = _rainLevel > 0f ? 1f : 0f;
            Push(0f);
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
            var (cloud, rain, fog, storm) = Levels(Current);
            // About twenty game minutes to change over (and never slower than a few real seconds).
            float k = 1f - Mathf.Exp(-Mathf.Max(gameMinutes / 20f, dt / 8f));
            _cloud = Mathf.Lerp(_cloud, cloud, k);
            _rainLevel = Mathf.Lerp(_rainLevel, rain, k);
            _fog = Mathf.Lerp(_fog, fog, k);
            _storm = Mathf.Lerp(_storm, storm, k);
            // Wet fast when it rains, dry over about an hour of game time.
            Wetness = _rainLevel > 0.05f ? Mathf.MoveTowards(Wetness, 1f, gameMinutes / 8f + dt * 0.02f) : Mathf.MoveTowards(Wetness, 0f, gameMinutes / 60f);
            Lightning(dt);
            Push(dt);
        }

        private void Lightning(float dt)
        {
            _flash = Mathf.MoveTowards(_flash, 0f, dt * 3f);
            if (_storm < 0.5f) return;
            _nextStrike -= dt;
            if (_nextStrike > 0f) return;
            _nextStrike = 8f + (float)_strikes.NextDouble() * 25f;
            _flash = 1f;
            // Thunder follows the flash (the strike is somewhere over the hills).
            if (_thunder != null) _thunder.PlayDelayed(0.6f + (float)_strikes.NextDouble() * 2.5f);
        }

        /// <summary>Everything that shows the weather.</summary>
        private void Push(float dt)
        {
            if (_daylight != null)
            {
                _daylight.Grey = Mathf.Clamp01(_cloud);
                _daylight.SunScale = Mathf.Lerp(1f, 0.18f, _cloud);
                // Clear days see across the valley; rain and fog close it in.
                _daylight.FogScale = Mathf.Lerp(2.4f, 0.9f, _cloud) * Mathf.Lerp(1f, 0.55f, _rainLevel) * Mathf.Lerp(1f, 0.28f, _fog);
                _daylight.Flash = _flash;
            }
            _c.P.ApplyWetness(Wetness);
            CarController.WeatherGrip = 1f - 0.18f * Wetness;
            if (_people != null) _people.Outdoors = 1f - 0.6f * _rainLevel;
            if (_traffic != null) _traffic.Simulation.SpeedFactor = 1f - 0.15f * _rainLevel;
            if (_rain != null)
            {
                ParticleSystem.EmissionModule emission = _rain.emission;
                emission.rateOverTime = 2600f * _rainLevel;
                Camera cam = _camera != null ? _camera : Camera.main;
                if (cam != null) _rain.transform.position = cam.transform.position + Vector3.up * 10f;
            }
            if (_rainSound != null)
            {
                _rainSound.volume = Mathf.Clamp01(_rainLevel) * 0.55f;
                if (_rainLevel > 0.02f && !_rainSound.isPlaying) _rainSound.Play();
                else if (_rainLevel <= 0.02f && _rainSound.isPlaying) _rainSound.Stop();
            }
        }

        private void BuildRain()
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(transform, false);
            _rain = go.AddComponent<ParticleSystem>();
            _rain.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = _rain.main;
            main.loop = true;
            main.startLifetime = 1.1f;
            main.startSpeed = 18f;
            main.startSize3D = true;
            main.startSizeX = 0.02f;
            main.startSizeY = 0.5f;
            main.startSizeZ = 0.02f;
            main.startColor = new Color(0.75f, 0.8f, 0.85f, 0.45f);
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.maxParticles = 6000;
            main.gravityModifier = 0.4f;
            ParticleSystem.ShapeModule shape = _rain.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = new Vector3(44f, 44f, 1f); // x and y are the flat square once rotated to face down
            shape.rotation = new Vector3(90f, 0f, 0f); // emit downward
            ParticleSystem.EmissionModule emission = _rain.emission;
            emission.rateOverTime = 0f;
            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.velocityScale = 0.04f;
            renderer.lengthScale = 1.5f;
            renderer.sharedMaterial = _c.P.Unlit(new Color(0.7f, 0.76f, 0.82f));
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _rain.Play();

            _rainSound = gameObject.AddComponent<AudioSource>();
            _rainSound.clip = ProceduralSounds.Noise("rain", 0.55f);
            _rainSound.loop = true;
            _rainSound.spatialBlend = 0f;
            _rainSound.volume = 0f;
            _thunder = gameObject.AddComponent<AudioSource>();
            _thunder.clip = ProceduralSounds.Noise("thunder", 0.004f);
            _thunder.spatialBlend = 0f;
            _thunder.volume = 0.9f;
            _thunder.pitch = 0.5f;
        }

        private void OnDestroy() => CarController.WeatherGrip = 1f;
    }
}
