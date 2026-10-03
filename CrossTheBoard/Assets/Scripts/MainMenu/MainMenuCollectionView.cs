using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace CrossTheBoard.UI
{
    public sealed class MainMenuCollectionView : IDisposable
    {
        private static readonly Color Panel = new(0.10f, 0.141f, 0.196f);
        private static readonly Color Card = new(0.145f, 0.196f, 0.255f);
        private static readonly Color Lime = new(0.788f, 0.961f, 0.361f);
        private static readonly Color Cream = new(0.96f, 0.945f, 0.91f);
        private static readonly Color Muted = new(0.56f, 0.63f, 0.72f);
        private readonly Font _font;
        private readonly SaveManager _save;
        private readonly List<CharacterCard> _characterCards = new();
        private readonly List<ShopCard> _shopCards = new();
        private Text _wallet;
        private Text _status;
        private Text _previewName;
        private Text _previewDetails;
        private Image _previewImage;
        private Button _confirm;
        private Text _confirmLabel;
        private Text _homeCharacter;
        private Image _homeImage;
        private string _previewId;
        public GameObject[] Sections { get; }
        public Button[] Navigation { get; }

        public MainMenuCollectionView(MainMenuBootstrap menu, GameObject[] sections, Button[] navigation, Font font)
        {
            _font = font;
            _save = SaveManager.Instance;
            _previewId = _save.Data.selectedCharacterId;
            Transform canvas = navigation[0].GetComponentInParent<Canvas>().transform;
            var walletPanel = Area("Wallet", canvas, 0.055f, 0.934f, 0.40f, 0.975f);
            Paint(walletPanel, Panel);
            Picture(walletPanel, "Coin", PlaceholderSprites.Coin(), 0.035f, 0.16f, 0.19f, 0.84f);
            _wallet = Copy(walletPanel, "Coin total", "", 32, 0.24f, 0.05f, 0.96f, 0.95f, TextAnchor.MiddleLeft);
            _status = Copy(canvas, "Collection status", "", 23, 0.055f, 0.143f, 0.945f, 0.18f);

            var collection = Area("Characters", sections[0].transform.parent, 0, 0, 1, 1);
            Paint(collection, Panel);
            BuildCharacters(collection);
            BuildShop((RectTransform)sections[1].transform);
            BuildHome((RectTransform)sections[0].transform);

            var characterButton = UnityEngine.Object.Instantiate(navigation[0], navigation[0].transform.parent);
            characterButton.name = "Characters Button";
            characterButton.GetComponentInChildren<Text>().text = "캐릭터";
            Sections = new[] { sections[0], collection.gameObject, sections[1], sections[2], sections[3] };
            Navigation = new[] { navigation[0], characterButton, navigation[1], navigation[2], navigation[3] };
            for (int i = 0; i < Navigation.Length; i++)
            {
                var rect = (RectTransform)Navigation[i].transform;
                rect.anchorMin = new Vector2((float)i / Navigation.Length + 0.007f, 0.12f);
                rect.anchorMax = new Vector2((float)(i + 1) / Navigation.Length - 0.007f, 0.88f);
                rect.offsetMin = rect.offsetMax = Vector2.zero;
                Navigation[i].GetComponentInChildren<Text>().fontSize = 30;
                Navigation[i].onClick = new Button.ButtonClickedEvent();
                int index = i;
                Navigation[i].onClick.AddListener(() => { _status.text = ""; menu.SelectSection(index); });
            }
            collection.gameObject.SetActive(false);
            _save.DataChanged += Refresh;
            Refresh();
        }

        private void BuildCharacters(RectTransform panel)
        {
            Copy(panel, "Heading", "캐릭터 컬렉션", 38, 0.04f, 0.915f, 0.96f, 0.98f, TextAnchor.MiddleLeft);
            Copy(panel, "Hint", "해금된 캐릭터를 고른 뒤 선택 버튼으로 확정하세요.", 22, 0.04f, 0.86f, 0.96f, 0.91f, TextAnchor.MiddleLeft).color = Muted;
            _previewImage = Picture(panel, "Character preview", null, 0.34f, 0.61f, 0.66f, 0.85f);
            _previewName = Copy(panel, "Preview name", "", 32, 0.08f, 0.55f, 0.92f, 0.61f);
            var confirmRect = Area("Confirm character", panel, 0.28f, 0.47f, 0.72f, 0.545f);
            _confirm = MakeButton(confirmRect, Lime, ConfirmCharacter);
            _confirmLabel = Copy(confirmRect, "Label", "선택", 28, 0, 0, 1, 1);
            _confirmLabel.color = Panel;
            _previewDetails = Copy(panel, "Preview details", "", 20, 0.05f, 0.408f, 0.95f, 0.46f);
            _previewDetails.color = Muted;
            for (int i = 0; i < CharacterCatalog.Characters.Count; i++)
            {
                var character = CharacterCatalog.Characters[i];
                int column = i % 5;
                int row = i / 5;
                float left = 0.03f + column * 0.19f;
                float bottom = 0.035f + (1 - row) * 0.18f;
                var rect = Area(character.Id, panel, left, bottom, left + 0.18f, bottom + 0.17f);
                var button = MakeButton(rect, Card, () => PreviewCharacter(character.Id));
                var portrait = Picture(rect, "Portrait", CharacterCatalog.GetSprite(character.Id), 0.21f, 0.39f, 0.79f, 0.96f);
                var name = Copy(rect, "Name", character.Name, 19, 0.03f, 0.24f, 0.97f, 0.42f);
                var state = Copy(rect, "State", "", 16, 0.04f, 0.015f, 0.96f, 0.26f);
                _characterCards.Add(new CharacterCard { Id = character.Id, Button = button, Portrait = portrait, Name = name, State = state });
            }
        }

        private void BuildShop(RectTransform panel)
        {
            // Hide placeholder headings in memory; the authored scene is preserved.
            foreach (Transform child in panel) child.gameObject.SetActive(false);
            Copy(panel, "Shop heading", "코인 상점", 42, 0.05f, 0.92f, 0.95f, 0.985f, TextAnchor.MiddleLeft);
            Copy(panel, "Shop hint", "캐릭터 구매 · 해금 조건은 상점 구매입니다.", 23, 0.05f, 0.865f, 0.95f, 0.92f, TextAnchor.MiddleLeft).color = Muted;
            int index = 0;
            foreach (var character in CharacterCatalog.Characters)
            {
                if (character.UnlockCondition != CharacterUnlockCondition.ShopPurchase) continue;
                int column = index % 2;
                int row = index / 2;
                float left = 0.05f + column * 0.46f;
                float bottom = 0.06f + (1 - row) * 0.39f;
                var rect = Area("Buy " + character.Id, panel, left, bottom, left + 0.44f, bottom + 0.37f);
                Paint(rect, Card);
                Picture(rect, "Portrait", CharacterCatalog.GetSprite(character.Id), 0.03f, 0.28f, 0.39f, 0.93f);
                Copy(rect, "Name", character.Name, 28, 0.43f, 0.62f, 0.96f, 0.92f);
                Copy(rect, "Condition", "상점 구매", 21, 0.43f, 0.40f, 0.96f, 0.62f).color = Muted;
                var buyRect = Area("Purchase", rect, 0.06f, 0.05f, 0.94f, 0.30f);
                var button = MakeButton(buyRect, Lime, () => UpdateSave(data => CharacterCatalog.PurchaseCharacter(data, character.Id), "캐릭터를 구매했습니다. 캐릭터 탭에서 선택하세요."));
                var label = Copy(buyRect, "Label", "", 25, 0, 0, 1, 1);
                label.color = Panel;
                _shopCards.Add(new ShopCard { Id = character.Id, Price = character.Price, Button = button, Label = label });
                index++;
            }
        }

        private void BuildHome(RectTransform panel)
        {
            foreach (Transform child in panel)
            {
                var text = child.GetComponent<Text>();
                if (text == null || ((RectTransform)child).anchorMin.y > 0.66f) continue;
                var subtitle = (RectTransform)child;
                subtitle.anchorMin = new Vector2(0.08f, 0.57f);
                subtitle.anchorMax = new Vector2(0.92f, 0.66f);
                text.fontSize = 25;
            }
            _homeImage = Picture(panel, "Equipped character", null, 0.35f, 0.32f, 0.65f, 0.54f);
            _homeCharacter = Copy(panel, "Equipped label", "", 26, 0.04f, 0.015f, 0.96f, 0.08f);
        }

        public void PreviewCharacter(string id)
        {
            if (!CharacterCatalog.IsUnlocked(_save.Data, id)) return;
            _previewId = id;
            Refresh();
        }

        public void ConfirmCharacter()
        {
            UpdateSave(data => CharacterCatalog.SelectCharacter(data, _previewId), "선택 완료 · 다음 게임에 적용됩니다.");
        }

        private void UpdateSave(Action<SaveData> update, string success)
        {
            try
            {
                _status.text = _save.TryUpdate(update) ? success : "저장하지 못했습니다. 구매/선택은 적용되지 않았습니다.";
            }
            catch (InvalidOperationException exception) { _status.text = exception.Message; }
        }

        public void Refresh()
        {
            var data = _save.Data;
            _wallet.text = data.coins.ToString("N0");
            var current = CharacterCatalog.Find(data.selectedCharacterId) ?? CharacterCatalog.Find(CharacterCatalog.StarterId);
            _homeImage.sprite = CharacterCatalog.GetSprite(current.Id);
            _homeCharacter.text = $"다음 게임: {current.Name}";
            if (!CharacterCatalog.IsUnlocked(data, _previewId)) _previewId = current.Id;
            var preview = CharacterCatalog.Find(_previewId);
            _previewImage.sprite = CharacterCatalog.GetSprite(preview.Id);
            _previewName.text = preview.Name;
            _previewDetails.text = preview.ConditionLabel;
            _confirm.interactable = _save.CanSave && CharacterCatalog.IsUnlocked(data, preview.Id);
            _confirmLabel.text = preview.Id == current.Id ? "선택됨 · 다음 게임에 적용" : "이 캐릭터 선택";
            foreach (var card in _characterCards)
            {
                bool owned = CharacterCatalog.IsUnlocked(data, card.Id);
                var definition = CharacterCatalog.Find(card.Id);
                card.Button.interactable = owned;
                var colors = card.Button.colors;
                colors.normalColor = card.Id == _previewId ? new Color(0.27f, 0.37f, 0.22f) : Card;
                colors.selectedColor = colors.normalColor;
                colors.highlightedColor = Color.Lerp(colors.normalColor, Color.white, 0.16f);
                card.Button.colors = colors;
                card.Portrait.color = owned ? Color.white : new Color(0.34f, 0.37f, 0.41f);
                card.Name.color = owned ? Cream : Muted;
                card.State.color = owned ? Lime : Muted;
                card.State.text = owned ? (card.Id == current.Id ? "선택됨" : "해금 완료") : "잠김\n" + definition.ConditionLabel;
            }
            foreach (var card in _shopCards)
            {
                bool owned = CharacterCatalog.IsUnlocked(data, card.Id);
                card.Label.text = owned ? "보유 중" : $"{card.Price:N0} 코인 · 구매";
                card.Button.interactable = _save.CanSave && !owned && data.coins >= card.Price;
                card.Label.color = card.Button.interactable ? Panel : Cream;
                if (!owned && data.coins < card.Price) card.Label.text = $"{card.Price:N0} 코인 · 코인 부족";
            }
        }

        public void Dispose() => _save.DataChanged -= Refresh;

        private static RectTransform Area(string name, Transform parent, float left, float bottom, float right, float top)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = parent.gameObject.layer;
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = new Vector2(left, bottom);
            rect.anchorMax = new Vector2(right, top);
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            return rect;
        }

        private static Image Paint(RectTransform rect, Color color)
        {
            var image = rect.gameObject.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
            return image;
        }

        private Text Copy(Transform parent, string name, string value, int size, float left, float bottom, float right, float top, TextAnchor alignment = TextAnchor.MiddleCenter)
        {
            var rect = Area(name, parent, left, bottom, right, top);
            var text = rect.gameObject.AddComponent<Text>();
            text.font = _font;
            text.fontSize = size;
            text.text = value;
            text.color = Cream;
            text.alignment = alignment;
            text.raycastTarget = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = Mathf.Max(13, size - 5);
            text.resizeTextMaxSize = size;
            return text;
        }

        private static Image Picture(Transform parent, string name, Sprite sprite, float left, float bottom, float right, float top)
        {
            var rect = Area(name, parent, left, bottom, right, top);
            var image = Paint(rect, Color.white);
            image.sprite = sprite;
            image.preserveAspect = true;
            return image;
        }

        private static Button MakeButton(RectTransform rect, Color color, UnityEngine.Events.UnityAction action)
        {
            var image = Paint(rect, Color.white);
            image.raycastTarget = true;
            var button = rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            var colors = button.colors;
            colors.normalColor = color;
            colors.highlightedColor = Color.Lerp(color, Color.white, 0.16f);
            colors.selectedColor = colors.highlightedColor;
            colors.pressedColor = Color.Lerp(color, Color.black, 0.15f);
            colors.disabledColor = new Color(0.17f, 0.19f, 0.23f);
            button.colors = colors;
            button.onClick.AddListener(action);
            return button;
        }

        private sealed class CharacterCard
        {
            public string Id;
            public Button Button;
            public Image Portrait;
            public Text Name;
            public Text State;
        }

        private sealed class ShopCard
        {
            public string Id;
            public int Price;
            public Button Button;
            public Text Label;
        }
    }
}
