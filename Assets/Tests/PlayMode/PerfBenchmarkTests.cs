using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using NUnit.Framework;
using OpeningBell.City;
using OpeningBell.Gameplay;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.TestTools;

namespace OpeningBell.Tests
{
    /// <summary>
    /// A frame-cost benchmark, not a pass/fail test: renders the player's camera at 1920×1080 from typical spots
    /// (street, downtown, the busiest shops, a mirror) and logs frame time, draw calls, set-pass calls, triangles and
    /// shadow casters per spot to `TestResults/perf.txt`, so optimisations can be compared like for like.
    /// </summary>
    public class PerfBenchmarkTests : SceneTestBase
    {
        [UnityTest]
        public IEnumerator Benchmark_Viewpoints()
        {
            yield return LoadMain();
            var game = Find<GameBootstrap>();
            var player = Find<FirstPersonController>();
            var city = Find<CityBuilder>();
            game.SkipTo(game.Clock.Now.Date.AddDays(1).AddHours(12));
            Camera cam = player.GetComponentInChildren<Camera>();
            var target = new RenderTexture(1920, 1080, 24, RenderTextureFormat.DefaultHDR);
            cam.targetTexture = target;

            var spots = new List<(string Name, Vector3 At, float Yaw, float Pitch)>();
            void Anchor(string name, string anchor, Vector3 offset, float yaw, float pitch = 5f)
            {
                if (city.Anchors.TryGetValue(anchor, out Vector3 a)) spots.Add((name, a + offset, yaw, pitch));
            }
            Anchor("maple street", "mart_front_out", new Vector3(0f, 0f, -3f), 270f);
            Anchor("coffee", "coffee_counter", new Vector3(0f, 0f, -2f), 0f);
            Anchor("fuel", "fuel_pump_west", new Vector3(4f, 0f, -4f), 315f);
            Anchor("mechanic", "mechanic_bay1", new Vector3(0f, 0f, -3f), 0f);
            foreach (Business b in BusinessPlan.All())
            {
                if (b.Name != "FreshWay Market" && b.Name != "Harlow Hardware" && b.Name != "Lustre" && b.Name != "Kell Pawn & Loan") continue;
                Transform root = Object.FindObjectsByType<Transform>(FindObjectsSortMode.None).FirstOrDefault(t => t.name == b.Name && t.Find("Interior") != null);
                if (root != null) spots.Add((b.Name, root.TransformPoint(new Vector3(0f, 0f, 1.5f)), root.eulerAngles.y, 8f));
            }
            // High over town looking across it: the worst case for draw distance.
            if (city.Anchors.TryGetValue("mart_front_out", out Vector3 town)) spots.Add(("overlook", town + new Vector3(0f, 40f, -60f), 20f, 25f));

            string[] counters = { "Batches Count", "SetPass Calls Count", "Triangles Count", "Shadow Casters Count", "Visible Skinned Meshes Count" };
            var report = new StringBuilder();
            var urp = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
            // Diagnostic variants (set PERF_VARIANTS=1): what each setting costs, measured on the same spots.
            var variants = new List<(string Name, System.Action Apply)> { ("as shipped", () => { }) };
            if (System.Environment.GetEnvironmentVariable("PERF_VARIANTS") == "1")
            {
                int cascades = urp.shadowCascadeCount, msaa = urp.msaaSampleCount;
                float distance = urp.shadowDistance;
                variants.Add(("2 cascades", () => { urp.shadowCascadeCount = 2; }));
                variants.Add(("2 casc, 60 m", () => { urp.shadowCascadeCount = 2; urp.shadowDistance = 60f; }));
                variants.Add(("no MSAA", () => { urp.shadowCascadeCount = cascades; urp.shadowDistance = distance; urp.msaaSampleCount = 1; }));
                variants.Add(("no shadows", () => { urp.msaaSampleCount = msaa; urp.shadowDistance = 0f; }));
                variants.Add(("restore", () => { urp.shadowDistance = distance; urp.shadowCascadeCount = cascades; urp.msaaSampleCount = msaa; }));
            }
            foreach (var (variant, apply) in variants)
            {
            apply();
            if (variant == "restore") break;
            report.AppendLine($"== {variant}");
            report.AppendLine($"{"spot",-20} {"ms avg",7} {"ms p95",7} {"batches",8} {"setpass",8} {"tris k",8} {"shadow",7} {"skinned",8}");
            float sumAvg = 0f;
            foreach (var (name, at, yaw, pitch) in spots)
            {
                player.PlaceAt(at, yaw, pitch);
                for (int i = 0; i < 20; i++) yield return null; // settle: culling, lights, streaming
                var recorders = counters.Select(c => ProfilerRecorder.StartNew(ProfilerCategory.Render, c)).ToArray();
                var frames = new List<float>();
                for (int i = 0; i < 90; i++)
                {
                    yield return null;
                    frames.Add(Time.unscaledDeltaTime * 1000f);
                }
                long[] values = recorders.Select(r => r.LastValue).ToArray();
                foreach (var r in recorders) r.Dispose();
                frames.Sort();
                float avg = frames.Average(), p95 = frames[(int)(frames.Count * 0.95f)];
                sumAvg += avg;
                report.AppendLine($"{name,-20} {avg,7:F2} {p95,7:F2} {values[0],8} {values[1],8} {values[2] / 1000,8} {values[3],7} {values[4],8}");
            }
            report.AppendLine($"{"mean",-20} {sumAvg / Mathf.Max(1, spots.Count),7:F2}");
            }
            cam.targetTexture = null;
            target.Release();
            string dir = System.IO.Path.Combine(Application.dataPath, "..", "TestResults");
            System.IO.Directory.CreateDirectory(dir);
            System.IO.File.WriteAllText(System.IO.Path.Combine(dir, "perf.txt"), report.ToString());
            Debug.Log("PERF\n" + report);
            Assert.Greater(spots.Count, 6);
        }
    }
}
