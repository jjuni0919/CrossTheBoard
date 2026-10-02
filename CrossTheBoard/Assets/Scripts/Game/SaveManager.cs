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
                if (loaded.version != SaveData.CurrentVersion || loaded.achievements == null ||
                    loaded.coins < 0 || !IsVolumeValid(loaded.bgmVolume) || !IsVolumeValid(loaded.effectsVolume))
                    throw new InvalidDataException("Invalid save data or unsupported save version.");
                var ids = new HashSet<string>();
                foreach (var progress in loaded.achievements)
                {
                    if (progress == null || string.IsNullOrWhiteSpace(progress.id) || progress.value < 0 || !ids.Add(progress.id))
                        throw new InvalidDataException("Invalid achievement progress.");
                }
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
                return true;
            }
            catch (Exception exception) when (exception is IOException || exception is UnauthorizedAccessException)
            {
                Debug.LogError($"Could not save game data: {exception.Message}", this);
                return false;
            }
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
