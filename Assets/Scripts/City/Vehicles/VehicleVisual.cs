using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// TODO(art): primitive bike / e-bike built from a model's real dimensions (wheel size,
    /// wheelbase) until vehicle art exists. Origin on the ground between the wheels, facing +z.
    /// Spins wheels by distance travelled, turns the cranks and leans the frame.
    /// </summary>
    public sealed class VehicleVisual : MonoBehaviour
    {
        private Transform _frame, _crank;
        private Transform[] _wheels;
        private float _wheelRadius;
        private double _wheelAngle;

        public float SaddleHeight { get; private set; }
        public float Length { get; private set; }

        public static VehicleVisual Build(Kit kit, Transform parent, VehicleModel model)
        {
            var root = new GameObject(model.Name);
            root.transform.SetParent(parent, false);
            var v = root.AddComponent<VehicleVisual>();
            v._frame = Kit.Group(root.transform, "Frame");
            Color paint = new Color(model.ColorR, model.ColorG, model.ColorB);
            v.BuildBike(kit, model, paint);
            return v;
        }

        private void BuildBike(Kit kit, VehicleModel model, Color paint)
        {
            RideSpec s = model.Spec;
            float r = (float)s.WheelDiameter / 2f, half = (float)s.Wheelbase / 2f;
            float scale = Mathf.Clamp(r / 0.35f, 0.75f, 1.1f);
            Material frame = kit.P.Lit(paint, 0.45f);
            Material dark = kit.P.Lit(new Color(0.08f, 0.08f, 0.09f), 0.3f);
            Material metal = kit.P.Lit(new Color(0.7f, 0.7f, 0.72f), 0.7f);

            var rearAxle = new Vector3(0f, r, -half);
            var frontAxle = new Vector3(0f, r, half);
            var bb = new Vector3(0f, r - 0.04f, -0.06f);
            var seatTop = new Vector3(0f, r + 0.52f * scale, -0.2f * scale);
            var headTop = new Vector3(0f, r + 0.55f * scale, half - 0.2f);
            var headBottom = new Vector3(0f, r + 0.36f * scale, half - 0.14f);
            Tube(kit, "Down tube", bb, headBottom, 0.045f, frame);
            Tube(kit, "Top tube", seatTop, headTop, 0.035f, frame);
            Tube(kit, "Seat tube", bb, seatTop, 0.04f, frame);
            Tube(kit, "Chain stay", bb, rearAxle, 0.025f, frame);
            Tube(kit, "Seat stay", seatTop + new Vector3(0f, -0.05f, 0f), rearAxle, 0.022f, frame);
            Tube(kit, "Fork", headBottom, frontAxle, 0.03f, frame);
            Tube(kit, "Head tube", headBottom, headTop, 0.05f, frame);
            Vector3 stem = headTop + new Vector3(0f, 0.08f, 0.04f);
            Tube(kit, "Stem", headTop, stem, 0.03f, metal);
            float bars = model.Spec.RoughnessPenalty < 1 ? 0.72f : model.Spec.DragArea < 0.35 ? 0.42f : 0.6f;
            kit.Box(_frame, "Bars", stem, new Vector3(bars, 0.025f, 0.025f), dark, collider: false);
            SaddleHeight = seatTop.y + 0.06f;
            kit.Box(_frame, "Saddle", new Vector3(0f, SaddleHeight - 0.02f, seatTop.z - 0.02f), new Vector3(0.14f, 0.04f, 0.26f), dark, collider: false);
            if (model.Kind == VehicleKind.EBike)
            {
                Vector3 mid = Vector3.Lerp(bb, headBottom, 0.45f);
                GameObject pack = kit.Box(_frame, "Battery", mid + new Vector3(0f, 0.03f, 0f), new Vector3(0.09f, 0.09f, 0.34f), kit.P.Lit(new Color(0.12f, 0.12f, 0.13f), 0.5f), collider: false);
                pack.transform.localRotation = Quaternion.LookRotation(headBottom - bb);
                kit.Cylinder(_frame, "Hub motor", rearAxle, 0.16f, 0.07f, dark).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
            }

            _crank = Kit.Group(_frame, "Crank", bb);
            kit.Box(_crank, "Arm", Vector3.zero, new Vector3(0.02f, 0.34f, 0.03f), metal, collider: false);
            kit.Box(_crank, "Pedal L", new Vector3(-0.08f, 0.17f, 0f), new Vector3(0.1f, 0.02f, 0.06f), dark, collider: false);
            kit.Box(_crank, "Pedal R", new Vector3(0.08f, -0.17f, 0f), new Vector3(0.1f, 0.02f, 0.06f), dark, collider: false);

            _wheels = new Transform[2];
            int i = 0;
            foreach (Vector3 axle in new[] { rearAxle, frontAxle })
            {
                Transform w = Kit.Group(_frame, "Wheel", axle);
                kit.Cylinder(w, "Tyre", Vector3.zero, r * 2f, 0.045f, dark).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                kit.Cylinder(w, "Rim", Vector3.zero, r * 1.7f, 0.05f, metal).transform.localRotation = Quaternion.Euler(0f, 0f, 90f);
                kit.Box(w, "Spoke", Vector3.zero, new Vector3(0.012f, r * 1.65f, 0.012f), dark, collider: false);
                _wheels[i++] = w;
            }
            _wheelRadius = r;
            Length = (float)s.Wheelbase + r * 2f;
        }

        private void Tube(Kit kit, string name, Vector3 a, Vector3 b, float thickness, Material m)
        {
            GameObject t = kit.Box(_frame, name, (a + b) / 2f, new Vector3(thickness, thickness, Vector3.Distance(a, b)), m, collider: false);
            t.transform.localRotation = Quaternion.LookRotation(b - a);
        }

        /// <param name="distance">Metres rolled since the last call.</param>
        /// <param name="lean">Radians, positive = leaning right.</param>
        public void Animate(double distance, double crankAngle, double lean)
        {
            _wheelAngle += distance / _wheelRadius;
            var spin = Quaternion.Euler((float)(_wheelAngle * Mathf.Rad2Deg), 0f, 0f);
            foreach (Transform w in _wheels) w.localRotation = spin;
            if (_crank != null) _crank.localRotation = Quaternion.Euler((float)(crankAngle * Mathf.Rad2Deg), 0f, 0f);
            _frame.localRotation = Quaternion.Euler(0f, 0f, (float)(-lean * Mathf.Rad2Deg));
        }
    }
}
