using System;
using System.IO;
using AllIn.Core;
using UnityEngine;

namespace AllIn.Game
{
    /// <summary>
    /// Autosave only. Writes to a temp file and swaps it in, so a crash mid-write never leaves a half save.
    /// No manual save/load UI — there is no going back.
    /// </summary>
    public static class SaveIO
    {
        public const string FileName = "allin-save-v1.json";

        /// <summary>Dev capture runs point this elsewhere so they never touch the player's real save.</summary>
        public static string DirectoryOverride;

        public static string FilePath => Path.Combine(DirectoryOverride ?? Application.persistentDataPath, FileName);

        public static string ToJson(SaveData data) => JsonUtility.ToJson(data);

        public static SaveData FromJson(string json) => JsonUtility.FromJson<SaveData>(json);

        public static void Save(SaveData data)
        {
            string path = FilePath;
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, ToJson(data));
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public static SaveData Load()
        {
            string path = FilePath;
            if (!File.Exists(path)) return null;
            try
            {
                var data = FromJson(File.ReadAllText(path));
                return data != null && data.version == 1 && data.day >= 1 ? data : null;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[AllIn] unreadable save, starting over: " + e.Message);
                return null;
            }
        }

        public static void Delete()
        {
            if (File.Exists(FilePath)) File.Delete(FilePath);
        }
    }
}
