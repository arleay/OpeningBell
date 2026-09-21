using System;
using System.IO;
using UnityEngine;

namespace OpeningBell
{
    /// <summary>
    /// JSON save slots under persistentDataPath/saves. Writes go to a temp file first and replace the slot
    /// atomically, keeping the previous save as .bak; a corrupt slot falls back to its backup.
    /// </summary>
    public static class SaveSystem
    {
        [Serializable]
        private sealed class VersionProbe
        {
            public int Version;
        }

        /// <summary>Tests point this at a temp folder so they never touch the player's saves.</summary>
        public static string DirectoryOverride { get; set; }

        public static string Directory => DirectoryOverride ?? Path.Combine(Application.persistentDataPath, "saves");

        public static string SlotPath(string slot) => Path.Combine(Directory, slot + ".json");

        public static bool Exists(string slot) => File.Exists(SlotPath(slot));

        public static void Write(SaveGame save, string slot)
        {
            System.IO.Directory.CreateDirectory(Directory);
            string path = SlotPath(slot);
            string temp = path + ".tmp";
            save.Version = SaveGame.CurrentVersion;
            save.SavedAtUtc = DateTime.UtcNow.ToString("o");
            File.WriteAllText(temp, JsonUtility.ToJson(save));

            if (File.Exists(path)) File.Replace(temp, path, path + ".bak");
            else File.Move(temp, path);
        }

        /// <summary>False with an error message if the slot is missing, unreadable, or from a newer game version.</summary>
        public static bool TryRead(string slot, out SaveGame save, out string error)
        {
            string path = SlotPath(slot);
            if (!File.Exists(path))
            {
                save = null;
                error = null;
                return false;
            }

            if (TryParse(File.ReadAllText(path), out save, out error)) return true;

            string backup = path + ".bak";
            if (File.Exists(backup) && TryParse(File.ReadAllText(backup), out save, out string backupError))
            {
                error = $"Save '{slot}' was unreadable ({error}); loaded its backup instead.";
                return true;
            }
            return false;
        }

        public static void Delete(string slot)
        {
            foreach (string path in new[] { SlotPath(slot), SlotPath(slot) + ".bak" })
                if (File.Exists(path)) File.Delete(path);
        }

        private static bool TryParse(string json, out SaveGame save, out string error)
        {
            save = null;
            try
            {
                var probe = JsonUtility.FromJson<VersionProbe>(json);
                if (probe == null || probe.Version < 1)
                {
                    error = "not a save file";
                    return false;
                }
                if (probe.Version > SaveGame.CurrentVersion)
                {
                    error = $"made by a newer version of the game (v{probe.Version}, this build reads up to v{SaveGame.CurrentVersion})";
                    return false;
                }
                // Future: upgrade older versions here, one step per version, before deserializing.
                save = JsonUtility.FromJson<SaveGame>(json);
                error = null;
                return true;
            }
            catch (Exception e)
            {
                error = e.Message;
                return false;
            }
        }
    }
}
