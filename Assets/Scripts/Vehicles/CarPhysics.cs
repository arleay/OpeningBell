using System;

namespace OpeningBell.Vehicles
{
    // Serialized by value: append only.
    public enum Drivetrain
    {
        FWD,
        RWD,
        AWD,
    }

    /// <summary>
    /// A car's physical characteristics (spec §18–25, §27, §89). SI units. There is no "top speed": it falls out
    /// of power, drag and gearing.
    /// </summary>
    [Serializable]
    public sealed class CarSpec
    {
        public double Mass = 1300;                 // kg
        public double CenterOfMassHeight = 0.55;   // m above the ground
        public double Wheelbase = 2.6;             // m
        public double TrackWidth = 1.55;           // m
        public double FrontWeight = 0.58;          // static share on the front axle
        public Drivetrain Drive = Drivetrain.FWD;
        public double AwdFrontShare = 0.4;         // torque to the front axle on AWD

        public double IdleRpm = 850;
        public double RedlineRpm = 6500;
        /// <summary>Torque curve: parallel arrays of rpm and N·m, rpm ascending.</summary>
        public double[] CurveRpm = { 1000, 2500, 4000, 5500, 6500 };
        public double[] CurveTorque = { 120, 165, 180, 165, 140 };
        public double[] GearRatios = { 3.5, 2.05, 1.4, 1.0, 0.8 };
        public double ReverseRatio = 3.2;
        public double FinalDrive = 4.1;
        public double DrivetrainEfficiency = 0.88;
        /// <summary>Automatic torque converter: engine rpm it can hold while the car is stopped, and its multiplication there.</summary>
        public double StallRpm = 2200;
        public double StallMultiplier = 1.8;

        public double WheelRadius = 0.32;
        public double TireGrip = 1.0;              // peak friction coefficient on dry asphalt
        /// <summary>Slip angle (rad) at peak cornering force: small for stiff sport tyres, larger for soft ones.</summary>
        public double PeakSlipAngle = 0.14;
        public double BrakeTorque = 3200;          // N·m, all four wheels at full pedal
        public double BrakeFront = 0.65;           // front bias
        public double HandbrakeTorque = 1600;      // N·m, rear only

        public double SpringRate = 32000;          // N/m per wheel
        public double Damping = 3200;              // N·s/m per wheel
        public double RestLength = 0.38;           // m, suspension at rest
        public double Travel = 0.2;                // m of compression available
        public double AntiRoll = 9000;             // N/m across an axle

        public double DragCoefficient = 0.32;
        public double FrontalArea = 2.1;           // m²
        public double DownforceCoefficient = 0;    // N per (m/s)²
        public double MaxSteerDeg = 34;

        public double FuelCapacity = 50;           // litres
        /// <summary>Litres per kWh the engine delivers (~0.3 for a petrol engine at its sweet spot).</summary>
        public double FuelPerKwh = 0.3;
        public double IdleFuelPerHour = 0.8;       // litres/hour ticking over

        public CarSpec Clone()
        {
            var c = (CarSpec)MemberwiseClone();
            c.CurveRpm = (double[])CurveRpm.Clone();
            c.CurveTorque = (double[])CurveTorque.Clone();
            c.GearRatios = (double[])GearRatios.Clone();
            return c;
        }
    }

    /// <summary>
    /// The drivetrain and tyre maths for <see cref="CarSpec"/>, pure so it can be tested headless. The Unity
    /// controller does the rigid body, suspension raycasts and applies the forces these produce.
    /// </summary>
    public static class CarPhysics
    {
        public const double G = 9.81;
        public const double AirDensity = 1.2;
        /// <summary>Rolling-resistance coefficient of a car tyre on asphalt.</summary>
        public const double RollingResistance = 0.012;

        /// <summary>Engine torque at <paramref name="rpm"/> at full throttle (linear through the curve points).</summary>
        public static double Torque(CarSpec s, double rpm)
        {
            double[] r = s.CurveRpm, t = s.CurveTorque;
            if (rpm <= r[0]) return t[0] * Math.Max(0, rpm / r[0]);
            for (int i = 1; i < r.Length; i++)
                if (rpm <= r[i]) return t[i - 1] + (t[i] - t[i - 1]) * (rpm - r[i - 1]) / (r[i] - r[i - 1]);
            return rpm > s.RedlineRpm ? 0 : t[t.Length - 1];
        }

        /// <summary>Peak power (W) across the curve, for spec cards.</summary>
        public static double PeakPower(CarSpec s)
        {
            double best = 0;
            for (double rpm = s.IdleRpm; rpm <= s.RedlineRpm; rpm += 50) best = Math.Max(best, Torque(s, rpm) * rpm * Math.PI / 30);
            return best;
        }

        /// <summary>Wheel rpm → engine rpm through a gear (no converter slip).</summary>
        public static double EngineRpm(CarSpec s, double wheelRpm, double gearRatio) => Math.Abs(wheelRpm * gearRatio * s.FinalDrive);

        /// <summary>
        /// Automatic gearbox: shift up near the top of the useful rev range (earlier when cruising gently), down
        /// when revs fall (or for a kickdown). Returns the new gear index (0 = first).
        /// </summary>
        public static int AutoShift(CarSpec s, int gear, double wheelRpm, double throttle)
        {
            double upAt = s.IdleRpm + (s.RedlineRpm * 0.93 - s.IdleRpm) * (0.35 + 0.65 * throttle);
            double downAt = s.IdleRpm + (1600 + 1800 * throttle) * (s.RedlineRpm / 6500);
            double rpm = EngineRpm(s, wheelRpm, s.GearRatios[gear]);
            if (gear < s.GearRatios.Length - 1 && rpm > upAt) return gear + 1;
            if (gear > 0)
            {
                double below = EngineRpm(s, wheelRpm, s.GearRatios[gear - 1]);
                if (rpm < downAt && below < upAt * 0.95) return gear - 1;
            }
            return gear;
        }

        /// <summary>
        /// Engine speed and the torque reaching the driven wheels (both axles together, N·m), including the
        /// torque converter's slip and multiplication near a standstill, the rev limiter and engine braking.
        /// </summary>
        public static double WheelTorque(CarSpec s, double wheelRpm, double gearRatio, double throttle, out double engineRpm)
        {
            double locked = EngineRpm(s, wheelRpm, gearRatio);
            // Converter: at low road speed the engine can spin up to the stall speed under throttle.
            double slipRpm = s.IdleRpm + (s.StallRpm - s.IdleRpm) * throttle;
            engineRpm = Math.Max(locked, slipRpm);
            engineRpm = Math.Min(engineRpm, s.RedlineRpm + 150);
            double coupling = slipRpm > 0 ? Math.Min(1, locked / slipRpm) : 1;
            double multiply = s.StallMultiplier + (1 - s.StallMultiplier) * coupling;

            double engine = throttle * Torque(s, engineRpm);
            if (engineRpm >= s.RedlineRpm) engine = 0; // limiter
            // Off throttle the engine drags: roughly proportional to revs.
            double drag = (1 - throttle) * 0.018 * Torque(s, s.CurveRpm[s.CurveRpm.Length / 2]) * engineRpm / 1000 * coupling;
            return (engine * multiply - drag) * gearRatio * s.FinalDrive * s.DrivetrainEfficiency;
        }

        /// <summary>Share of drive torque going to the front axle.</summary>
        public static double FrontDriveShare(CarSpec s) => s.Drive == Drivetrain.FWD ? 1 : s.Drive == Drivetrain.RWD ? 0 : s.AwdFrontShare;

        /// <summary>
        /// Normalised cornering force for a slip angle: rises linearly, peaks at <see cref="CarSpec.PeakSlipAngle"/>,
        /// then falls off to ~80% as the tyre slides (a simplified "magic formula"). Sign opposes the slip.
        /// </summary>
        public static double LateralCurve(double slipAngle, double peakSlip)
        {
            const double c = 1.4;
            double b = Math.Tan(Math.PI / (2 * c)) / peakSlip;
            return Math.Sin(c * Math.Atan(b * slipAngle));
        }

        /// <summary>
        /// Friction-circle limit: the tyre can only give <paramref name="grip"/> × load in total. Returns the
        /// scale applied to both force components (1 = within grip). A spinning or locked wheel eats into
        /// cornering grip, which is what turns power into oversteer and brakes into understeer.
        /// </summary>
        public static double FrictionCircle(double longitudinal, double lateral, double grip, double load)
        {
            double limit = grip * Math.Max(0, load);
            double total = Math.Sqrt(longitudinal * longitudinal + lateral * lateral);
            return total > limit && total > 1e-6 ? limit / total : 1;
        }

        public static double Drag(CarSpec s, double speed) => 0.5 * AirDensity * s.DragCoefficient * s.FrontalArea * speed * speed;

        /// <summary>Fuel used (litres) delivering <paramref name="enginePowerW"/> for <paramref name="gameSeconds"/>, plus idle.</summary>
        public static double Fuel(CarSpec s, double enginePowerW, double gameSeconds) =>
            (Math.Max(0, enginePowerW) / 1000 * s.FuelPerKwh + s.IdleFuelPerHour) * gameSeconds / 3600;
    }
}
