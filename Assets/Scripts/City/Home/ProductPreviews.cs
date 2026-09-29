using System;
using System.Collections.Generic;
using OpeningBell.Home;
using UnityEngine;

namespace OpeningBell.City
{
    /// <summary>
    /// Product pictures for the online stores: each catalog item (in each colour) built from the same model the store
    /// and your home use, lit on a pale backdrop far below the city, rendered once by an off-screen camera and kept.
    /// Requests queue and a few render each frame, so opening the catalog doesn't stall.
    /// </summary>
    public sealed class ProductPreviews : MonoBehaviour
    {
        private const int Width = 320, Height = 240, PerFrame = 3;
        /// <summary>A layer nothing else uses, so the camera sees only the product.</summary>
        private const int Layer = 30;
        private static readonly Vector3 Stage = new Vector3(0f, -1800f, 0f);

        private CityContext _c;
        private Camera _camera;
        private RenderTexture _target;
        private readonly Dictionary<string, Texture2D> _done = new Dictionary<string, Texture2D>();
        private readonly Dictionary<string, List<Action<Texture2D>>> _waiting = new Dictionary<string, List<Action<Texture2D>>>();
        private readonly Queue<(string Id, int Variant)> _queue = new Queue<(string, int)>();

        public static ProductPreviews Build(CityContext c)
        {
            var go = new GameObject("Product previews");
            go.transform.SetParent(c.Dynamic, false);
            go.transform.position = Stage;
            var p = go.AddComponent<ProductPreviews>();
            p._c = c;
            c.Game.ProductPreview = p.Request;
            return p;
        }

        private void Request(string id, int variant, Action<Texture2D> done)
        {
            string key = id + "#" + variant;
            if (_done.TryGetValue(key, out Texture2D tex)) { done(tex); return; }
            if (!_waiting.TryGetValue(key, out var list))
            {
                _waiting[key] = list = new List<Action<Texture2D>>();
                _queue.Enqueue((id, variant));
            }
            list.Add(done);
        }

        private void Update()
        {
            for (int n = 0; n < PerFrame && _queue.Count > 0; n++)
            {
                var (id, variant) = _queue.Dequeue();
                string key = id + "#" + variant;
                Texture2D tex = Render(HomeCatalog.Find(id), variant);
                _done[key] = tex;
                if (_waiting.TryGetValue(key, out var list))
                    foreach (Action<Texture2D> done in list)
                        if (tex != null) done(tex);
                _waiting.Remove(key);
            }
        }

        private void Setup()
        {
            _target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4, name = "Product preview" };
            var cam = new GameObject("Preview camera");
            cam.transform.SetParent(transform, false);
            _camera = cam.AddComponent<Camera>();
            _camera.enabled = false; // rendered by hand
            _camera.cullingMask = 1 << Layer;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.93f, 0.93f, 0.92f);
            _camera.fieldOfView = 30f;
            _camera.targetTexture = _target;
            // A key light up front and a fill from the side: the sun may be down, the shop's pictures are always lit.
            foreach (var (at, intensity) in new[] { (new Vector3(-2f, 3f, -3f), 6f), (new Vector3(3f, 1.5f, -1f), 3f), (new Vector3(0f, 2f, 3f), 2f) })
            {
                var l = new GameObject("Preview light").AddComponent<Light>();
                l.transform.SetParent(transform, false);
                l.transform.localPosition = at;
                l.type = LightType.Point;
                l.range = 12f;
                l.intensity = intensity;
                l.cullingMask = 1 << Layer;
                l.shadows = LightShadows.None;
            }
        }

        private Texture2D Render(HomeItem item, int variant)
        {
            if (item == null) return null;
            if (_camera == null) Setup();
            GameObject model = HomeModels.Build(_c, transform, item, variant, false);
            model.transform.localPosition = Vector3.zero;
            model.transform.localRotation = Quaternion.Euler(0f, 30f, 0f); // fronts face -z, the camera's side: turned for a three-quarter view
            SetLayer(model.transform, Layer);
            foreach (Collider col in model.GetComponentsInChildren<Collider>()) col.enabled = false;

            // Frame it: the bounds fill most of the picture, seen from a little above.
            Bounds b = new Bounds(transform.position + Vector3.up * item.Height / 2f, new Vector3(item.Width, item.Height, item.Depth));
            bool any = false;
            foreach (Renderer r in model.GetComponentsInChildren<Renderer>())
            {
                if (!any) b = r.bounds;
                else b.Encapsulate(r.bounds);
                any = true;
            }
            float radius = Mathf.Max(0.05f, b.extents.magnitude);
            float distance = radius / Mathf.Sin(_camera.fieldOfView * 0.5f * Mathf.Deg2Rad) * 0.95f;
            _camera.transform.position = b.center + (Quaternion.Euler(18f, 0f, 0f) * Vector3.back) * distance;
            _camera.transform.LookAt(b.center);
            _camera.nearClipPlane = Mathf.Max(0.01f, distance - radius * 2f);
            _camera.farClipPlane = distance + radius * 2f;

            _camera.Render();
            RenderTexture before = RenderTexture.active;
            RenderTexture.active = _target;
            var tex = new Texture2D(Width, Height, TextureFormat.RGBA32, false) { name = "Preview " + item.Id, wrapMode = TextureWrapMode.Clamp };
            tex.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
            tex.Apply(false, true);
            RenderTexture.active = before;
            model.SetActive(false);
            Destroy(model);
            return tex;
        }

        private static void SetLayer(Transform t, int layer)
        {
            t.gameObject.layer = layer;
            foreach (Transform c in t) SetLayer(c, layer);
        }

        private void OnDestroy()
        {
            if (_target != null) _target.Release();
            foreach (Texture2D t in _done.Values) if (t != null) Destroy(t);
        }
    }
}
