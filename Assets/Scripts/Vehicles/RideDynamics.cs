using System;

namespace OpeningBell.Vehicles
{
    public struct RideInput
    {
        /// <summary>0–1: pedalling effort, or "keep pushing" on a skateboard.</summary>
        public double Throttle;
        public double Brake;
        /// <summary>-1 (left) … 1 (right).</summary>
        public double Steer;
        public bool Sprint;
    }

    public struct RideGround
    {
        /// <summary>Rise over run along the direction of travel (0.06 = 6% uphill).</summary>
        public double Grade;
        /// <summary>0 polished floor · 0.2 sidewalk · 0.3 asphalt · 1 grass.</summary>
        public double Roughness;
    }

    /// <summary>Continuous state while riding. The owned vehicle keeps the durable parts (battery, wear).</summary>
    public sealed class RideState
    {
        public double Speed;          // m/s, forward only
        public double Heading;        // radians, compass (0 = +z/north, clockwise positive)
        public double Lean;           // radians, bikes (visual + turn rate)
        public double YawRate;        // radians/s
        public double CrankAngle;     // radians, for the pedalling animation
        public double PushTimer;      // skateboards: time until the next push
        public bool Pushing;          // a push happened this step (animation)
        public double Wobble;         // 0–1 speed wobble on a skateboard
        public bool Bailed;           // fell off (wobble too strong)
        public int Assist = 2;        // e-bike assist level index
        public double MotorPower;     // W delivered this step
        public double Distance;       // metres this ride
    }

    /// <summary>
    /// Physically based ride model (spec §29–31). Forces, not speed stats:
    /// <list type="bullet">
    /// <item>Bikes: rider power, limited at low speed by crank torque through the lowest gear and at high speed
    /// by spinning out the highest gear; rolling resistance (worse on rough ground, worn tyres); air drag;
    /// gravity on slopes; brakes. Turning rate follows lean (g·tan(lean)/v), capped by the steering angle at
    /// low speed. E-bikes add a motor that multiplies rider effort per assist level up to a cutoff speed and
    /// drains the battery by game time.</item>
    /// <item>Skateboards: discrete pushes that lose effect near kicking speed; rolling resistance from wheel size,
    /// hardness vs ground roughness and bearings; carving limited by truck looseness and wheel grip; speed
    /// wobbles above a stability speed set by deck length and trucks, then a bail.</item>
    /// </list>
    /// Pure and deterministic; the Unity controller supplies grade and roughness from the ground under it.
    /// </summary>
    public static class RideDynamics
    {
        public const double G = 9.81;
        public const double AirDensity = 1.2;
        public const double RiderMass = 75;
        public const double CruisePower = 230;   // W, an easy sustained effort
        public const double SprintPower = 550;
        public const double CrankTorque = 90;    // N·m, hard push on the pedals
        public const double SpinOutCadence = 100; // rpm where the top gear runs out
        public const double MotorEfficiency = 0.85;

        /// <summary>E-bike assist multipliers of rider power: Off, Eco, Tour, Turbo.</summary>
        public static readonly double[] AssistRatio = { 0, 0.6, 1.3, 2.6 };
        public static readonly string[] AssistNames = { "OFF", "ECO", "TOUR", "TURBO" };

        /// <param name="gameSecondsPerSecond">Game-clock speed: battery energy follows game time, like the world.</param>
        /// <param name="battery">E-bike battery (Wh), updated in place.</param>
        public static void Step(VehicleKind kind, RideSpec spec, RideState s, RideInput input, RideGround ground, double dt,
            double gameSecondsPerSecond, double condition, double tireCondition, ref double battery)
        {
            s.Pushing = false;
            s.MotorPower = 0;
            if (kind == VehicleKind.Skateboard) StepSkateboard(spec, s, input, ground, dt, tireCondition);
            else StepBike(kind, spec, s, input, ground, dt, gameSecondsPerSecond, condition, tireCondition, ref battery);
            s.Distance += s.Speed * dt;
        }

        private static void StepBike(VehicleKind kind, RideSpec spec, RideState s, RideInput input, RideGround ground, double dt,
            double gameSpeed, double condition, double tireCondition, ref double battery)
        {
            double mass = spec.Mass + RiderMass;
            double v = s.Speed;
            double throttle = Clamp01(input.Throttle);

            // Rider: power at the pedals, a worn drivetrain wastes some of it.
            double power = throttle * (input.Sprint ? SprintPower : CruisePower) * (0.7 + 0.3 * Clamp01(condition));
            double spinOut = SpinOutCadence / 60.0 * Math.PI * spec.WheelDiameter * spec.HighestGear;
            if (v > spinOut) power *= Math.Max(0, 1 - (v - spinOut) / (0.4 * spinOut));
            double maxForce = throttle * CrankTorque / spec.LowestGear / (spec.WheelDiameter / 2);
            double rider = Math.Min(maxForce, power / Math.Max(v, 0.5));

            // Motor (e-bikes): a multiple of rider effort, fading out at the cutoff, while there is charge.
            double motor = 0;
            if (kind == VehicleKind.EBike && spec.MotorPower > 0 && battery > 0 && throttle > 0)
            {
                double fade = Clamp01((spec.AssistCutoff + 0.5 - v) / 1.5);
                double motorPower = Math.Min(spec.MotorPower, AssistRatio[Math.Max(0, Math.Min(AssistRatio.Length - 1, s.Assist))] * power) * fade;
                motor = Math.Min(motorPower / Math.Max(v, 1.0), 110);
                s.MotorPower = motor * Math.Max(v, 1.0);
                battery = Math.Max(0, battery - s.MotorPower / MotorEfficiency * dt * gameSpeed / 3600.0);
            }

            // Pavement of any kind rolls about the same on bike tyres; soft ground (grass) adds drag that slicks
            // sink into and knobbly tyres shrug off. Worn tyres roll worse.
            double soft = Math.Max(0, (ground.Roughness - 0.4) / 0.6);
            double crr = (spec.RollingResistance + soft * spec.RoughnessPenalty * 0.025) * (1 + (1 - Clamp01(tireCondition)));
            double cos = 1 / Math.Sqrt(1 + ground.Grade * ground.Grade), sin = ground.Grade * cos;
            double resist = crr * mass * G * cos + 0.5 * AirDensity * spec.DragArea * v * v + mass * G * sin;
            double brake = Clamp01(input.Brake) * spec.BrakeDecel * (0.6 + 0.4 * Clamp01(tireCondition)) * mass;
            if (kind == VehicleKind.EBike && brake > 0 && v > 2) battery += 0.1 * brake * v * dt * gameSpeed / 3600.0; // light regen

            double accel = (rider + motor - resist - brake) / (mass * 1.04);
            s.Speed = Math.Max(0, v + accel * dt);

            // Lean follows steering; turn rate from lean at speed, from the handlebar angle when slow.
            double targetLean = Clamp(input.Steer, -1, 1) * spec.MaxLeanDeg * Math.PI / 180;
            s.Lean += Clamp(targetLean - s.Lean, -3 * dt, 3 * dt);
            s.YawRate = BikeYawRate(spec, s.Speed, s.Lean);
            s.Heading = Wrap(s.Heading + s.YawRate * dt);

            // Automatic gear choice keeps cadence near 80 rpm; the crank only turns while pedalling.
            double ratio = Clamp(s.Speed * 60 / (Math.PI * spec.WheelDiameter * 80), spec.LowestGear, spec.HighestGear);
            if (throttle > 0) s.CrankAngle = Wrap(s.CrankAngle + s.Speed / (Math.PI * spec.WheelDiameter * ratio) * 2 * Math.PI * dt);
        }

        public static double BikeYawRate(RideSpec spec, double v, double lean)
        {
            if (v < 0.2 || Math.Abs(lean) < 1e-4) return 0;
            double fromLean = G * Math.Tan(Math.Abs(lean)) / v;
            double fromBars = v * Math.Tan(spec.MaxSteerDeg * Math.PI / 180) / spec.Wheelbase;
            return Math.Sign(lean) * Math.Min(fromLean, fromBars);
        }

        private static void StepSkateboard(RideSpec spec, RideState s, RideInput input, RideGround ground, double dt, double wheelCondition)
        {
            double mass = spec.Mass + RiderMass;
            double v = s.Speed;
            bool grass = ground.Roughness >= 0.9;

            // Pushing: a kick every ~0.85 s while held, less useful as you approach kicking speed.
            s.PushTimer -= dt;
            if (input.Throttle > 0.1 && s.PushTimer <= 0 && input.Brake < 0.1)
            {
                double kick = (input.Sprint ? 1.25 : 0.9) * Math.Max(0, 1 - v / 6.5) * (grass ? 0.3 : 1);
                v += kick;
                s.PushTimer = 0.85;
                s.Pushing = true;
            }

            // Rolling: small hard wheels are quick on smooth floors, soft big ones soak up rough pavement.
            double hardness = Clamp01((spec.WheelDurometer - 75) / 25);
            double crr = grass
                ? 0.25
                : 0.009 * (0.054 / spec.WheelDiameter) * (1 + ground.Roughness * (1.8 * hardness + 0.4)) * spec.BearingFactor *
                  (1 + 0.5 * (1 - Clamp01(wheelCondition)));
            double cos = 1 / Math.Sqrt(1 + ground.Grade * ground.Grade), sin = ground.Grade * cos;
            double resist = crr * mass * G * cos + 0.5 * AirDensity * 0.5 * v * v + mass * G * sin;
            double brake = Clamp01(input.Brake) * 2.2 * mass; // foot brake

            // Carving: loose trucks turn tighter; wheel grip limits the turn at speed; carving bleeds speed.
            double maxYaw = 0.8 + 0.8 * Clamp01(spec.TruckLooseness);
            double grip = 4 + 2 * (1 - hardness);
            double yaw = Clamp(input.Steer, -1, 1) * maxYaw;
            if (v > 0.1) yaw = Clamp(yaw, -grip / v, grip / v);
            if (v < 0.3) yaw *= v / 0.3;
            double carveDrag = 0.05 * Math.Abs(yaw) * v * mass;

            double accel = (-resist - brake - carveDrag) / mass;
            s.Speed = Math.Max(0, v + accel * dt);
            s.YawRate = yaw;
            s.Heading = Wrap(s.Heading + yaw * dt);

            // Stability: a longer deck and tighter trucks stay calm to higher speeds.
            double stable = StableSpeed(spec);
            s.Wobble = Clamp01((s.Speed - stable) / (0.35 * stable));
            if (s.Wobble >= 1)
            {
                s.Bailed = true;
                s.Speed = 0;
            }
        }

        public static double StableSpeed(RideSpec spec) => 3.5 + 7 * spec.DeckLength + 2 * (1 - Clamp01(spec.TruckLooseness));

        private static double Clamp01(double x) => x < 0 ? 0 : x > 1 ? 1 : x;
        private static double Clamp(double x, double lo, double hi) => x < lo ? lo : x > hi ? hi : x;

        private static double Wrap(double a)
        {
            const double tau = 2 * Math.PI;
            a %= tau;
            return a < 0 ? a + tau : a;
        }
    }
}
