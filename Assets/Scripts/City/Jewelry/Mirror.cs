using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace OpeningBell.City
{
    /// <summary>
    /// A working mirror. The nearest one you're facing (within <see cref="Range"/>) is live: a second camera renders
    /// the scene reflected in its plane (the main camera's view matrix times a reflection, near plane clipped to the
    /// glass so nothing behind the wall shows) into a texture the glass samples at screen position (Mirror.shader).
    /// Other mirrors show a dim idle grey, so a house full of them costs one extra render at most.
    /// <para>
    /// Your first-person body has a collapsed head and stands behind the camera; the reflection instead shows a
    /// double (same model, look and jewellery, head intact) on the Reflection layer, which copies the body's pose
    /// every frame. The main camera skips that layer and the mirror camera skips the own-body layer.
    /// </para>
    /// </summary>
    [DefaultExecutionOrder(1000)] // after PlayerBody.LateUpdate, so the double copies this frame's pose
    public sealed class Mirror : MonoBehaviour
    {
        public const int ReflectionLayer = 13;
        public const float Range = 9f;
        private static readonly List<Mirror> All = new List<Mirror>();
        private static readonly int ReflectionTex = Shader.PropertyToID("_ReflectionTex"), Live = Shader.PropertyToID("_Live");
        private static Camera _cam;
        private static RenderTexture _rt;
        private static Mirror _live;
        private static GameObject _double, _doubleOf;
        private static readonly List<(Transform From, Transform To)> Bones = new List<(Transform, Transform)>();
        private static CityContext _c;
        private static int _frame = -1;

        private Renderer _glass;
        private Material _material;

        /// <summary>The mirror rendering a live reflection this frame, if any (tests).</summary>
        public static Mirror Current => _live;
        public static GameObject Double => _double;
        public RenderTexture Texture => _rt;

        /// <summary>
        /// A mirror pane under <paramref name="parent"/>, centred at <paramref name="centre"/>, its reflective side
        /// facing <paramref name="facing"/> (parent-local).
        /// </summary>
        public static Mirror Create(CityContext c, Transform parent, string name, Vector3 centre, Vector3 facing, Vector2 size)
        {
            _c = c;
            GameObject quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            Object.Destroy(quad.GetComponent<Collider>());
            quad.name = name;
            quad.transform.SetParent(parent, false);
            quad.transform.localPosition = centre;
            // The Quad primitive shows its face toward -z.
            quad.transform.localRotation = Quaternion.LookRotation(-facing, Vector3.up);
            quad.transform.localScale = new Vector3(size.x, size.y, 1f);
            var r = quad.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            Material template = Resources.Load<Material>("Surfaces/Mirror");
            var m = template != null ? new Material(template) : c.P.Lit(new Color(0.6f, 0.65f, 0.7f), 0.95f);
            // Set here rather than trusting the asset: a material saved without its properties reads them as zero.
            m.SetColor("_BaseColor", new Color(0.94f, 0.96f, 0.97f));
            m.SetColor("_Fallback", new Color(0.42f, 0.46f, 0.5f));
            m.SetFloat(Live, 0f);
            r.sharedMaterial = m;
            var mirror = quad.AddComponent<Mirror>();
            mirror._glass = r;
            mirror._material = m;
            return mirror;
        }

        private void OnEnable()
        {
            All.Add(this);
            if (All.Count == 1)
            {
                RenderPipelineManager.beginCameraRendering += BeginCamera;
                RenderPipelineManager.endCameraRendering += EndCamera;
            }
        }

        private void OnDisable()
        {
            All.Remove(this);
            if (_live == this) SetLive(null);
            if (All.Count == 0)
            {
                RenderPipelineManager.beginCameraRendering -= BeginCamera;
                RenderPipelineManager.endCameraRendering -= EndCamera;
                if (_cam != null) _cam.enabled = false;
                if (_double != null) _double.SetActive(false);
            }
        }

        // A reflection flips handedness: triangles wind the other way, so culling must flip while it renders.
        private static void BeginCamera(ScriptableRenderContext ctx, Camera cam) { if (cam == _cam) GL.invertCulling = true; }
        private static void EndCamera(ScriptableRenderContext ctx, Camera cam) { if (cam == _cam) GL.invertCulling = false; }

        /// <summary>The reflective side's normal (the Quad faces -z).</summary>
        private Vector3 Normal => -transform.forward;

        private void LateUpdate()
        {
            if (_frame == Time.frameCount) return; // one mirror runs the lot each frame
            _frame = Time.frameCount;
            Camera main = Camera.main;
            if (main == null) return;
            main.cullingMask &= ~(1 << ReflectionLayer);

            Mirror best = null;
            float bestD = Range;
            Vector3 eye = main.transform.position;
            foreach (Mirror m in All)
            {
                // Not a placement preview or a mirror being carried (their glass is swapped or on Ignore Raycast).
                if (m == null || !m._glass.isVisible || m._glass.sharedMaterial != m._material || m.gameObject.layer == 2) continue;
                Vector3 toEye = eye - m.transform.position;
                if (Vector3.Dot(toEye, m.Normal) <= 0.05f) continue; // behind it
                float d = toEye.magnitude;
                if (d < bestD) { bestD = d; best = m; }
            }
            SetLive(best);
            if (best == null) return;
            best.Render(main);
        }

        private static void SetLive(Mirror m)
        {
            if (_live == m) return;
            if (_live != null && _live._material != null) _live._material.SetFloat(Live, 0f);
            _live = m;
            if (m == null)
            {
                if (_cam != null) _cam.enabled = false;
                if (_double != null) _double.SetActive(false);
            }
        }

        private void Render(Camera main)
        {
            int w = Mathf.Max(256, main.pixelWidth / 2), h = Mathf.Max(144, main.pixelHeight / 2);
            if (_rt == null || _rt.width != w || _rt.height != h)
            {
                if (_rt != null) _rt.Release();
                _rt = new RenderTexture(w, h, 24, RenderTextureFormat.DefaultHDR) { name = "Mirror" };
            }
            if (_cam == null)
            {
                var go = new GameObject("Mirror camera");
                _cam = go.AddComponent<Camera>();
                _cam.enabled = false;
            }
            _cam.enabled = true;
            _cam.targetTexture = _rt;
            // Before the main camera, so the glass shows this frame's reflection (and never an unfilled texture).
            _cam.depth = main.depth - 1f;
            _cam.fieldOfView = main.fieldOfView;
            _cam.aspect = main.aspect;
            _cam.nearClipPlane = main.nearClipPlane;
            _cam.farClipPlane = Mathf.Min(main.farClipPlane, 120f);
            _cam.clearFlags = main.clearFlags;
            _cam.backgroundColor = main.backgroundColor;
            _cam.cullingMask = (main.cullingMask & ~(1 << PlayerBody.OwnBodyLayer)) | (1 << ReflectionLayer);

            // Reflect the main camera in the plane n·x + d = 0.
            Vector3 n = Normal, p = transform.position;
            float d = -Vector3.Dot(n, p);
            Matrix4x4 reflect = Reflection(new Vector4(n.x, n.y, n.z, d));
            _cam.transform.SetPositionAndRotation(reflect.MultiplyPoint(main.transform.position), main.transform.rotation);
            _cam.worldToCameraMatrix = main.worldToCameraMatrix * reflect;
            // Oblique near plane on the glass: whatever is behind the mirror (the wall, the next room) isn't drawn.
            Vector4 clip = CameraSpacePlane(_cam, p, n, 0.02f);
            _cam.projectionMatrix = main.projectionMatrix;
            _cam.projectionMatrix = _cam.CalculateObliqueMatrix(clip);

            _material.SetTexture(ReflectionTex, _rt);
            _material.SetFloat(Live, 1f);
            UpdateDouble();
        }

        /// <summary>
        /// Keeps the double in step with the first-person body: rebuilt when the body is (a new look or jewellery), and
        /// every frame placed where the body is with its bones copied (the head at full size).
        /// </summary>
        private static void UpdateDouble()
        {
            PlayerBody body = _c?.Player != null ? _c.Player.GetComponent<PlayerBody>() : null;
            if (body == null || body.Model == null || !body.Visible)
            {
                if (_double != null) _double.SetActive(false);
                return;
            }
            if (_doubleOf != body.Model || _double == null)
            {
                if (_double != null) Destroy(_double);
                _double = Instantiate(body.Source);
                _double.name = "Mirror double";
                CharacterStyle.Apply(_double, body.Look);
                Animator a = _double.GetComponent<Animator>();
                // Same proportions as the body (it may have shrunk the hands), before the jewellery is measured.
                Animator from = body.Model.GetComponent<Animator>();
                _double.transform.localScale = body.Model.transform.lossyScale;
                foreach (HumanBodyBones hb in new[] { HumanBodyBones.LeftHand, HumanBodyBones.RightHand })
                {
                    Transform s = from.GetBoneTransform(hb), t = a.GetBoneTransform(hb);
                    if (s != null && t != null) t.localScale = s.localScale;
                }
                a.enabled = false; // posed by copying, not animated
                CopyJewellery(body.Model.transform, _double.transform);
                foreach (Transform t in _double.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = ReflectionLayer;
                foreach (SkinnedMeshRenderer r in _double.GetComponentsInChildren<SkinnedMeshRenderer>()) r.updateWhenOffscreen = true;
                // Every transform of the skeleton, matched by name, not just the humanoid bones: the animator also
                // turns the rig's own in-between nodes, and a double missing those stands (and wears) back to front.
                Bones.Clear();
                Pair(body.Model.transform, _double.transform);
                _doubleOf = body.Model;
            }
            _double.SetActive(true);
            Transform src = body.Model.transform;
            _double.transform.SetPositionAndRotation(src.position, src.rotation);
            foreach (var (s, t) in Bones)
            {
                t.localPosition = s.localPosition;
                t.localRotation = s.localRotation;
                t.localScale = s.localScale;
            }
            Transform head = _double.GetComponent<Animator>().GetBoneTransform(HumanBodyBones.Head);
            if (head != null) head.localScale = Vector3.one;
        }

        /// <summary>
        /// The body's jewellery, cloned onto the same bones of the double (same local placement), so the reflection
        /// wears exactly what was fitted to the body. Watches keep time.
        /// </summary>
        private static void CopyJewellery(Transform body, Transform dbl)
        {
            foreach (Transform t in body.GetComponentsInChildren<Transform>(true))
            {
                if (t.name != "Jewellery" || t.parent == null) continue;
                Transform bone = Find(dbl, t.parent.name);
                if (bone == null) continue;
                GameObject copy = Instantiate(t.gameObject, bone, false);
                copy.name = "Jewellery";
                WatchFace[] from = t.GetComponentsInChildren<WatchFace>(true), to = copy.GetComponentsInChildren<WatchFace>(true);
                for (int i = 0; i < from.Length && i < to.Length; i++) to[i].Follow(from[i]);
            }
        }

        private static Transform Find(Transform root, string name)
        {
            if (root.name == name) return root;
            foreach (Transform child in root)
            {
                Transform hit = Find(child, name);
                if (hit != null) return hit;
            }
            return null;
        }

        private static void Pair(Transform from, Transform to)
        {
            foreach (Transform child in from)
            {
                if (child.name == "Jewellery") continue;
                Transform match = to.Find(child.name);
                if (match == null) continue;
                Bones.Add((child, match));
                Pair(child, match);
            }
        }

        private static Matrix4x4 Reflection(Vector4 plane)
        {
            var m = new Matrix4x4();
            m.m00 = 1f - 2f * plane.x * plane.x; m.m01 = -2f * plane.x * plane.y; m.m02 = -2f * plane.x * plane.z; m.m03 = -2f * plane.w * plane.x;
            m.m10 = -2f * plane.y * plane.x; m.m11 = 1f - 2f * plane.y * plane.y; m.m12 = -2f * plane.y * plane.z; m.m13 = -2f * plane.w * plane.y;
            m.m20 = -2f * plane.z * plane.x; m.m21 = -2f * plane.z * plane.y; m.m22 = 1f - 2f * plane.z * plane.z; m.m23 = -2f * plane.w * plane.z;
            m.m30 = 0f; m.m31 = 0f; m.m32 = 0f; m.m33 = 1f;
            return m;
        }

        /// <summary>The mirror plane in the reflected camera's space, nudged off the glass by <paramref name="offset"/>.</summary>
        private static Vector4 CameraSpacePlane(Camera cam, Vector3 pos, Vector3 normal, float offset)
        {
            Matrix4x4 m = cam.worldToCameraMatrix;
            Vector3 cp = m.MultiplyPoint(pos + normal * offset);
            Vector3 cn = m.MultiplyVector(normal).normalized;
            return new Vector4(cn.x, cn.y, cn.z, -Vector3.Dot(cp, cn));
        }
    }
}
