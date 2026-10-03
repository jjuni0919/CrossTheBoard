using System;
using UnityEngine;

namespace CrossTheBoard
{
    [DefaultExecutionOrder(-100)]
    public sealed class AchievementTracker : MonoBehaviour
    {
        [SerializeField] private AchievementDatabase _database;
        public static AchievementTracker Instance { get; private set; }
        public AchievementDatabase Database => _database;
        public event Action ProgressChanged;

        public float CompletionRate
        {
            get
            {
                int completed = 0;
                foreach (var definition in _database.Achievements)
                {
                    if (IsAchieved(definition.id))
                        completed++;
                }
                return _database.Achievements.Count == 0 ? 0f : (float)completed / _database.Achievements.Count;
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetInstance() => Instance = null;

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _database.Validate();
            Instance = this;
            DontDestroyOnLoad(gameObject);
        }

        public int GetProgress(string id) => SaveManager.Instance.Data.achievements.Find(item => item.id == id)?.value ?? 0;

        public bool IsAchieved(string id) => SaveManager.Instance.Data.achievements.Find(item => item.id == id)?.achieved ?? false;

        public bool AddProgress(string id, int amount = 1)
        {
            if (amount <= 0)
                throw new ArgumentOutOfRangeException(nameof(amount));
            AchievementDefinition definition = null;
            foreach (var item in _database.Achievements)
            {
                if (item.id == id)
                {
                    definition = item;
                    break;
                }
            }
            if (definition == null)
                throw new ArgumentException($"Achievement '{id}' is not registered.", nameof(id));
            var save = SaveManager.Instance;
            if (!save.CanSave)
                return false;
            if (IsAchieved(id))
                return true;
            if (!save.TryUpdate(data =>
            {
                var progress = data.achievements.Find(item => item.id == id);
                if (progress == null)
                {
                    progress = new AchievementProgress { id = id };
                    data.achievements.Add(progress);
                }
                progress.value = (int)Math.Min(definition.target, (long)progress.value + amount);
                progress.achieved = progress.value >= definition.target;
                if (progress.achieved)
                    data.coins = checked(data.coins + definition.reward);
            }))
                return false;
            ProgressChanged?.Invoke();
            return true;
        }

        private void OnDestroy()
        {
            if (Instance == this)
                Instance = null;
        }
    }
}
