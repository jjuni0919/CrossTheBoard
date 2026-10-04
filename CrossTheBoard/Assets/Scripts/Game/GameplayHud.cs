using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;

namespace CrossTheBoard
{
    public sealed class GameplayHud : MonoBehaviour
    {
        private GameplayController _gameplay;
        private SaveManager _save;
        private PlayerController _player;
        private Text _score;
        private Text _coins;
        private Text _health;
        private Font _font;

        public static void Create(GameplayController gameplay)
        {
            var hud = new GameObject("Gameplay HUD", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster), typeof(GameplayHud));
            var view = hud.GetComponent<GameplayHud>();
            view.Initialize(gameplay);
        }

        private void Initialize(GameplayController gameplay)
        {
            _gameplay = gameplay;
            _player = gameplay.Player;
            _save = SaveManager.Instance;
            _font = Font.CreateDynamicFontFromOSFont(new[] { "Malgun Gothic", "Noto Sans CJK KR", "Arial" }, 32);
            GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            GetComponent<Canvas>().sortingOrder = 110;
            var scaler = GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080, 1920);
            scaler.matchWidthOrHeight = 0.5f;
            if (FindFirstObjectByType<EventSystem>() == null)
            {
                var events = new GameObject("Gameplay EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
                events.transform.SetParent(transform, false);
            }
            _score = Label("Score", new Vector2(0.055f, 0.945f), new Vector2(0.47f, 0.98f));
            _coins = Label("Coins", new Vector2(0.055f, 0.915f), new Vector2(0.47f, 0.945f));
            _health = Label("Health", new Vector2(0.5f, 0.945f), new Vector2(0.73f, 0.98f));
            var back = new GameObject("Back to menu", typeof(RectTransform), typeof(Image), typeof(Button));
            var backRect = (RectTransform)back.transform;
            backRect.SetParent(transform, false);
            backRect.anchorMin = new Vector2(0.76f, 0.935f);
            backRect.anchorMax = new Vector2(0.945f, 0.975f);
            backRect.offsetMin = backRect.offsetMax = Vector2.zero;
            var background = back.GetComponent<Image>();
            background.color = new Color(0.10f, 0.14f, 0.20f);
            var button = back.GetComponent<Button>();
            button.targetGraphic = background;
            button.onClick.AddListener(() =>
            {
                if (SceneLoadManager.Instance.LoadScene("MainMenuScene", GameState.MainMenu))
                    button.interactable = false;
            });
            var label = Label("Back label", new Vector2(0.76f, 0.935f), new Vector2(0.945f, 0.975f));
            label.text = "메뉴";
            label.alignment = TextAnchor.MiddleCenter;
            _gameplay.ScoreChanged += RefreshScore;
            _player.HealthChanged += RefreshHealth;
            if (_save != null) _save.DataChanged += RefreshCoins;
            RefreshScore(_gameplay.Score);
            RefreshHealth(_player.Health);
            RefreshCoins();
        }

        private Text Label(string name, Vector2 min, Vector2 max)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            var rect = (RectTransform)go.transform;
            rect.SetParent(transform, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = rect.offsetMax = Vector2.zero;
            var text = go.GetComponent<Text>();
            text.font = _font;
            text.fontSize = 30;
            text.color = new Color(0.96f, 0.945f, 0.91f);
            text.alignment = TextAnchor.MiddleLeft;
            text.raycastTarget = false;
            return text;
        }

        private void RefreshScore(int value) => _score.text = $"점수  {value:N0}";
        private void RefreshHealth(int value) => _health.text = $"HP  {value}/{_player.MaxHealth}";
        private void RefreshCoins() => _coins.text = $"코인  {(_save != null ? _save.Data.coins : 0):N0}";

        private void OnDestroy()
        {
            if (_gameplay != null) _gameplay.ScoreChanged -= RefreshScore;
            if (_player != null) _player.HealthChanged -= RefreshHealth;
            if (_save != null) _save.DataChanged -= RefreshCoins;
            if (_font != null) Destroy(_font);
        }
    }
}
