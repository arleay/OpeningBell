using System.Collections.Generic;
using OpeningBell.Gameplay;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// The Northline shuttle: three cars out of the west tunnel on the south track, a stop at Maple Station, into
    /// the east tunnel; a pause; back on the north track. Runs on real time, pauses with the game. Kinematic
    /// colliders (you can't walk through it on the platform), a rumble that grows with speed, lit windows at night.
    /// </summary>
    public sealed class RailTrain : MonoBehaviour
    {
        public enum Phase { Running, Dwelling, Hidden }

        private const int Cars = 3;
        private const float CarLength = 17f, Gap = 1f;
        private const float MaxSpeed = 16f, Accel = 0.9f, Decel = 1.0f;
        private const float DwellSeconds = 25f, HiddenSeconds = 50f;

        private CityContext _c;
        private readonly List<Transform> _cars = new List<Transform>();
        private AudioSource _rumble;
        private float _speed, _timer;
        private bool _eastbound = true, _stopped;

        /// <summary>Head of the train (x) and how it's doing.</summary>
        public float X { get; private set; }
        public Phase State { get; private set; } = Phase.Hidden;
        public bool Eastbound => _eastbound;

        private static float TrainLength => Cars * CarLength + (Cars - 1) * Gap;
        private float Start => _eastbound ? CityPlan.RailWest - 50f : CityPlan.RailEast + 50f;
        private float End => _eastbound ? CityPlan.RailEast + 50f + TrainLength : CityPlan.RailWest - 50f - TrainLength;
        /// <summary>Where the head stops so the train sits centred on the platforms.</summary>
        private float StopAt => (CityPlan.StationWest + CityPlan.StationEast) / 2f + (_eastbound ? 1f : -1f) * TrainLength / 2f;

        public void Configure(CityContext c)
        {
            _c = c;
            Kit k = c.Kit;
            Material body = c.P.Lit(new Color(0.85f, 0.86f, 0.84f), 0.35f);
            Material stripe = c.P.Lit(new Color(0.12f, 0.36f, 0.62f), 0.35f);
            Material window = c.P.Lamp(new Color(0.14f, 0.17f, 0.2f), new Color(1f, 0.93f, 0.78f), 1.4f);
            Material under = c.P.Lit(new Color(0.15f, 0.15f, 0.16f));
            for (int i = 0; i < Cars; i++)
            {
                Transform car = Kit.Group(transform, "Car " + (i + 1));
                // Local +x runs along the track (the car's length).
                k.Box(car, "Body", new Vector3(0f, 2.2f, 0f), new Vector3(CarLength, 3f, 3f), body, collider: false);
                k.Box(car, "Stripe", new Vector3(0f, 1.4f, 0f), new Vector3(CarLength + 0.02f, 0.5f, 3.02f), stripe, collider: false);
                k.Box(car, "Windows", new Vector3(0f, 2.6f, 0f), new Vector3(CarLength - 1.5f, 1.1f, 3.04f), window, collider: false);
                k.Box(car, "Roof", new Vector3(0f, 3.8f, 0f), new Vector3(CarLength - 0.4f, 0.25f, 2.6f), under, collider: false);
                foreach (float bx in new[] { -CarLength / 2f + 3f, CarLength / 2f - 3f })
                    k.Box(car, "Bogie", new Vector3(bx, 0.45f, 0f), new Vector3(2.6f, 0.7f, 2.2f), under, collider: false);
                var box = car.gameObject.AddComponent<BoxCollider>();
                box.center = new Vector3(0f, 2.2f, 0f);
                box.size = new Vector3(CarLength, 3.6f, 3f);
                var rb = car.gameObject.AddComponent<Rigidbody>();
                rb.isKinematic = true;
                rb.interpolation = RigidbodyInterpolation.Interpolate;
                _cars.Add(car);
            }
            _rumble = _cars[1].gameObject.AddComponent<AudioSource>();
            _rumble.clip = OpeningBell.Gameplay.ProceduralSounds.Noise("train-rumble", 0.015f);
            _rumble.loop = true;
            _rumble.spatialBlend = 1f;
            _rumble.minDistance = 12f;
            _rumble.maxDistance = 220f;
            _rumble.volume = 0f;
            _rumble.Play();
            // Start partway through a pause so the first train shows up soon after loading.
            _timer = HiddenSeconds * 0.4f;
            X = Start;
            Place();
        }

        private void Update()
        {
            if (_c == null) return;
            float dt = _c.Game.IsPaused ? 0f : Time.deltaTime;
            switch (State)
            {
                case Phase.Hidden:
                    _timer -= dt;
                    if (_timer > 0f) break;
                    State = Phase.Running;
                    _stopped = false;
                    _speed = MaxSpeed * 0.8f;
                    X = Start;
                    break;
                case Phase.Dwelling:
                    _timer -= dt;
                    if (_timer <= 0f) State = Phase.Running;
                    break;
                default:
                    float dir = _eastbound ? 1f : -1f;
                    float target = _stopped ? End : StopAt;
                    float remaining = (target - X) * dir;
                    float limit = _stopped ? MaxSpeed : Mathf.Sqrt(2f * Decel * Mathf.Max(0f, remaining));
                    float wanted = Mathf.Min(MaxSpeed, limit);
                    _speed = Mathf.MoveTowards(_speed, wanted, (wanted > _speed ? Accel : Decel * 2f) * dt);
                    X += dir * Mathf.Min(_speed * dt, Mathf.Max(0f, remaining));
                    if (!_stopped && remaining - _speed * dt <= 0.05f && _speed < 0.6f)
                    {
                        X = StopAt;
                        _speed = 0f;
                        _stopped = true;
                        State = Phase.Dwelling;
                        _timer = DwellSeconds;
                    }
                    else if (_stopped && (End - X) * dir <= 0.1f)
                    {
                        State = Phase.Hidden;
                        _timer = HiddenSeconds;
                        _eastbound = !_eastbound;
                    }
                    break;
            }
            _rumble.volume = State == Phase.Running ? Mathf.Clamp01(0.25f + _speed / MaxSpeed) * 0.8f : 0f;
            _rumble.pitch = 0.6f + 0.5f * _speed / MaxSpeed;
            Place();
        }

        /// <summary>Cars follow the head along the track; hidden ones wait inside a tunnel.</summary>
        private void Place()
        {
            float dir = _eastbound ? 1f : -1f;
            float z = CityPlan.RailZ + (_eastbound ? -2.25f : 2.25f);
            for (int i = 0; i < _cars.Count; i++)
            {
                float centre = X - dir * (CarLength / 2f + i * (CarLength + Gap));
                float y = CityPlan.RailDeck(Mathf.Clamp(centre, CityPlan.RailWest, CityPlan.RailEast)) + 0.42f;
                var p = new Vector3(centre, y, z);
                Quaternion r = Quaternion.Euler(0f, _eastbound ? 0f : 180f, 0f);
                Rigidbody rb = _cars[i].GetComponent<Rigidbody>();
                if (State == Phase.Hidden || !Application.isPlaying) _cars[i].SetPositionAndRotation(p, r);
                else
                {
                    rb.MovePosition(p);
                    rb.MoveRotation(r);
                }
            }
        }
    }
}
