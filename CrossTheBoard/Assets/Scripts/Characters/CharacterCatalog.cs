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

    public sealed class SkinDefinition
    {
        public string Id { get; }
        public string CharacterId { get; }
        public string Name { get; }
        public int Price { get; }
        public Color Color { get; }

        public SkinDefinition(string id, string characterId, string name, int price, Color color)
        { Id = id; CharacterId = characterId; Name = name; Price = price; Color = color; }
    }

    /// <summary>Stable IDs are shared by saves, shop entries, skins and gameplay appearances.</summary>
    public static class CharacterCatalog
    {
        public const string StarterId = "slime";
        public const string DefaultSkinId = "default";
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
        private static readonly SkinDefinition[] SkinDefinitions =
        {
            new("slime_sun", StarterId, "햇살 슬라임", 25, new Color(1f, 0.73f, 0.25f)),
            new("slime_moon", StarterId, "밤하늘 슬라임", 35, new Color(0.58f, 0.54f, 0.94f))
        };
        public static IReadOnlyList<CharacterDefinition> Characters { get; } = Array.AsReadOnly(Definitions);
        public static IReadOnlyList<SkinDefinition> Skins { get; } = Array.AsReadOnly(SkinDefinitions);

        public static CharacterDefinition Find(string id) => Array.Find(Definitions, character => character.Id == id);
        public static SkinDefinition FindSkin(string id) => Array.Find(SkinDefinitions, skin => skin.Id == id);
        public static bool IsUnlocked(SaveData data, string id) => Find(id) != null && data.unlockedCharacterIds.Contains(id);
        public static bool CanUseSkin(SaveData data, string characterId, string skinId)
        {
            if (skinId == DefaultSkinId)
                return true;
            var skin = FindSkin(skinId);
            return skin != null && skin.CharacterId == characterId && data.unlockedSkinIds.Contains(skinId);
        }

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

        public static void PurchaseSkin(SaveData data, string id)
        {
            var skin = FindSkin(id);
            if (skin == null || !IsUnlocked(data, skin.CharacterId))
                throw new InvalidOperationException("해당 캐릭터를 먼저 해금해 주세요.");
            if (data.unlockedSkinIds.Contains(id))
                throw new InvalidOperationException("이미 보유한 스킨입니다.");
            if (data.coins < skin.Price)
                throw new InvalidOperationException("코인이 부족합니다.");
            data.coins -= skin.Price;
            data.unlockedSkinIds.Add(id);
        }

        public static void SelectCharacter(SaveData data, string id)
        {
            if (!IsUnlocked(data, id))
                throw new InvalidOperationException("해금되지 않은 캐릭터입니다.");
            if (data.selectedCharacterId != id)
                data.selectedSkinId = DefaultSkinId;
            data.selectedCharacterId = id;
        }

        public static void SelectSkin(SaveData data, string skinId)
        {
            var skin = FindSkin(skinId);
            if (skin == null || !IsUnlocked(data, skin.CharacterId) || !data.unlockedSkinIds.Contains(skinId))
                throw new InvalidOperationException("해금되지 않은 스킨입니다.");
            data.selectedCharacterId = skin.CharacterId;
            data.selectedSkinId = skin.Id;
        }

        public static Sprite GetSprite(string characterId, string skinId = DefaultSkinId)
        {
            var character = Find(characterId) ?? Find(StarterId);
            var skin = FindSkin(skinId);
            Color color = skin != null && skin.CharacterId == character.Id ? skin.Color : character.Color;
            return PlaceholderSprites.Character(character.Id + ":" + skinId, character.PortraitStyle, color);
        }
    }
}
