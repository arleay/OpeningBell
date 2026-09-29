using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using Unity.Profiling;
using Unity.Profiling.LowLevel;
using Unity.Profiling.LowLevel.Unsafe;
using UnityEngine;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>
    /// Where the frame goes (a diagnostic, run on demand): triangles by scene group, terrain settings, and main-thread
    /// time by engine phase, to `TestResults/perf-diag.txt`.
    /// </summary>
    public class PerfDiagnosticsTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Where_The_Frame_Goes()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(12));
            Camera cam = player.GetComponentInChildren<Camera>();
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.DefaultHDR);
            cam.targetTexture = target;
            for (int i = 0; i < 30; i++) yield return null;
            var report = new StringBuilder();

            // Triangles in the scene by group two levels deep (what's loaded, not only what's visible).
            var byGroup = new Dictionary<string, long>();
            foreach (MeshFilter mf in Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None))
            {
                var r = mf.GetComponent<MeshRenderer>();
                if (r == null || !r.enabled || !r.gameObject.activeInHierarchy || mf.sharedMesh == null) continue;
                long tris = 0;
                for (int s = 0; s < mf.sharedMesh.subMeshCount; s++) tris += mf.sharedMesh.GetIndexCount(s) / 3;
                // Statically batched renderers share one combined mesh: count only their own part.
                if (r.isPartOfStaticBatch) tris = tris / Mathf.Max(1, mf.sharedMesh.subMeshCount);
                string key = Path(mf.transform, 3);
                byGroup[key] = byGroup.TryGetValue(key, out long t) ? t + tris : tris;
            }
            long skinned = Object.FindObjectsByType<SkinnedMeshRenderer>(FindObjectsSortMode.None).Where(s => s.enabled && s.sharedMesh != null).Sum(s => { long n = 0; for (int i = 0; i < s.sharedMesh.subMeshCount; i++) n += s.sharedMesh.GetIndexCount(i) / 3; return n; });
            report.AppendLine("== triangles by group (loaded)");
            foreach (var kv in byGroup.OrderByDescending(k => k.Value).Take(30)) report.AppendLine($"{kv.Value / 1000,9}k  {kv.Key}");
            report.AppendLine($"{skinned / 1000,9}k  (skinned characters)");
            foreach (Terrain t in Terrain.activeTerrains)
                report.AppendLine($"terrain {t.name}: res {t.terrainData.heightmapResolution}, pixelError {t.heightmapPixelError}, basemapDist {t.basemapDistance}, details {t.drawTreesAndFoliage}/{t.detailObjectDistance}/{t.detailObjectDensity}, trees {t.terrainData.treeInstanceCount} @ {t.treeDistance}");

            // Triangles drawn this frame with each suspect switched off (the differences are what each costs).
            report.AppendLine("== triangles drawn with X off (maple street view)");
            var city = Find<CityBuilder>();
            if (city.Anchors.TryGetValue("mart_front_out", out Vector3 street)) player.PlaceAt(street + new Vector3(0f, 0f, -3f), 270f, 5f);
            for (int i = 0; i < 10; i++) yield return null;
            var drawn = ProfilerRecorder.StartNew(ProfilerCategory.Render, "Triangles Count");
            var toggles = new List<(string Name, System.Action<bool> Set)>
            {
                ("nothing", _ => { }),
                ("forest", on => { foreach (var f in Object.FindObjectsByType<OpeningBell.City.Forest>(FindObjectsSortMode.None)) f.enabled = on; }),
                ("terrain details", on => { foreach (Terrain t in Terrain.activeTerrains) t.drawTreesAndFoliage = on; }),
                ("terrain", on => { foreach (Terrain t in Terrain.activeTerrains) t.drawHeightmap = on; }),
                ("static city", on => { foreach (Transform t in GameObject.Find("City").transform) if (t.name == "Static") t.gameObject.SetActive(on); }),
                ("dynamic city", on => { foreach (Transform t in GameObject.Find("City").transform) if (t.name == "Dynamic") t.gameObject.SetActive(on); }),
                ("pedestrians+traffic", on => { foreach (var v in new[] { "Pedestrians", "Traffic" }) { var g = GameObject.Find(v); if (g != null) foreach (Transform ch in g.transform) ch.gameObject.SetActive(on); } }),
            };
            foreach (var (name, set) in toggles)
            {
                set(false);
                for (int i = 0; i < 4; i++) yield return null;
                report.AppendLine($"{drawn.LastValue / 1000,9}k  without {name}");
                set(true);
                for (int i = 0; i < 2; i++) yield return null;
            }
            drawn.Dispose();

            // Main-thread phases, averaged over 60 frames.
            string[] markers =
            {
                "PlayerLoop", "BehaviourUpdate", "PreLateUpdate.ScriptRunBehaviourLateUpdate", "FixedUpdate.PhysicsFixedUpdate",
                "PreLateUpdate.DirectorUpdateAnimationBegin", "PreLateUpdate.DirectorUpdateAnimationEnd", "PostLateUpdate.UpdateAllSkinnedMeshes",
                "PostLateUpdate.FinishFrameRendering", "Inl_UniversalRenderPipeline.RenderSingleCameraInternal", "Gfx.WaitForPresentOnGfxThread",
                "CullScriptable", "Shadows.Draw", "PostLateUpdate.UpdateAllRenderers",
            };
            var recs = markers.Select(m => ProfilerRecorder.StartNew(new ProfilerCategory("Internal"), m, 64)).ToArray();
            for (int i = 0; i < 60; i++) yield return null;
            report.AppendLine("== main thread by phase (ms, avg of 60 frames)");
            for (int i = 0; i < markers.Length; i++)
            {
                double sum = 0; int n = recs[i].Count;
                for (int s = 0; s < n; s++) sum += recs[i].GetSample(s).Value;
                report.AppendLine($"{(n > 0 ? sum / n / 1e6 : -1),8:F2}  {markers[i]}  (valid {recs[i].Valid}, samples {n})");
                recs[i].Dispose();
            }
            // Visible renderers (maple street view) by group and by kind.
            var visible = Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None).Where(r => r.enabled && r.isVisible).ToList();
            report.AppendLine($"== visible renderers: {visible.Count}");
            foreach (var g in visible.GroupBy(r => Path(r.transform, 3)).OrderByDescending(g => g.Count()).Take(15)) report.AppendLine($"{g.Count(),7}  {g.Key}");
            foreach (var g in visible.GroupBy(r => r.GetType().Name + (r.GetComponent<TextMesh>() != null ? " (text)" : "") + (r.isPartOfStaticBatch ? " static-batched" : "")).OrderByDescending(g => g.Count()))
                report.AppendLine($"{g.Count(),7}  {g.Key}");
            report.AppendLine($"shadow-casting visible: {visible.Count(r => r.shadowCastingMode != UnityEngine.Rendering.ShadowCastingMode.Off)}");
            foreach (string g in new[] { "City/Dynamic/Parked cars", "City/Static/Town", "City/Static/Street signs" })
            {
                report.AppendLine($"-- {g} by name / why not merged");
                foreach (var n in visible.Where(r => Path(r.transform, 3) == g).GroupBy(r => r.name + " [" + string.Join(",", r.GetComponents<Component>().Select(c => c.GetType().Name).Where(x => x != "Transform")) + "] " + (Scripted(r.transform) ?? "")).OrderByDescending(x => x.Count()).Take(8))
                    report.AppendLine($"{n.Count(),7}  {n.Key} readable {(n.First().TryGetComponent(out MeshFilter nf) && nf.sharedMesh != null ? nf.sharedMesh.isReadable.ToString() : "-")} det {n.First().transform.localToWorldMatrix.determinant:F3}");
            }

            // Every profiler marker, timed over 30 frames: the 40 most expensive (inclusive, so parents include children).
            var handles = new List<ProfilerRecorderHandle>();
            ProfilerRecorderHandle.GetAvailable(handles);
            var all = new List<(string Name, ProfilerRecorder Rec)>();
            foreach (var h in handles)
            {
                var d = ProfilerRecorderHandle.GetDescription(h);
                if (d.UnitType != ProfilerMarkerDataUnit.TimeNanoseconds) continue;
                all.Add((d.Category.Name + "/" + d.Name, ProfilerRecorder.StartNew(d.Category, d.Name, 32)));
            }
            for (int i = 0; i < 30; i++) yield return null;
            report.AppendLine($"== top markers (ms/frame, of {all.Count})");
            foreach (var (name, rec) in all.Select(x => (x.Name, x.Rec, Avg: Avg(x.Rec))).OrderByDescending(x => x.Avg).Take(45).Select(x => (x.Name, x.Rec)))
                report.AppendLine($"{Avg(rec),8:F2}  {name}");
            foreach (var (_, rec) in all) rec.Dispose();

            // Scripts by type: how many of each MonoBehaviour are live (cheap proxy for per-frame script cost).
            report.AppendLine("== live behaviours by type (top 20)");
            foreach (var g in Object.FindObjectsByType<MonoBehaviour>(FindObjectsSortMode.None).Where(b => b.isActiveAndEnabled).GroupBy(b => b.GetType().Name).OrderByDescending(g => g.Count()).Take(20))
                report.AppendLine($"{g.Count(),6}  {g.Key}");
            report.AppendLine($"lights: {Object.FindObjectsByType<Light>(FindObjectsSortMode.None).Count(l => l.isActiveAndEnabled)} enabled; rigidbodies {Object.FindObjectsByType<Rigidbody>(FindObjectsSortMode.None).Length}; colliders {Object.FindObjectsByType<Collider>(FindObjectsSortMode.None).Length}; animators {Object.FindObjectsByType<Animator>(FindObjectsSortMode.None).Count(a => a.isActiveAndEnabled)}");

            cam.targetTexture = null;
            target.Release();
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "TestResults");
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "perf-diag.txt"), report.ToString());
            Assert.Pass();
        }

        private static string Scripted(Transform t)
        {
            for (Transform p = t; p != null; p = p.parent)
                foreach (MonoBehaviour b in p.GetComponents<MonoBehaviour>())
                    if (!(b is SurfaceTag)) return "under " + b.GetType().Name + " on " + p.name;
            return null;
        }

        private static double Avg(ProfilerRecorder r)
        {
            if (!r.Valid || r.Count == 0) return 0;
            double sum = 0;
            for (int i = 0; i < r.Count; i++) sum += r.GetSample(i).Value;
            return sum / r.Count / 1e6;
        }

        private static string Path(Transform t, int depth)
        {
            var parts = new List<string>();
            for (Transform p = t; p != null; p = p.parent) parts.Insert(0, p.name);
            return string.Join("/", parts.Take(depth));
        }
    }
}
