using System;
using System.Collections.Generic;

namespace CrossTheBoard
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 1;
        public int version = CurrentVersion;
        public float bgmVolume = 0.8f;
        public float effectsVolume = 0.8f;
        public int coins;
        public List<AchievementProgress> achievements = new();
    }

    [Serializable]
    public sealed class AchievementProgress
    {
        public string id;
        public int value;
        public bool achieved;
    }
}
