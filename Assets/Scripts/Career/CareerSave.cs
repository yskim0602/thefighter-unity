using System;
using System.IO;
using UnityEngine;

namespace TheFighter
{
    /// JSON on disk, under Application.persistentDataPath so it survives a reinstall-in-place and
    /// lands somewhere the platform actually lets us write (Program Files and the project folder
    /// do not count on either of the platforms we care about).
    ///
    /// Every failure path here ends in a playable career rather than an exception. Losing a save is
    /// bad; refusing to start because a save is unreadable is worse, and a half-loaded save that
    /// looks fine is worst of all - so a bad file is backed up and replaced, never patched.
    public static class CareerSave
    {
        const string FileName = "career.json";
        const string BackupSuffix = ".broken";

        public static string Path
        {
            get { return System.IO.Path.Combine(Application.persistentDataPath, FileName); }
        }

        public static bool Exists
        {
            get { return File.Exists(Path); }
        }

        public static CareerData LoadOrCreate()
        {
            CareerData loaded = Load();
            return loaded != null ? loaded : new CareerData();
        }

        /// Returns null rather than a half-built career when anything is wrong, so the caller
        /// never has to guess whether what it got is trustworthy.
        public static CareerData Load()
        {
            if (!File.Exists(Path))
            {
                return null;
            }

            string json;
            try
            {
                json = File.ReadAllText(Path);
            }
            catch (Exception error)
            {
                Debug.LogWarning("CareerSave: could not read " + Path + " - " + error.Message);
                return null;
            }

            CareerData data = null;
            try
            {
                data = JsonUtility.FromJson<CareerData>(json);
            }
            catch (Exception error)
            {
                Debug.LogWarning("CareerSave: " + Path + " is not valid JSON - " + error.Message);
            }

            // FromJson returns null for empty input and throws for malformed input, but it will
            // happily hand back an object with null collections if the file is a stub - so the
            // fields the rest of the game dereferences get checked, not assumed.
            if (data == null || data.Stats == null)
            {
                Quarantine("unreadable");
                return null;
            }

            if (data.Version != CareerData.CurrentVersion)
            {
                Quarantine("version " + data.Version + ", expected " + CareerData.CurrentVersion);
                return null;
            }

            if (data.History == null) { data.History = new System.Collections.Generic.List<FightRecord>(); }
            if (data.Offers == null) { data.Offers = new System.Collections.Generic.List<MatchOffer>(); }

            return data;
        }

        public static bool Save(CareerData data)
        {
            if (data == null)
            {
                return false;
            }

            try
            {
                // Written to a temp file and moved into place, so a crash mid-write cannot leave a
                // truncated save where a good one used to be.
                string temp = Path + ".tmp";
                File.WriteAllText(temp, JsonUtility.ToJson(data, true));
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
                File.Move(temp, Path);
                return true;
            }
            catch (Exception error)
            {
                Debug.LogWarning("CareerSave: could not write " + Path + " - " + error.Message);
                return false;
            }
        }

        public static void Delete()
        {
            try
            {
                if (File.Exists(Path))
                {
                    File.Delete(Path);
                }
            }
            catch (Exception error)
            {
                Debug.LogWarning("CareerSave: could not delete " + Path + " - " + error.Message);
            }
        }

        /// Moves a save we refuse to load aside instead of deleting it. If the format turns out to
        /// be recoverable, somebody's career is still on disk.
        static void Quarantine(string why)
        {
            Debug.LogWarning("CareerSave: starting a fresh career - " + Path + " (" + why + ")");
            try
            {
                string broken = Path + BackupSuffix;
                if (File.Exists(broken))
                {
                    File.Delete(broken);
                }
                File.Move(Path, broken);
            }
            catch (Exception error)
            {
                Debug.LogWarning("CareerSave: could not set the bad save aside - " + error.Message);
            }
        }
    }
}
