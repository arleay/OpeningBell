using System;
using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Car physics on a rigid body (spec §19–26): four raycast wheels with spring/damper suspension and anti-roll
    /// bars, tyre forces from slip angle (cornering) and drive/brake torque (longitudinal) limited together by a
    /// friction circle on each wheel's actual load, an engine with a torque curve through an automatic gearbox
    /// and torque converter, FWD/RWD/AWD torque split, aerodynamic drag and downforce. Weight transfer, body
    /// roll, understeer, oversteer and wheelspin emerge from these; nothing sets a speed directly.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class CarController : MonoBehaviour
    {
        public sealed class Wheel
        {
            public Transform Visual;
            public Vector3 Mount;      // local: suspension top (wheel centre at full extension + rest length)
            public bool Front, Left;
            public float Radius;
            public float Compression, LastCompression;
            public bool Grounded;
            public float Load;
            public float SlipAngle, SlipRatio;
            public double Spin;        // radians, visual
            public float Roughness;
        }

        private Rigidbody _rb;
        private CarSpec _spec;
        private Wheel[] _wheels;
        private float _steer;
        private int _gear;
        private float _shiftTimer;
        private bool _reverse;
        private readonly RaycastHit[] _hits = new RaycastHit[4];

        public CarSpec Spec => _spec;
        public Rigidbody Body => _rb;
        public Wheel[] Wheels => _wheels;

        // Inputs (set by the driver each frame).
        public float Throttle { get; set; }
        public float Brake { get; set; }
        public float Steer { get; set; }
        public bool Handbrake { get; set; }
        /// <summary>No fuel, or nobody at the wheel: the engine gives nothing.</summary>
        public bool EngineOn { get; set; }
        /// <summary>0–1: frame/engine condition; a battered engine makes less power.</summary>
        public float Condition { get; set; } = 1f;
        public float TireCondition { get; set; } = 1f;
        /// <summary>Wet roads grip less (set by the weather: 1 dry, ~0.82 soaked).</summary>
        public static float WeatherGrip { get; set; } = 1f;

        // Telemetry.
        public float SpeedKmh => ForwardSpeed * 3.6f;
        public float ForwardSpeed { get; private set; }
        public float EngineRpm { get; private set; }
        public int Gear => _gear;
        public bool InReverse => _reverse;
        public bool Shifting => _shiftTimer > 0f;
        /// <summary>Power the engine delivered last step (W), for fuel use.</summary>
        public float EnginePower { get; private set; }
        public bool Wheelspin { get; private set; }
        /// <summary>Impact speeds of collisions (m/s along the contact normal), for damage.</summary>
        public event Action<float, Collision> Impact;

        public void Configure(CarSpec spec, Wheel[] wheels)
        {
            _spec = spec;
            _wheels = wheels;
            _rb = GetComponent<Rigidbody>();
            _rb.mass = (float)spec.Mass;
            _rb.linearDamping = 0f;
            _rb.angularDamping = 0.6f;
            _rb.interpolation = RigidbodyInterpolation.Interpolate;
            _rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // Centre of mass: height from the spec, fore-aft placed so the static front share matches.
            float front = 0f, rear = 0f;
            foreach (Wheel w in wheels)
                if (w.Front) front = w.Mount.z;
                else rear = w.Mount.z;
            float z = Mathf.Lerp(rear, front, (float)spec.FrontWeight);
            _rb.centerOfMass = new Vector3(0f, (float)spec.CenterOfMassHeight, z);
        }

        /// <summary>Parked: frozen in place (kinematic). Driving: a free rigid body.</summary>
        public void SetParked(bool parked)
        {
            // A parked car may have been placed by moving its transform; make the body start from there.
            _rb.position = transform.position;
            _rb.rotation = transform.rotation;
            _rb.isKinematic = parked;
            if (parked) return;
            _rb.linearVelocity = Vector3.zero;
            _rb.angularVelocity = Vector3.zero;
            _rb.WakeUp();
        }

        private void FixedUpdate()
        {
            if (_rb.isKinematic || _spec == null) return;
            float dt = Time.fixedDeltaTime;
            Vector3 velocity = _rb.linearVelocity;
            ForwardSpeed = Vector3.Dot(velocity, transform.forward);

            // Speed-sensitive steering: full lock when parking, much less at speed.
            float lockDeg = (float)_spec.MaxSteerDeg * Mathf.Lerp(1f, 0.32f, Mathf.Clamp01(Mathf.Abs(ForwardSpeed) / 30f));
            _steer = Mathf.MoveTowards(_steer, Steer * lockDeg, 120f * dt);

            // Reverse: holding brake at a standstill selects reverse; then the brake pedal drives backwards.
            if (!_reverse && Brake > 0.1f && Throttle < 0.1f && ForwardSpeed < 0.6f) _reverse = true;
            if (_reverse && Throttle > 0.1f && ForwardSpeed > -0.6f) _reverse = false;
            float drivePedal = _reverse ? Brake : Throttle;
            float brakePedal = _reverse ? Throttle : Brake;

            // Drivetrain: wheel speed → engine through the current gear (automatic).
            double wheelRpm = ForwardSpeed / (2 * Math.PI * _spec.WheelRadius) * 60;
            if (_shiftTimer > 0f) _shiftTimer -= dt;
            else if (!_reverse)
            {
                int next = CarPhysics.AutoShift(_spec, _gear, Math.Abs(wheelRpm), drivePedal);
                if (next != _gear)
                {
                    _gear = next;
                    _shiftTimer = 0.25f;
                }
            }
            else _gear = 0;
            double ratio = _reverse ? -_spec.ReverseRatio : _spec.GearRatios[_gear];
            double throttle = EngineOn && _shiftTimer <= 0f ? drivePedal : 0;
            double wheelTorque = CarPhysics.WheelTorque(_spec, Math.Abs(wheelRpm), Math.Abs(ratio), throttle, out double engineRpm);
            if (!EngineOn) engineRpm = 0;
            wheelTorque *= 0.6 + 0.4 * Condition;
            if (_reverse) wheelTorque = -wheelTorque;
            EngineRpm = (float)engineRpm;
            EnginePower = (float)Math.Max(0, throttle * CarPhysics.Torque(_spec, engineRpm) * engineRpm * Math.PI / 30);
            double frontShare = CarPhysics.FrontDriveShare(_spec);

            // Suspension, anti-roll and tyres, wheel by wheel.
            float grip = (float)_spec.TireGrip * (0.75f + 0.25f * TireCondition) * WeatherGrip;
            Wheelspin = false;
            for (int axle = 0; axle < 2; axle++)
            {
                Wheel l = null, r = null;
                foreach (Wheel w in _wheels)
                    if (w.Front == (axle == 0))
                    {
                        if (w.Left) l = w;
                        else r = w;
                    }
                Suspend(l, dt);
                Suspend(r, dt);
                // Anti-roll bar: the more compressed side pushes the body up, the other side pulls it down.
                float roll = (l.Compression - r.Compression) * (float)_spec.AntiRoll;
                if (l.Grounded)
                {
                    l.Load += roll;
                    _rb.AddForceAtPosition(transform.up * roll, transform.TransformPoint(l.Mount));
                }
                if (r.Grounded)
                {
                    r.Load -= roll;
                    _rb.AddForceAtPosition(-transform.up * roll, transform.TransformPoint(r.Mount));
                }
            }
            foreach (Wheel w in _wheels)
            {
                double share = w.Front ? frontShare : 1 - frontShare;
                double drive = wheelTorque * share / 2; // open differential: equal torque left/right
                double brake = brakePedal * _spec.BrakeTorque * (w.Front ? _spec.BrakeFront : 1 - _spec.BrakeFront) / 2;
                if (Handbrake && !w.Front) brake += _spec.HandbrakeTorque / 2;
                Tyre(w, drive, brake, grip, dt);
            }

            // Air: drag against the motion, downforce pressing the tyres into the road.
            float speed = velocity.magnitude;
            if (speed > 0.1f) _rb.AddForce(-velocity / speed * (float)CarPhysics.Drag(_spec, speed));
            if (_spec.DownforceCoefficient > 0) _rb.AddForce(-transform.up * (float)(_spec.DownforceCoefficient * speed * speed));
        }

        private void Suspend(Wheel w, float dt)
        {
            Vector3 origin = transform.TransformPoint(w.Mount);
            Vector3 down = -transform.up;
            float reach = (float)_spec.RestLength + w.Radius;
            w.Grounded = false;
            w.Load = 0f;
            w.LastCompression = w.Compression;
            int n = Physics.RaycastNonAlloc(origin, down, _hits, reach, ~0, QueryTriggerInteraction.Ignore);
            float best = float.MaxValue;
            RaycastHit hit = default;
            for (int i = 0; i < n; i++)
                if (_hits[i].rigidbody != _rb && _hits[i].distance < best && !_hits[i].collider.transform.IsChildOf(transform))
                {
                    best = _hits[i].distance;
                    hit = _hits[i];
                }
            if (best == float.MaxValue)
            {
                w.Compression = 0f;
                w.Visual.localPosition = w.Mount + Vector3.down * (float)_spec.RestLength;
                return;
            }
            w.Grounded = true;
            w.Compression = Mathf.Clamp((float)_spec.RestLength - (best - w.Radius), 0f, (float)_spec.RestLength);
            float rate = (w.Compression - w.LastCompression) / dt;
            float spring = w.Compression * (float)_spec.SpringRate;
            // Bump stop: past the travel the spring gets very stiff instead of bottoming through.
            float over = w.Compression - (float)_spec.Travel;
            if (over > 0f) spring += over * (float)_spec.SpringRate * 8f;
            w.Load = Mathf.Max(0f, spring + rate * (float)_spec.Damping);
            SurfaceTag surface = hit.collider.GetComponentInParent<SurfaceTag>();
            w.Roughness = surface != null ? surface.Roughness : SurfaceTag.Untagged;
            _rb.AddForceAtPosition(transform.up * w.Load, origin);
            w.Visual.localPosition = w.Mount + Vector3.down * ((float)_spec.RestLength - w.Compression);
            _contact[Array.IndexOf(_wheels, w)] = hit;
        }

        private readonly RaycastHit[] _contact = new RaycastHit[4];

        private void Tyre(Wheel w, double driveTorque, double brakeTorque, float grip, float dt)
        {
            int index = Array.IndexOf(_wheels, w);
            Quaternion steer = Quaternion.AngleAxis(w.Front ? _steer : 0f, transform.up);
            Vector3 forward = steer * transform.forward;
            if (!w.Grounded)
            {
                w.Spin += driveTorque * dt * 0.02; // free-spinning in the air (visual)
                w.Visual.localRotation = Quaternion.Euler((float)(w.Spin * Mathf.Rad2Deg), w.Front ? _steer : 0f, 0f);
                return;
            }
            RaycastHit hit = _contact[index];
            Vector3 normal = hit.normal;
            forward = Vector3.ProjectOnPlane(forward, normal).normalized;
            Vector3 side = Vector3.Cross(normal, forward);
            Vector3 v = _rb.GetPointVelocity(hit.point);
            float vf = Vector3.Dot(v, forward), vs = Vector3.Dot(v, side);
            // Grass and gravel hold less than asphalt.
            float mu = grip * (w.Roughness >= 0.9f ? 0.6f : 1f);
            float load = w.Load;
            float wheelMass = _rb.mass / 4f;

            // Cornering: slip angle through the tyre curve; at a crawl, just resist sideways sliding.
            float lateral;
            if (Mathf.Abs(vf) > 2.5f)
            {
                w.SlipAngle = Mathf.Atan2(vs, Mathf.Abs(vf));
                lateral = -mu * load * (float)CarPhysics.LateralCurve(w.SlipAngle, _spec.PeakSlipAngle);
            }
            else
            {
                w.SlipAngle = 0f;
                lateral = Mathf.Clamp(-vs * wheelMass / dt * 0.6f, -mu * load, mu * load);
            }
            if (Handbrake && !w.Front) lateral *= 0.45f;

            // Longitudinal: drive and brakes at the contact patch, rolling resistance.
            float longitudinal = (float)(driveTorque / _spec.WheelRadius);
            float brakeForce = (float)(brakeTorque / _spec.WheelRadius);
            if (Mathf.Abs(vf) > 0.4f) longitudinal -= Mathf.Sign(vf) * brakeForce;
            else longitudinal -= Mathf.Clamp(vf * wheelMass / dt, -brakeForce, brakeForce); // hold still
            longitudinal -= Mathf.Sign(vf) * (float)CarPhysics.RollingResistance * load * (1f + 2f * w.Roughness);

            // Friction circle: total grip is shared. Too much drive = wheelspin, which costs cornering grip.
            float scale = (float)CarPhysics.FrictionCircle(longitudinal, lateral, mu, load);
            if (scale < 0.98f && Mathf.Abs((float)(driveTorque / _spec.WheelRadius)) > mu * load * 0.9f)
            {
                Wheelspin = true;
                w.SlipRatio = 1f - scale;
            }
            else w.SlipRatio = 0f;
            longitudinal *= scale;
            lateral *= scale;

            // Tyre forces act a little above the contact patch: a low roll centre gives visible body roll without
            // the tip-over tendency of forces applied right at the ground.
            Vector3 at = hit.point + normal * (w.Radius * 0.3f);
            _rb.AddForceAtPosition(forward * longitudinal + side * lateral, at);

            w.Spin += (vf / w.Radius + (Wheelspin && driveTorque != 0 ? Math.Sign(driveTorque) * 25 * w.SlipRatio : 0)) * dt;
            w.Visual.localRotation = Quaternion.Euler((float)(w.Spin * Mathf.Rad2Deg), w.Front ? _steer : 0f, 0f);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;
            float impact = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, collision.GetContact(0).normal));
            Impact?.Invoke(impact, collision);
        }
    }
}
