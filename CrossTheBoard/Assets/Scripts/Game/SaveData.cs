using System;
using System.Collections.Generic;

namespace CrossTheBoard
{
    [Serializable]
    public sealed class SaveData
    {
        public const int CurrentVersion = 4;
        public int version = CurrentVersion;
        public float bgmVolume = 0.8f;
        public float effectsVolume = 0.8f;
        public int coins;
        public List<AchievementProgress> achievements = new();
        public string selectedCharacterId = CharacterCatalog.StarterId;
        public List<string> unlockedCharacterIds = new() { CharacterCatalog.StarterId };
        public int totalCoinsCollected;
        public int totalForwardCells;
        public int bestDistance;
        public int lastScore;
        public int bestScore;
        public int lastRunCoins;
    }

    [Serializable]
    public sealed class AchievementProgress
    {
        public string id;
        public int value;
        public bool achieved;
    }
}
