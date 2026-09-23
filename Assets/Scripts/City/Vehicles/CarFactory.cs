using OpeningBell.Vehicles;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Builds cars from the Kenney car kit models (CC0): drivable ones (rigid body, <see cref="CarController"/>,
    /// seat, headlights) and traffic ones (kinematic, wheels that spin). The kit's chunky proportions are scaled
    /// up to sit right next to 1.75 m people and 5 m lanes.
    /// </summary>
    public static class CarFactory
    {
        public const float Scale = 1.35f;
        public static readonly string[] WheelNames = { "wheel-front-left", "wheel-front-right", "wheel-back-left", "wheel-back-right" };

        private static PhysicsMaterial _slippery;

        /// <summary>
        /// The model under a new root (facing +z, wheels on the ground at y 0). Wheels are lifted out onto pivots
        /// at their centres so they can spin, steer and move with the suspension.
        /// </summary>
        public static GameObject Model(Transform parent, GameObject prefab, string name, out Transform[] wheels, out float wheelRadius, out Bounds body)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.position = new Vector3(0f, -100f, 0f); // never flash at the origin (inside the apartment)
            GameObject model = Object.Instantiate(prefab, root.transform);
            model.name = "Model";
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.identity;
            model.transform.localScale = Vector3.one * Scale;

            wheels = new Transform[WheelNames.Length];
            wheelRadius = 0.4f;
            for (int i = 0; i < WheelNames.Length; i++)
            {
                Transform mesh = Find(model.transform, WheelNames[i]);
                var pivot = new GameObject(WheelNames[i]).transform;
                pivot.SetParent(root.transform, false);
                if (mesh != null && mesh.TryGetComponent(out Renderer r))
                {
                    pivot.position = r.bounds.center;
                    wheelRadius = r.bounds.size.y / 2f;
                    mesh.SetParent(pivot, true);
                }
                wheels[i] = pivot;
            }

            Transform bodyMesh = Find(model.transform, "body");
            Bounds b = bodyMesh != null && bodyMesh.TryGetComponent(out Renderer br) ? br.bounds : new Bounds(root.transform.position + Vector3.up, new Vector3(2f, 1.5f, 4f));
            body = new Bounds(root.transform.InverseTransformPoint(b.center), b.size);
            return root;
        }

        private static readonly string[] NotPaint = { "glass", "tire", "tyre", "wheel", "rim", "chrome", "light", "lamp", "black", "interior", "seat", "rubber", "mirror", "plate", "logo", "grill", "carbon", "brake", "exhaust", "trim" };

        /// <summary>
        /// A respray: every body material that isn't glass, rubber, chrome, lights or trim takes the colour (the
        /// model's texture detail is kept underneath). Materials are copied, so other cars keep theirs.
        /// </summary>
        public static void Paint(GameObject car, Color colour)
        {
            Transform body = Find(car.transform, "body");
            if (body == null || !body.TryGetComponent(out Renderer r)) return;
            Material[] mats = r.sharedMaterials;
            for (int i = 0; i < mats.Length; i++)
            {
                if (mats[i] == null) continue;
                string n = mats[i].name.ToLowerInvariant();
                if (System.Array.Exists(NotPaint, x => n.Contains(x))) continue;
                mats[i] = new Material(mats[i]) { name = mats[i].name + " (paint)" };
                if (mats[i].HasProperty("_BaseColor")) mats[i].SetColor("_BaseColor", colour);
                else mats[i].color = colour;
            }
            r.sharedMaterials = mats;
        }

        /// <summary>Window tint: every glass material swapped for a darker one.</summary>
        public static void Tint(GameObject car, Material dark)
        {
            foreach (Renderer r in car.GetComponentsInChildren<Renderer>())
            {
                Material[] mats = r.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < mats.Length; i++)
                    if (mats[i] != null && mats[i].name.ToLowerInvariant().Contains("glass"))
                    {
                        mats[i] = dark;
                        changed = true;
                    }
                if (changed) r.sharedMaterials = mats;
            }
        }

        public static CarController BuildDrivable(Transform parent, GameObject prefab, CarSpec spec, string name)
        {
            GameObject root = Model(parent, prefab, name, out Transform[] wheels, out float radius, out Bounds body);
            root.layer = CityLayers.Vehicle;

            // Body collider: off the ground (the suspension carries the car), low friction so scrapes slide.
            var box = root.AddComponent<BoxCollider>();
            float bottom = body.min.y + 0.12f, top = body.max.y - 0.05f;
            box.center = new Vector3(body.center.x, (bottom + top) / 2f, body.center.z);
            box.size = new Vector3(body.size.x * 0.97f, top - bottom, body.size.z * 0.98f);
            box.sharedMaterial = Slippery();

            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            var car = root.AddComponent<CarController>();
            var ws = new CarController.Wheel[4];
            for (int i = 0; i < 4; i++)
            {
                bool front = i < 2, left = i % 2 == 0;
                Vector3 centre = wheels[i].localPosition;
                // Mount the spring so the wheel sits where the model has it once the car has settled on it.
                double axleLoad = spec.Mass * CarPhysics.G * (front ? spec.FrontWeight : 1 - spec.FrontWeight) / 2;
                float sag = (float)(axleLoad / spec.SpringRate);
                ws[i] = new CarController.Wheel
                {
                    Visual = wheels[i],
                    Mount = centre + Vector3.up * ((float)spec.RestLength - sag),
                    Front = front,
                    Left = left,
                    Radius = radius,
                };
            }
            car.Configure(spec, ws);

            // Driver's eye point: left seat, a bit behind the middle, below the roof.
            var seat = new GameObject("Seat").transform;
            seat.SetParent(root.transform, false);
            seat.localPosition = new Vector3(-body.size.x * 0.2f, body.max.y - 0.42f, body.center.z - body.size.z * 0.08f);
            AddHeadlights(root.transform, body);
            return car;
        }

        /// <summary>A forward spot light at the front; off until night.</summary>
        public static Light AddHeadlights(Transform root, Bounds body)
        {
            var beam = new GameObject("Headlights");
            beam.transform.SetParent(root, false);
            beam.transform.localPosition = new Vector3(0f, body.min.y + body.size.y * 0.35f, body.max.z + 0.05f);
            beam.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            var spot = beam.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.range = 22f;
            spot.spotAngle = 70f;
            spot.intensity = 24f;
            spot.color = new Color(1f, 0.95f, 0.85f);
            spot.shadows = LightShadows.None;
            spot.enabled = false;
            return spot;
        }

        private static PhysicsMaterial Slippery()
        {
            if (_slippery != null) return _slippery;
            _slippery = new PhysicsMaterial("Car body") { dynamicFriction = 0.2f, staticFriction = 0.2f, frictionCombine = PhysicsMaterialCombine.Minimum };
            return _slippery;
        }

        private static Transform Find(Transform t, string name)
        {
            if (t.name == name) return t;
            foreach (Transform child in t)
            {
                Transform found = Find(child, name);
                if (found != null) return found;
            }
            return null;
        }
    }

    /// <summary>Physics layers the city uses (defined in the project's tag manager).</summary>
    public static class CityLayers
    {
        public const int Vehicle = 8;
        public const int Pedestrian = 9;

        /// <summary>People and cars don't physically collide (people step aside, cars stop for them).</summary>
        public static void Apply() => Physics.IgnoreLayerCollision(Vehicle, Pedestrian, true);
    }
}
