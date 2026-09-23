using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

namespace OpeningBell.EditorTools
{
    /// <summary>
    /// Fills the vehicle library's recorded engines from Assets/Audio/Engines/&lt;Name&gt;/{Engine,Exhaust}/*.wav and
    /// Startup.wav. Each loop's file name ends in the rpm it was recorded at (e.g. "1lr-gse_05500.wav").
    /// Batch: <c>Unity -batchmode -quit -projectPath . -executeMethod OpeningBell.EditorTools.EngineSoundsBuilder.Rebuild</c>
    /// </summary>
    public static class EngineSoundsBuilder
    {
        public const string Root = "Assets/Audio/Engines";
        private const string LibraryPath = "Assets/ScriptableObjects/Vehicles/VehicleLibrary.asset";
        private static readonly Regex Rpm = new Regex(@"(\d+)$");

        [MenuItem("Opening Bell/Rebuild Engine Sounds")]
        public static void Rebuild()
        {
            var sets = new List<EngineSoundSet>();
            foreach (string dir in Directory.GetDirectories(Root).OrderBy(d => d))
            {
                var set = new EngineSoundSet { Name = Path.GetFileName(dir) };
                (set.Engine, set.EngineRpm) = Loops(dir + "/Engine");
                (set.Exhaust, set.ExhaustRpm) = Loops(dir + "/Exhaust");
                set.Startup = AssetDatabase.LoadAssetAtPath<AudioClip>(dir + "/Startup.wav");
                sets.Add(set);
                Debug.Log($"Engine {set.Name}: {set.Engine.Length} engine + {set.Exhaust.Length} exhaust loops, " +
                          $"{(set.EngineRpm.Length > 0 ? set.EngineRpm.First() + "–" + set.EngineRpm.Last() : "-")} rpm{(set.Startup != null ? ", startup" : "")}");
            }
            var library = AssetDatabase.LoadAssetAtPath<VehicleLibrary>(LibraryPath);
            library.EditorSetEngineSounds(sets);
            EditorUtility.SetDirty(library);
            AssetDatabase.SaveAssets();
        }

        /// <summary>The folder's loops ordered by recorded rpm.</summary>
        private static (AudioClip[], float[]) Loops(string folder)
        {
            if (!Directory.Exists(folder)) return (new AudioClip[0], new float[0]);
            var loops = Directory.GetFiles(folder, "*.wav")
                .Select(f => (clip: AssetDatabase.LoadAssetAtPath<AudioClip>(f.Replace('\\', '/')),
                              rpm: float.Parse(Rpm.Match(Path.GetFileNameWithoutExtension(f)).Groups[1].Value)))
                .Where(l => l.clip != null && l.rpm > 0f)
                .OrderBy(l => l.rpm).ToArray();
            return (loops.Select(l => l.clip).ToArray(), loops.Select(l => l.rpm).ToArray());
        }
    }
}
