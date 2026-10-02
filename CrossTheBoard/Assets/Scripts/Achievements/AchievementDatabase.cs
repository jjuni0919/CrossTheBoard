using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrossTheBoard
{
    [CreateAssetMenu(menuName = "CrossTheBoard/Achievement Database")]
    public sealed class AchievementDatabase : ScriptableObject
    {
        [SerializeField] private AchievementDefinition[] _achievements = Array.Empty<AchievementDefinition>();
        public IReadOnlyList<AchievementDefinition> Achievements => _achievements;

        public void Validate()
        {
            if (_achievements == null)
                throw new InvalidOperationException($"Achievement list is missing in '{name}'.");
            var ids = new HashSet<string>();
            foreach (var achievement in _achievements)
            {
                if (achievement == null || string.IsNullOrWhiteSpace(achievement.id) ||
                    string.IsNullOrWhiteSpace(achievement.goal) || achievement.target <= 0 ||
                    achievement.reward < 0 || !ids.Add(achievement.id))
                    throw new InvalidOperationException($"Invalid or duplicate achievement in '{name}'.");
            }
        }
    }

    [Serializable]
    public sealed class AchievementDefinition
    {
        public string id;
        public string goal;
        [Min(1)] public int target = 1;
        [Min(0), Tooltip("Coins granted once when this achievement is completed.")] public int reward;
    }
}
