using System;
using System.Globalization;
using System.IO;
using SalesSim.Game;
using UnityEngine;

namespace SalesSim.Infrastructure
{
    /// <summary>
    /// <see cref="IGameStateStore"/> as one small JSON file (default: <c>{persistentDataPath}/savegame.json</c>).
    /// </summary>
    /// <remarks>
    /// <para>Writing is atomic: the new state goes to <c>savegame.json.tmp</c> and then replaces the file, keeping the
    /// previous state as <c>savegame.json.bak</c>. A crash while writing leaves the old file intact.</para>
    /// <para>Loading never throws: no file = new game; unreadable file = the backup if that is readable, else a new game —
    /// the damaged file is kept as <c>savegame.corrupt-{time}.json</c> and the reason is logged. Unknown fields are
    /// ignored; a file from a newer format is loaded as far as it is understood and copied to
    /// <c>savegame.v{version}.json</c> before this build overwrites it.</para>
    /// </remarks>
    public sealed class JsonFileGameStateStore : IGameStateStore
    {
        public const string FileName = "savegame.json";

        private readonly string path;
        private readonly Action<string> log;
        private readonly Action<string> warn;

        /// <param name="path">The save file; tests pass a temporary one.</param>
        /// <param name="log">Information (e.g. "new game").</param>
        /// <param name="warn">Problems: damaged, unwritable or newer saves. Defaults to <paramref name="log"/>.</param>
        public JsonFileGameStateStore(string path, Action<string> log = null, Action<string> warn = null)
        {
            this.path = path ?? throw new ArgumentNullException(nameof(path));
            this.log = log ?? (_ => { });
            this.warn = warn ?? this.log;
        }

        /// <summary>The store the game uses: <c>savegame.json</c> in Unity's persistent data folder.</summary>
        public static JsonFileGameStateStore ForGame(Action<string> log, Action<string> warn)
        {
            return new JsonFileGameStateStore(Path.Combine(UnityEngine.Application.persistentDataPath, FileName), log, warn);
        }

        public string FilePath => path;

        private string BackupPath => path + ".bak";

        public SaveData Load()
        {
            if (!File.Exists(path))
            {
                log($"[Save] No savegame at {path}: new game.");
                return new SaveData();
            }

            if (TryRead(path, out var data, out var error))
            {
                return data;
            }

            var kept = Path.Combine(Path.GetDirectoryName(path) ?? string.Empty,
                $"savegame.corrupt-{DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}.json");
            TryMove(path, kept);
            if (File.Exists(BackupPath) && TryRead(BackupPath, out var backup, out _))
            {
                warn($"[Save] Savegame unreadable ({error}); kept as {kept}, continuing from the backup.");
                return backup;
            }

            warn($"[Save] Savegame unreadable ({error}); kept as {kept}, starting a new game.");
            return new SaveData();
        }

        public void Save(SaveData data)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            var temporary = path + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
                data.version = SaveData.CurrentVersion;
                File.WriteAllText(temporary, JsonUtility.ToJson(data, true));
                if (File.Exists(path))
                {
                    File.Replace(temporary, path, BackupPath);
                }
                else
                {
                    File.Move(temporary, path);
                }
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                // The game goes on; the next save tries again.
                warn($"[Save] Could not write {path}: {exception.Message}");
            }
        }

        /// <summary>Deletes the save and its backup: the next start is a new game. Development use only.</summary>
        public void Delete()
        {
            foreach (var file in new[] { path, BackupPath, path + ".tmp" })
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }

        private bool TryRead(string file, out SaveData data, out string error)
        {
            data = null;
            error = null;
            try
            {
                var json = File.ReadAllText(file);
                if (string.IsNullOrWhiteSpace(json))
                {
                    error = "empty file";
                    return false;
                }

                data = JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception exception) when (exception is ArgumentException || exception is IOException)
            {
                error = exception.Message;
                return false;
            }

            if (data == null)
            {
                error = "no JSON object";
                return false;
            }

            if (data.version > SaveData.CurrentVersion)
            {
                var copy = Path.Combine(Path.GetDirectoryName(file) ?? string.Empty, $"savegame.v{data.version}.json");
                if (!File.Exists(copy))
                {
                    File.Copy(file, copy);
                }

                warn($"[Save] Savegame format {data.version} is newer than this build ({SaveData.CurrentVersion}); "
                    + $"loading the known fields, original kept as {copy}.");
            }

            if (data.balance < 0)
            {
                warn($"[Save] Negative balance {data.balance} in the savegame; using 0.");
                data.balance = 0;
            }

            return true;
        }

        private void TryMove(string from, string to)
        {
            try
            {
                File.Move(from, to);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                warn($"[Save] Could not keep the damaged savegame: {exception.Message}");
            }
        }
    }
}
