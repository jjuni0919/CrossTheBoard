using System;
using System.Collections.Generic;
using UnityEngine;

namespace CrossTheBoard
{
    public enum CharacterUnlockCondition { Default, ShopPurchase, BestDistance, TotalCoinsCollected, TotalForwardCells }

    public sealed class CharacterDefinition
    {
        public string Id { get; }
        public string Name { get; }
        public CharacterUnlockCondition UnlockCondition { get; }
        public int Requirement { get; }
        public int Price { get; }
        public Color Color { get; }
        public int PortraitStyle { get; }

        public CharacterDefinition(string id, string name, CharacterUnlockCondition condition, int requirement, int price, Color color, int portraitStyle)
        {
            Id = id; Name = name; UnlockCondition = condition; Requirement = requirement;
            Price = price; Color = color; PortraitStyle = portraitStyle;
        }

        public string ConditionLabel => UnlockCondition switch
        {
            CharacterUnlockCondition.Default => "기본 캐릭터",
            CharacterUnlockCondition.ShopPurchase => $"상점 구매 · {Price:N0} 코인",
            CharacterUnlockCondition.BestDistance => $"한 게임에서 {Requirement}칸 전진",
            CharacterUnlockCondition.TotalCoinsCollected => $"코인 누적 {Requirement}개 수집",
            CharacterUnlockCondition.TotalForwardCells => $"전체 게임에서 {Requirement}칸 전진",
            _ => string.Empty
        };
    }

    public static class CharacterCatalog
    {
        public const string StarterId = "slime";
        private static readonly CharacterDefinition[] Definitions =
        {
            new(StarterId, "초록 슬라임", CharacterUnlockCondition.Default, 0, 0, new Color(0.50f, 0.86f, 0.37f), 0),
            new("robot", "탐험 로봇", CharacterUnlockCondition.ShopPurchase, 0, 50, new Color(0.42f, 0.78f, 0.96f), 1),
            new("cat", "별빛 고양이", CharacterUnlockCondition.ShopPurchase, 0, 100, new Color(0.69f, 0.55f, 0.96f), 2),
            new("knight", "작은 기사", CharacterUnlockCondition.ShopPurchase, 0, 150, new Color(0.73f, 0.78f, 0.85f), 3),
            new("fox", "사막 여우", CharacterUnlockCondition.ShopPurchase, 0, 200, new Color(0.98f, 0.59f, 0.28f), 4),
            new("frog", "길잡이 개구리", CharacterUnlockCondition.BestDistance, 10, 0, new Color(0.31f, 0.81f, 0.65f), 5),
            new("ghost", "달빛 유령", CharacterUnlockCondition.BestDistance, 25, 0, new Color(0.73f, 0.88f, 0.97f), 6),
            new("mushroom", "황금 버섯", CharacterUnlockCondition.TotalCoinsCollected, 25, 0, new Color(0.98f, 0.77f, 0.27f), 7),
            new("pumpkin", "호박 마법사", CharacterUnlockCondition.TotalCoinsCollected, 100, 0, new Color(0.94f, 0.40f, 0.27f), 8),
            new("golem", "이끼 골렘", CharacterUnlockCondition.TotalForwardCells, 100, 0, new Color(0.53f, 0.68f, 0.51f), 9)
        };
        public static IReadOnlyList<CharacterDefinition> Characters { get; } = Array.AsReadOnly(Definitions);

        public static CharacterDefinition Find(string id) => Array.Find(Definitions, character => character.Id == id);
        public static bool IsUnlocked(SaveData data, string id) => Find(id) != null && data.unlockedCharacterIds.Contains(id);

        public static void EvaluateUnlocks(SaveData data)
        {
            foreach (var character in Definitions)
            {
                bool met = character.UnlockCondition switch
                {
                    CharacterUnlockCondition.Default => true,
                    CharacterUnlockCondition.BestDistance => data.bestDistance >= character.Requirement,
                    CharacterUnlockCondition.TotalCoinsCollected => data.totalCoinsCollected >= character.Requirement,
                    CharacterUnlockCondition.TotalForwardCells => data.totalForwardCells >= character.Requirement,
                    _ => false // A shop character is unlocked only by an explicit purchase.
                };
                if (met && !data.unlockedCharacterIds.Contains(character.Id))
                    data.unlockedCharacterIds.Add(character.Id);
            }
        }

        public static void PurchaseCharacter(SaveData data, string id)
        {
            var character = Find(id);
            if (character == null || character.UnlockCondition != CharacterUnlockCondition.ShopPurchase)
                throw new InvalidOperationException("상점에서 판매하는 캐릭터가 아닙니다.");
            if (IsUnlocked(data, id))
                throw new InvalidOperationException("이미 보유한 캐릭터입니다.");
            if (data.coins < character.Price)
                throw new InvalidOperationException("코인이 부족합니다.");
            data.coins -= character.Price;
            data.unlockedCharacterIds.Add(id);
        }

        public static void SelectCharacter(SaveData data, string id)
        {
            if (!IsUnlocked(data, id))
                throw new InvalidOperationException("해금되지 않은 캐릭터입니다.");
            data.selectedCharacterId = id;
        }

        public static Sprite GetSprite(string characterId)
        {
            var character = Find(characterId) ?? Find(StarterId);
            return PlaceholderSprites.Character(character.Id, character.PortraitStyle, character.Color);
        }
    }
}
