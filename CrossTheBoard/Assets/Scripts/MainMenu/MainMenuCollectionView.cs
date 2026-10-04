using System;
using UnityEngine;
using UnityEngine.UI;

namespace CrossTheBoard.UI
{
    public sealed class MainMenuCollectionView : MonoBehaviour
    {
        private static readonly Color Panel = new(0.10f, 0.141f, 0.196f);
        private static readonly Color Card = new(0.145f, 0.196f, 0.255f);
        private static readonly Color Lime = new(0.788f, 0.961f, 0.361f);
        private static readonly Color Cream = new(0.96f, 0.945f, 0.91f);
        private static readonly Color Muted = new(0.56f, 0.63f, 0.72f);
        [SerializeField] private CharacterCard[] _characterCards;
        [SerializeField] private ShopCard[] _shopCards;
        [SerializeField] private Text _wallet;
        [SerializeField] private Text _status;
        [SerializeField] private Text _previewName;
        [SerializeField] private Text _previewDetails;
        [SerializeField] private Image _previewImage;
        [SerializeField] private Button _confirm;
        [SerializeField] private Text _confirmLabel;
        [SerializeField] private Text _homeCharacter;
        [SerializeField] private Image _homeImage;

        private SaveManager _save;
        private string _previewId;

        private void Awake()
        {
            _save = SaveManager.Instance;
            _previewId = _save.Data.selectedCharacterId;
        }

        private void Start()
        {
            _save.DataChanged += Refresh;
            Refresh();
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

        public void PurchaseCharacter(string id)
        {
            UpdateSave(data => CharacterCatalog.PurchaseCharacter(data, id), "캐릭터를 구매했습니다. 캐릭터 탭에서 선택하세요.");
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
            _homeCharacter.text = $"다음 게임: {current.Name}";
            if (!CharacterCatalog.IsUnlocked(data, _previewId)) _previewId = current.Id;
            var preview = CharacterCatalog.Find(_previewId);
            _previewName.text = preview.Name;
            _previewDetails.text = preview.ConditionLabel;
            _confirm.interactable = _save.CanSave && CharacterCatalog.IsUnlocked(data, preview.Id);
            _confirmLabel.text = preview.Id == current.Id ? "선택됨 · 다음 게임에 적용" : "이 캐릭터 선택";
            foreach (var card in _characterCards)
            {
                bool owned = CharacterCatalog.IsUnlocked(data, card.Id);
                var definition = CharacterCatalog.Find(card.Id);
                if (card.Id == current.Id) _homeImage.sprite = card.Portrait.sprite;
                if (card.Id == preview.Id) _previewImage.sprite = card.Portrait.sprite;
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
                int price = CharacterCatalog.Find(card.Id).Price;
                card.Label.text = owned ? "보유 중" : $"{price:N0} 코인 · 구매";
                card.Button.interactable = _save.CanSave && !owned && data.coins >= price;
                card.Label.color = card.Button.interactable ? Panel : Cream;
                if (!owned && data.coins < price) card.Label.text = $"{price:N0} 코인 · 코인 부족";
            }
        }

        private void OnDestroy()
        {
            if (_save != null) _save.DataChanged -= Refresh;
        }

        [Serializable]
        private sealed class CharacterCard
        {
            public string Id;
            public Button Button;
            public Image Portrait;
            public Text Name;
            public Text State;
        }

        [Serializable]
        private sealed class ShopCard
        {
            public string Id;
            public Button Button;
            public Text Label;
        }
    }
}
