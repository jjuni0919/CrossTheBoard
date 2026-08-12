using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CrossTheBoard.UI
{
    [DefaultExecutionOrder(-100)]
    public sealed class MainMenuBootstrap : MonoBehaviour
    {
        private static readonly Color Background = Hex("101722");
        private static readonly Color Surface = Hex("1A2432");
        private static readonly Color SurfaceHover = Hex("263447");
        private static readonly Color TextPrimary = Hex("F5F1E8");
        private static readonly Color TextSecondary = Hex("93A2B4");
        private static readonly Color Accent = Hex("C9F55C");

        private readonly List<NavigationItem> _navigationItems = new();
        private Font _font;
        private Sprite _roundedSprite;
        private Canvas _canvas;
        private RectTransform _content;
        private RectTransform _modalLayer;

        private void Awake()
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Malgun Gothic", "Apple SD Gothic Neo", "Segoe UI", "Arial" }, 48);
            _roundedSprite = CreateRoundedSprite();

            CreateEventSystem();
            CreateCanvas();
            CreateBackground();
            CreateLogo();
            CreateContent();
            CreateBottomNavigation();
            CreateModalLayer();
            CreateSettingsButton();
            SelectSection(MenuSection.Home);
        }

        private void Update()
        {
            if (_modalLayer != null && _modalLayer.gameObject.activeSelf &&
                Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                CloseModal();
        }

        private void CreateEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(transform, false);
        }

        private void CreateCanvas()
        {
            var root = new GameObject("Main Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false);
            _canvas = root.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = root.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void CreateBackground()
        {
            var background = CreateRect("Background", _canvas.transform, Vector2.zero, Vector2.one);
            AddImage(background, Background);

            var glow = CreateRect("Top Glow", background, new Vector2(0.18f, 0.62f), new Vector2(0.82f, 1f));
            AddImage(glow, new Color(0.20f, 0.48f, 0.48f, 0.12f));
            glow.GetComponent<Image>().raycastTarget = false;

            var line = CreateRect("Accent Line", background, new Vector2(0.42f, 0.865f), new Vector2(0.58f, 0.869f));
            AddImage(line, Accent);
            line.GetComponent<Image>().raycastTarget = false;
        }

        private void CreateLogo()
        {
            var logo = CreateRect("Logo", _canvas.transform, new Vector2(0.33f, 0.865f), new Vector2(0.67f, 0.975f));
            AddText(logo, "CROSS THE BOARD", 38, TextPrimary, FontStyle.Bold, TextAnchor.MiddleCenter);

            var subtitle = CreateRect("Logo Subtitle", _canvas.transform, new Vector2(0.38f, 0.825f), new Vector2(0.62f, 0.87f));
            AddText(subtitle, "모든 선택이 길을 만든다", 15, TextSecondary, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        private void CreateSettingsButton()
        {
            var settings = CreateRect("Settings Button", _canvas.transform, new Vector2(0.91f, 0.885f), new Vector2(0.965f, 0.975f));
            var button = AddButton(settings, Surface, OpenSettings);
            button.colors = Colors(Surface, SurfaceHover);
            AddButtonLabel(settings, "⚙", 34, TextPrimary);

            var tooltip = CreateRect("Settings Label", _canvas.transform, new Vector2(0.88f, 0.85f), new Vector2(0.995f, 0.885f));
            AddText(tooltip, "설정", 13, TextSecondary, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        private void CreateContent()
        {
            _content = CreateRect("Content", _canvas.transform, new Vector2(0.08f, 0.19f), new Vector2(0.92f, 0.79f));
        }

        private void CreateBottomNavigation()
        {
            var navigation = CreateRect("Bottom Navigation", _canvas.transform, new Vector2(0.17f, 0.035f), new Vector2(0.83f, 0.15f));
            AddImage(navigation, new Color(Surface.r, Surface.g, Surface.b, 0.98f), true);
            AddShadow(navigation.gameObject, new Color(0f, 0f, 0f, 0.38f), new Vector2(0f, -7f));

            AddNavigationItem(navigation, MenuSection.Home, "⌂", "홈", 0);
            AddNavigationItem(navigation, MenuSection.Shop, "◆", "상점", 1);
            AddNavigationItem(navigation, MenuSection.Challenge, "★", "챌린지 모드", 2);
            AddNavigationItem(navigation, MenuSection.Achievements, "♛", "업적", 3);
        }

        private void AddNavigationItem(RectTransform parent, MenuSection section, string icon, string label, int index)
        {
            const float padding = 0.012f;
            float width = (1f - padding * 5f) / 4f;
            float left = padding + index * (width + padding);
            var item = CreateRect(label, parent, new Vector2(left, 0.12f), new Vector2(left + width, 0.88f));
            var button = AddButton(item, new Color(0f, 0f, 0f, 0f), () => SelectSection(section));
            button.colors = Colors(new Color(0f, 0f, 0f, 0f), new Color(1f, 1f, 1f, 0.07f));

            var iconRect = CreateRect("Icon", item, new Vector2(0f, 0.43f), new Vector2(1f, 0.96f));
            var iconText = AddText(iconRect, icon, 27, TextSecondary, FontStyle.Bold, TextAnchor.MiddleCenter);
            var labelRect = CreateRect("Label", item, new Vector2(0f, 0.03f), new Vector2(1f, 0.48f));
            var labelText = AddText(labelRect, label, label.Length > 5 ? 14 : 16, TextSecondary, FontStyle.Bold, TextAnchor.MiddleCenter);
            var indicator = CreateRect("Selected", item, new Vector2(0.31f, -0.02f), new Vector2(0.69f, 0.025f));
            AddImage(indicator, Accent, true);

            _navigationItems.Add(new NavigationItem(section, item.GetComponent<Image>(), iconText, labelText, indicator.gameObject));
        }

        private void SelectSection(MenuSection section)
        {
            foreach (var child in new List<Transform>(GetChildren(_content)))
                Destroy(child.gameObject);

            foreach (var item in _navigationItems)
                item.SetSelected(item.Section == section, Accent, TextSecondary);

            switch (section)
            {
                case MenuSection.Home:
                    BuildSection("다시 오신 것을 환영합니다", "게임을 시작하고 보드 위의 새로운 길을 만들어 보세요.", "게임 시작", Accent);
                    break;
                case MenuSection.Shop:
                    BuildSection("상점", "새로운 말, 보드 테마와 꾸미기 아이템을 만나보세요.", "상품 둘러보기", Hex("A98BFF"));
                    break;
                case MenuSection.Challenge:
                    BuildSection("챌린지 모드", "특별한 규칙과 목표가 있는 스테이지에 도전하세요.", "챌린지 시작", Hex("58D5F7"));
                    break;
                case MenuSection.Achievements:
                    BuildSection("업적", "플레이 기록과 달성한 업적을 한눈에 확인하세요.", "업적 확인", Hex("FF9A74"));
                    break;
            }
        }

        private void BuildSection(string title, string description, string action, Color accent)
        {
            var card = CreateRect("Section Card", _content, new Vector2(0.12f, 0.12f), new Vector2(0.88f, 0.9f));
            AddImage(card, new Color(Surface.r, Surface.g, Surface.b, 0.92f), true);
            AddShadow(card.gameObject, new Color(0f, 0f, 0f, 0.25f), new Vector2(0f, -8f));

            var marker = CreateRect("Marker", card, new Vector2(0.08f, 0.76f), new Vector2(0.095f, 0.88f));
            AddImage(marker, accent, true);
            var heading = CreateRect("Heading", card, new Vector2(0.12f, 0.62f), new Vector2(0.9f, 0.9f));
            AddText(heading, title, 42, TextPrimary, FontStyle.Bold, TextAnchor.MiddleLeft);
            var body = CreateRect("Description", card, new Vector2(0.12f, 0.4f), new Vector2(0.88f, 0.63f));
            AddText(body, description, 20, TextSecondary, FontStyle.Normal, TextAnchor.UpperLeft);
            var actionButton = CreateRect("Primary Action", card, new Vector2(0.12f, 0.14f), new Vector2(0.53f, 0.34f));
            AddButton(actionButton, accent, () => Debug.Log($"{action} selected"));
            AddButtonLabel(actionButton, action + "  ›", 20, Background, FontStyle.Bold);
        }

        private void CreateModalLayer()
        {
            _modalLayer = CreateRect("Modal Layer", _canvas.transform, Vector2.zero, Vector2.one);
            AddImage(_modalLayer, new Color(0f, 0f, 0f, 0.7f));
            _modalLayer.gameObject.SetActive(false);
        }

        private void OpenSettings()
        {
            if (_modalLayer == null)
                CreateModalLayer();

            _modalLayer.gameObject.SetActive(true);
            _modalLayer.SetAsLastSibling();
            foreach (var child in new List<Transform>(GetChildren(_modalLayer)))
                Destroy(child.gameObject);

            var dismiss = _modalLayer.GetComponent<Button>() ?? _modalLayer.gameObject.AddComponent<Button>();
            dismiss.targetGraphic = _modalLayer.GetComponent<Image>();
            dismiss.transition = Selectable.Transition.None;
            dismiss.onClick.RemoveAllListeners();
            dismiss.onClick.AddListener(CloseModal);

            var panel = CreateRect("Settings Panel", _modalLayer, new Vector2(0.31f, 0.2f), new Vector2(0.69f, 0.8f));
            AddImage(panel, Surface, true);
            panel.gameObject.AddComponent<ModalClickBlocker>();
            var title = CreateRect("Title", panel, new Vector2(0.1f, 0.76f), new Vector2(0.72f, 0.91f));
            AddText(title, "설정", 32, TextPrimary, FontStyle.Bold, TextAnchor.MiddleLeft);
            var close = CreateRect("Close", panel, new Vector2(0.82f, 0.8f), new Vector2(0.93f, 0.92f));
            AddButton(close, SurfaceHover, CloseModal);
            AddButtonLabel(close, "×", 28, TextPrimary);

            AddSettingRow(panel, "사운드", "켜짐", 0.62f);
            AddSettingRow(panel, "진동", "켜짐", 0.46f);
            AddSettingRow(panel, "언어", "한국어", 0.30f);
        }

        private void AddSettingRow(RectTransform panel, string label, string value, float y)
        {
            var row = CreateRect(label, panel, new Vector2(0.1f, y), new Vector2(0.9f, y + 0.12f));
            AddImage(row, SurfaceHover, true);
            var name = CreateRect("Name", row, new Vector2(0.05f, 0f), new Vector2(0.55f, 1f));
            AddText(name, label, 17, TextPrimary, FontStyle.Bold, TextAnchor.MiddleLeft);
            var current = CreateRect("Value", row, new Vector2(0.55f, 0f), new Vector2(0.94f, 1f));
            AddText(current, value, 16, Accent, FontStyle.Bold, TextAnchor.MiddleRight);
        }

        private void CloseModal() => _modalLayer.gameObject.SetActive(false);

        private RectTransform CreateRect(string name, Transform parent, Vector2 min, Vector2 max)
        {
            var gameObject = new GameObject(name, typeof(RectTransform));
            var rect = gameObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            return rect;
        }

        private Image AddImage(RectTransform rect, Color color, bool rounded = false)
        {
            var image = rect.GetComponent<Image>() ?? rect.gameObject.AddComponent<Image>();
            image.color = color;
            if (rounded)
            {
                image.sprite = _roundedSprite;
                image.type = Image.Type.Sliced;
            }
            return image;
        }

        private Text AddText(RectTransform rect, string value, int size, Color color, FontStyle style, TextAnchor alignment)
        {
            var text = rect.GetComponent<Text>() ?? rect.gameObject.AddComponent<Text>();
            if (text == null)
                throw new InvalidOperationException($"Text cannot be added directly to '{rect.name}'. Create a child label instead.");
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.supportRichText = true;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            return text;
        }

        private Text AddButtonLabel(
            RectTransform button,
            string value,
            int size,
            Color color,
            FontStyle style = FontStyle.Normal)
        {
            var label = CreateRect("Label", button, Vector2.zero, Vector2.one);
            label.offsetMin = new Vector2(8f, 4f);
            label.offsetMax = new Vector2(-8f, -4f);
            return AddText(label, value, size, color, style, TextAnchor.MiddleCenter);
        }

        private Button AddButton(RectTransform rect, Color normal, UnityEngine.Events.UnityAction action)
        {
            var image = AddImage(rect, Color.white, true);
            var button = rect.GetComponent<Button>() ?? rect.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = Colors(normal, Brighten(normal));
            button.onClick.AddListener(action);
            return button;
        }

        private static IEnumerable<Transform> GetChildren(Transform parent)
        {
            for (int i = 0; i < parent.childCount; i++)
                yield return parent.GetChild(i);
        }

        private static ColorBlock Colors(Color normal, Color highlighted) => new()
        {
            normalColor = normal,
            highlightedColor = highlighted,
            pressedColor = new Color(highlighted.r * 0.82f, highlighted.g * 0.82f, highlighted.b * 0.82f, highlighted.a),
            selectedColor = highlighted,
            disabledColor = new Color(normal.r, normal.g, normal.b, normal.a * 0.4f),
            colorMultiplier = 1f,
            fadeDuration = 0.1f
        };

        private static Color Brighten(Color color) =>
            new(Mathf.Min(1f, color.r * 1.14f), Mathf.Min(1f, color.g * 1.14f), Mathf.Min(1f, color.b * 1.14f), Mathf.Max(color.a, 0.12f));

        private static void AddShadow(GameObject target, Color color, Vector2 distance)
        {
            var shadow = target.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
        }

        private static Sprite CreateRoundedSprite()
        {
            const int size = 64;
            const float radius = 14f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Rounded Rectangle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = Mathf.Max(radius - x, 0f, x - (size - 1f - radius));
                float dy = Mathf.Max(radius - y, 0f, y - (size - 1f - radius));
                float alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
            }
            texture.SetPixels32(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size), Vector2.one * 0.5f, 100f, 0,
                SpriteMeshType.FullRect, new Vector4(16f, 16f, 16f, 16f));
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            return color;
        }

        private enum MenuSection { Home, Shop, Challenge, Achievements }

        private sealed class NavigationItem
        {
            public readonly MenuSection Section;
            private readonly Image _background;
            private readonly Text _icon;
            private readonly Text _label;
            private readonly GameObject _indicator;

            public NavigationItem(MenuSection section, Image background, Text icon, Text label, GameObject indicator)
            {
                Section = section;
                _background = background;
                _icon = icon;
                _label = label;
                _indicator = indicator;
            }

            public void SetSelected(bool selected, Color accent, Color muted)
            {
                _background.color = selected ? new Color(1f, 1f, 1f, 0.07f) : Color.clear;
                _icon.color = selected ? accent : muted;
                _label.color = selected ? accent : muted;
                _indicator.SetActive(selected);
            }
        }
    }

    public sealed class ModalClickBlocker : MonoBehaviour, IPointerClickHandler
    {
        public void OnPointerClick(PointerEventData eventData) => eventData.Use();
    }
}
