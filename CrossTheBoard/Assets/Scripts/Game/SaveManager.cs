using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace CrossTheBoard
{
    [DefaultExecutionOrder(-400)]
    public sealed class SaveManager : MonoBehaviour
    {
        private const string FileName = "save.json";

        public static SaveManager Instance { get; private set; }
        public SaveData Data { get; private set; } = new();
        public string SavePath => Path.Combine(Application.persistentDataPath, FileName);
        public bool CanSave { get; private set; } = true;
        public event Action DataChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;
            DontDestroyOnLoad(gameObject);
            Load();
        }

        public bool Load()
        {
            string backupPath = SavePath + ".bak";
            if (!File.Exists(SavePath) && !File.Exists(backupPath))
            {
                Data = new SaveData();
                CanSave = true;
                return true;
            }
            if (TryRead(SavePath, out var data))
            {
                Data = data;
                CanSave = true;
                return true;
            }
            if (data != null && data.version > SaveData.CurrentVersion)
            {
                CanSave = false;
                Debug.LogError("The save was created by a newer game version. Saving is disabled.", this);
                return false;
            }
            if (TryRead(backupPath, out data))
            {
                Data = data;
                try
                {
                    File.Copy(backupPath, SavePath, true);
                    CanSave = true;
                    Debug.LogWarning("Recovered save data from backup.", this);
                    return true;
                }
                catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
                {
                    Debug.LogError($"Could not restore save backup: {exception.Message}", this);
                }
            }
            // Preserve damaged or newer-version saves instead of replacing them with defaults.
            CanSave = false;
            Debug.LogError("Save data could not be loaded. Saving is disabled to preserve the existing files.", this);
            return false;
        }

        private bool TryRead(string path, out SaveData data)
        {
            data = null;
            if (!File.Exists(path))
                return false;
            try
            {
                var loaded = new SaveData { version = 0 };
                JsonUtility.FromJsonOverwrite(File.ReadAllText(path), loaded);
                data = loaded;
                if ((loaded.version != 1 && loaded.version != SaveData.CurrentVersion) || loaded.achievements == null ||
                    loaded.coins < 0 || !IsVolumeValid(loaded.bgmVolume) || !IsVolumeValid(loaded.effectsVolume))
                    throw new InvalidDataException("Invalid save data or unsupported save version.");
                var ids = new HashSet<string>();
                foreach (var progress in loaded.achievements)
                {
                    if (progress == null || string.IsNullOrWhiteSpace(progress.id) || progress.value < 0 || !ids.Add(progress.id))
                        throw new InvalidDataException("Invalid achievement progress.");
                }
                // FromJsonOverwrite retains constructor defaults for fields absent in v1.
                // The existing wallet, achievement and audio data are kept intact.
                loaded.version = SaveData.CurrentVersion;
                ValidateCollectionData(loaded);
                CharacterCatalog.EvaluateUnlocks(loaded);
                data = loaded;
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException ||
                exception is ArgumentException || exception is InvalidDataException)
            {
                Debug.LogWarning($"Could not read '{path}': {exception.Message}", this);
                return false;
            }
        }

        public bool Save()
        {
            if (!CanSave)
                return false;
            try
            {
                Directory.CreateDirectory(Application.persistentDataPath);
                string temporaryPath = SavePath + ".tmp";
                File.WriteAllText(temporaryPath, JsonUtility.ToJson(Data, true));
                if (File.Exists(SavePath))
                    File.Replace(temporaryPath, SavePath, SavePath + ".bak");
                else
                    File.Move(temporaryPath, SavePath);
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogError($"Could not save game data: {exception.Message}", this);
                return false;
            }
            // A broken UI listener must not turn a completed disk write into a
            // failed transaction or roll the wallet back only in memory.
            if (DataChanged != null)
            {
                foreach (Action listener in DataChanged.GetInvocationList())
                {
                    try { listener(); }
                    catch (Exception exception) { Debug.LogException(exception, this); }
                }
            }
            return true;
        }

        /// <summary>Commit a complete candidate, or keep the original data if persistence fails.</summary>
        public bool TryUpdate(Action<SaveData> update)
        {
            if (update == null)
                throw new ArgumentNullException(nameof(update));
            if (!CanSave)
                return false;
            SaveData original = Data;
            var candidate = JsonUtility.FromJson<SaveData>(JsonUtility.ToJson(original));
            update(candidate);
            CharacterCatalog.EvaluateUnlocks(candidate);
            ValidateCollectionData(candidate);
            Data = candidate;
            if (Save())
                return true;
            Data = original;
            return false;
        }

        private static void ValidateCollectionData(SaveData data)
        {
            if (data.coins < 0 || data.totalCoinsCollected < 0 || data.totalForwardCells < 0 || data.bestDistance < 0 ||
                data.unlockedCharacterIds == null || data.unlockedSkinIds == null)
                throw new InvalidDataException("Invalid wallet or collection progress.");
            var characterIds = new HashSet<string>();
            foreach (string id in data.unlockedCharacterIds)
                if (string.IsNullOrWhiteSpace(id) || !characterIds.Add(id))
                    throw new InvalidDataException("Invalid character unlock list.");
            var skinIds = new HashSet<string>();
            foreach (string id in data.unlockedSkinIds)
                if (string.IsNullOrWhiteSpace(id) || !skinIds.Add(id))
                    throw new InvalidDataException("Invalid skin unlock list.");
            if (!characterIds.Contains(CharacterCatalog.StarterId))
                data.unlockedCharacterIds.Add(CharacterCatalog.StarterId);
            if (CharacterCatalog.Find(data.selectedCharacterId) == null || !data.unlockedCharacterIds.Contains(data.selectedCharacterId))
                data.selectedCharacterId = CharacterCatalog.StarterId;
            if (!CharacterCatalog.CanUseSkin(data, data.selectedCharacterId, data.selectedSkinId))
                data.selectedSkinId = CharacterCatalog.DefaultSkinId;
        }

        private static bool IsVolumeValid(float value) => !float.IsNaN(value) && value >= 0f && value <= 1f;

        private void OnApplicationPause(bool paused)
        {
            if (paused && Instance == this)
                Save();
        }

        private void OnApplicationQuit()
        {
            if (Instance == this)
                Save();
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
