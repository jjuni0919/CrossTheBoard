using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace CrossTheBoard.UI
{
    /// <summary>
    /// Creates the main menu at runtime so the scene stays lightweight and the menu can be
    /// moved to a dedicated scene later without rebuilding the visual hierarchy by hand.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class MainMenuBootstrap : MonoBehaviour
    {
        private static readonly Color Ink = Hex("10151F");
        private static readonly Color Panel = Hex("19212D");
        private static readonly Color PanelBright = Hex("222D3C");
        private static readonly Color Cream = Hex("F3EFE4");
        private static readonly Color Muted = Hex("91A0AF");
        private static readonly Color Lime = Hex("C9F55C");
        private static readonly Color Sky = Hex("58D5F7");
        private static readonly Color Coral = Hex("FF7D68");
        private static readonly Color Violet = Hex("A98BFF");

        private Font _font;
        private Sprite _roundedSprite;
        private Canvas _canvas;
        private RectTransform _overlay;
        private GameObject _dialog;
        private Text _sectionLabel;
        private readonly List<MenuCard> _menuCards = new();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void CreateMenu()
        {
            if (FindFirstObjectByType<MainMenuBootstrap>() != null)
                return;

            new GameObject("Main Menu UI").AddComponent<MainMenuBootstrap>();
        }

        private void Awake()
        {
            _font = Font.CreateDynamicFontFromOSFont(
                new[] { "Segoe UI", "Arial", "Liberation Sans" }, 48);
            _roundedSprite = CreateRoundedSprite();
            BuildEventSystem();
            BuildCanvas();
            BuildBackground();
            BuildHeader();
            BuildHero();
            BuildNavigation();
            BuildFooter();
            BuildOverlay();
        }

        private void Update()
        {
            if (_dialog != null && UnityEngine.InputSystem.Keyboard.current?.escapeKey.wasPressedThisFrame == true)
                CloseSection();
        }

        private void BuildEventSystem()
        {
            if (FindFirstObjectByType<EventSystem>() != null)
                return;

            var eventSystem = new GameObject("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            eventSystem.transform.SetParent(transform, false);
        }

        private void BuildCanvas()
        {
            var canvasObject = new GameObject("Main Menu Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            _canvas = canvasObject.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvas.sortingOrder = 100;

            var scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920, 1080);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;
        }

        private void BuildBackground()
        {
            var background = Rect("Background", _canvas.transform, Vector2.zero, Vector2.one);
            Image(background, Ink);

            var upperGlow = Rect("Upper glow", background, new Vector2(0f, 0.48f), new Vector2(0.58f, 1f));
            Image(upperGlow, new Color(0.08f, 0.31f, 0.33f, 0.28f));

            var rightShade = Rect("Right shade", background, new Vector2(0.58f, 0f), Vector2.one);
            Image(rightShade, new Color(0.025f, 0.035f, 0.055f, 0.6f));

            var board = Rect("Board route", background, Vector2.zero, Vector2.one);
            var route = board.gameObject.AddComponent<BoardRouteGraphic>();
            route.raycastTarget = false;
            route.color = new Color(0.45f, 0.88f, 0.8f, 0.16f);

            CreateBoardToken(background, new Vector2(0.08f, 0.26f), 24f, Sky, 0.35f);
            CreateBoardToken(background, new Vector2(0.27f, 0.71f), 14f, Lime, 0.32f);
            CreateBoardToken(background, new Vector2(0.51f, 0.32f), 18f, Coral, 0.28f);
            CreateBoardToken(background, new Vector2(0.84f, 0.83f), 12f, Violet, 0.3f);
        }

        private void CreateBoardToken(Transform parent, Vector2 anchor, float size, Color color, float speed)
        {
            var token = Rect("Floating token", parent, anchor, anchor, new Vector2(size, size));
            token.anchoredPosition = Vector2.zero;
            var image = Image(token, color, true);
            image.raycastTarget = false;
            var floater = token.gameObject.AddComponent<AmbientFloat>();
            floater.amplitude = 9f;
            floater.speed = speed;
            floater.phase = anchor.x * 8f;
        }

        private void BuildHeader()
        {
            var header = Rect("Header", _canvas.transform, new Vector2(0.045f, 0.875f), new Vector2(0.955f, 0.965f));

            var mark = Rect("Logo mark", header, new Vector2(0f, 0.16f), new Vector2(0.047f, 0.84f));
            Image(mark, Lime, true);
            Text(mark, "CB", 22, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);

            var brand = Rect("Brand", header, new Vector2(0.058f, 0f), new Vector2(0.28f, 1f));
            Text(brand, "CROSS THE BOARD", 24, Cream, FontStyle.Bold, TextAnchor.MiddleLeft, 2f);

            var version = Rect("Version", header, new Vector2(0.255f, 0f), new Vector2(0.41f, 1f));
            Text(version, "SEASON 01  /  DAY 12", 14, Muted, FontStyle.Normal, TextAnchor.MiddleLeft, 1.5f);

            var currency = Rect("Currency", header, new Vector2(0.755f, 0.18f), new Vector2(0.845f, 0.82f));
            Image(currency, new Color(1f, 1f, 1f, 0.055f), true);
            Text(currency, "◆  1,240", 16, Cream, FontStyle.Bold, TextAnchor.MiddleCenter);

            var profile = Rect("Profile", header, new Vector2(0.858f, 0.08f), Vector2.one);
            var avatar = Rect("Avatar", profile, new Vector2(0f, 0.08f), new Vector2(0.28f, 0.92f));
            Image(avatar, Sky, true);
            Text(avatar, "P1", 17, Ink, FontStyle.Bold, TextAnchor.MiddleCenter);
            var player = Rect("Player", profile, new Vector2(0.34f, 0f), Vector2.one);
            Text(player, "PLAYER 01\n<size=13><color=#91A0AF>LEVEL 08</color></size>", 17, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
        }

        private void BuildHero()
        {
            var hero = Rect("Hero", _canvas.transform, new Vector2(0.055f, 0.16f), new Vector2(0.575f, 0.84f));

            var eyebrow = Rect("Eyebrow", hero, new Vector2(0f, 0.84f), new Vector2(1f, 0.93f));
            Text(eyebrow, "YOUR NEXT MOVE STARTS HERE", 15, Lime, FontStyle.Bold, TextAnchor.MiddleLeft, 3f);

            var title = Rect("Title", hero, new Vector2(-0.005f, 0.47f), new Vector2(1f, 0.86f));
            Text(title, "CROSS\nTHE BOARD", 86, Cream, FontStyle.Bold, TextAnchor.MiddleLeft, -2f);

            var description = Rect("Description", hero, new Vector2(0f, 0.36f), new Vector2(0.82f, 0.50f));
            Text(description, "Choose your piece. Read the board.\nMake every move count.", 20, Muted, FontStyle.Normal, TextAnchor.MiddleLeft, 0.25f);

            var start = Rect("Start button", hero, new Vector2(0f, 0.14f), new Vector2(0.52f, 0.31f));
            Button(start, Lime, () => OpenSection(
                "CHOOSE YOUR GAME",
                "GAME MODE",
                "Every board has a different rhythm. Pick the challenge that fits your next move.",
                Lime,
                new[] { "SOLO RUN", "LOCAL VERSUS", "DAILY CHALLENGE" }));
            AddShadow(start.gameObject, new Color(0f, 0f, 0f, 0.35f), new Vector2(0, -7));

            var startLabel = Rect("Label", start, new Vector2(0.08f, 0f), new Vector2(0.78f, 1f));
            Text(startLabel, "START NEW GAME", 22, Ink, FontStyle.Bold, TextAnchor.MiddleLeft, 1f);
            var arrow = Rect("Arrow", start, new Vector2(0.80f, 0f), new Vector2(0.94f, 1f));
            Text(arrow, "→", 35, Ink, FontStyle.Normal, TextAnchor.MiddleRight);
            start.gameObject.AddComponent<ButtonPulse>();

            var continueCard = Rect("Continue", hero, new Vector2(0f, 0f), new Vector2(0.72f, 0.105f));
            var continueButton = Button(continueCard, new Color(1f, 1f, 1f, 0.045f), () => OpenSection(
                "CONTINUE JOURNEY", "LAST SESSION", "Your last run is ready when you are.", Sky,
                new[] { "BOARD 04  ·  TURN 17", "RESUME" }));
            continueButton.colors = ButtonColors(new Color(1f, 1f, 1f, 0.045f), new Color(1f, 1f, 1f, 0.1f));
            var continueText = Rect("Continue text", continueCard, new Vector2(0.045f, 0f), new Vector2(0.96f, 1f));
            Text(continueText, "CONTINUE  ·  BOARD 04     <color=#91A0AF>12m ago</color>", 15, Cream, FontStyle.Bold, TextAnchor.MiddleLeft);
        }

        private void BuildNavigation()
        {
            var nav = Rect("Navigation", _canvas.transform, new Vector2(0.625f, 0.16f), new Vector2(0.945f, 0.84f));
            var heading = Rect("Heading", nav, new Vector2(0f, 0.91f), Vector2.one);
            _sectionLabel = Text(heading, "MAIN MENU", 14, Muted, FontStyle.Bold, TextAnchor.MiddleLeft, 3f);

            AddMenuCard(nav, 0, "01", "GAME MODE", "Solo, versus & daily boards", Lime, () => OpenSection(
                "CHOOSE YOUR GAME", "GAME MODE", "Every board has a different rhythm. Pick the challenge that fits your next move.",
                Lime, new[] { "SOLO RUN", "LOCAL VERSUS", "DAILY CHALLENGE" }));
            AddMenuCard(nav, 1, "02", "CHARACTERS", "Find your perfect play style", Sky, () => OpenSection(
                "CHOOSE YOUR PIECE", "CHARACTERS", "Different pieces bring different routes, perks, and ways to control the board.",
                Sky, new[] { "THE SCOUT", "THE MAKER", "THE ROGUE", "LOCKED PIECE" }));
            AddMenuCard(nav, 2, "03", "ACHIEVEMENTS", "Records, badges & rankings", Coral, () => OpenSection(
                "MAKE YOUR MARK", "ACHIEVEMENTS & RANKING", "Track milestones, compare scores, and see how far you have crossed.",
                Coral, new[] { "ACHIEVEMENTS  18/48", "GLOBAL RANK  #1,284", "FRIENDS LEADERBOARD" }));
            AddMenuCard(nav, 3, "04", "SHOP", "Cosmetics, pieces & offers", Violet, () => OpenSection(
                "THE CORNER SHOP", "SHOP", "Make the board yours with new pieces, trails, themes, and victory poses.",
                Violet, new[] { "FEATURED", "BOARD THEMES", "PIECE STYLES" }));
            AddMenuCard(nav, 4, "05", "SETTINGS", "Audio, controls & display", Muted, () => OpenSection(
                "SET YOUR TABLE", "SETTINGS", "Tune the game to feel right before your next move.",
                Muted, new[] { "AUDIO", "CONTROLS", "DISPLAY" }));
        }

        private void AddMenuCard(RectTransform parent, int index, string number, string title, string subtitle, Color accent, UnityEngine.Events.UnityAction action)
        {
            const float height = 0.155f;
            const float gap = 0.021f;
            float top = 0.87f - index * (height + gap);
            var card = Rect(title, parent, new Vector2(0f, top - height), new Vector2(1f, top));
            var button = Button(card, new Color(1f, 1f, 1f, 0.055f), action);
            button.colors = ButtonColors(new Color(1f, 1f, 1f, 0.055f), new Color(accent.r, accent.g, accent.b, 0.17f));

            var bar = Rect("Accent", card, new Vector2(0f, 0f), new Vector2(0.012f, 1f));
            Image(bar, accent, true);

            var indexRect = Rect("Index", card, new Vector2(0.055f, 0f), new Vector2(0.16f, 1f));
            Text(indexRect, number, 14, accent, FontStyle.Bold, TextAnchor.MiddleLeft, 1.5f);

            var copy = Rect("Copy", card, new Vector2(0.18f, 0.13f), new Vector2(0.84f, 0.87f));
            Text(copy, $"{title}\n<size=13><color=#91A0AF>{subtitle}</color></size>", 19, Cream, FontStyle.Bold, TextAnchor.MiddleLeft, 0.4f);

            var arrow = Rect("Arrow", card, new Vector2(0.87f, 0f), new Vector2(0.95f, 1f));
            Text(arrow, "›", 32, Muted, FontStyle.Normal, TextAnchor.MiddleRight);

            var menuCard = card.gameObject.AddComponent<MenuCard>();
            menuCard.accent = bar.GetComponent<Image>();
            menuCard.arrow = arrow.GetComponent<Text>();
            menuCard.target = card.GetComponent<Image>();
            menuCard.accentColor = accent;
            _menuCards.Add(menuCard);
        }

        private void BuildFooter()
        {
            var footer = Rect("Footer", _canvas.transform, new Vector2(0.055f, 0.045f), new Vector2(0.945f, 0.105f));
            var hint = Rect("Hint", footer, new Vector2(0f, 0f), new Vector2(0.5f, 1f));
            Text(hint, "SELECT  ·  LEFT CLICK        BACK  ·  ESC", 13, new Color(Muted.r, Muted.g, Muted.b, 0.75f), FontStyle.Bold, TextAnchor.MiddleLeft, 1f);
            var online = Rect("Online", footer, new Vector2(0.73f, 0f), Vector2.one);
            Text(online, "●  ONLINE    BUILD 0.1.0", 13, new Color(Lime.r, Lime.g, Lime.b, 0.78f), FontStyle.Bold, TextAnchor.MiddleRight, 1f);
        }

        private void BuildOverlay()
        {
            _overlay = Rect("Section overlay", _canvas.transform, Vector2.zero, Vector2.one);
            Image(_overlay, new Color(0.02f, 0.03f, 0.045f, 0.82f));
            _overlay.gameObject.SetActive(false);
        }

        private void OpenSection(string title, string kicker, string description, Color accent, string[] options)
        {
            if (_dialog != null)
                Destroy(_dialog);

            _overlay.gameObject.SetActive(true);
            _overlay.SetAsLastSibling();

            var closeArea = Button(_overlay, new Color(0f, 0f, 0f, 0f), CloseSection);
            closeArea.transition = Selectable.Transition.None;

            var dialog = Rect("Section dialog", _overlay, new Vector2(0.24f, 0.18f), new Vector2(0.76f, 0.82f));
            _dialog = dialog.gameObject;
            Image(dialog, Panel, true);
            AddShadow(dialog.gameObject, new Color(0f, 0f, 0f, 0.55f), new Vector2(0f, -12f));
            dialog.gameObject.AddComponent<DialogEntrance>();

            var stripe = Rect("Stripe", dialog, new Vector2(0f, 0f), new Vector2(0.015f, 1f));
            Image(stripe, accent, true);

            var kickerRect = Rect("Kicker", dialog, new Vector2(0.09f, 0.80f), new Vector2(0.82f, 0.9f));
            Text(kickerRect, kicker, 14, accent, FontStyle.Bold, TextAnchor.MiddleLeft, 3f);
            var titleRect = Rect("Title", dialog, new Vector2(0.09f, 0.65f), new Vector2(0.85f, 0.82f));
            Text(titleRect, title, 35, Cream, FontStyle.Bold, TextAnchor.MiddleLeft, -0.5f);
            var descriptionRect = Rect("Description", dialog, new Vector2(0.09f, 0.50f), new Vector2(0.87f, 0.67f));
            Text(descriptionRect, description, 17, Muted, FontStyle.Normal, TextAnchor.UpperLeft, 0.2f);

            int count = Mathf.Max(1, options.Length);
            float available = 0.34f;
            float gap = 0.018f;
            float optionHeight = (available - gap * (count - 1)) / count;
            for (int i = 0; i < options.Length; i++)
            {
                float top = 0.43f - i * (optionHeight + gap);
                var option = Rect(options[i], dialog, new Vector2(0.09f, top - optionHeight), new Vector2(0.91f, top));
                var optionButton = Button(option, PanelBright, () => { });
                optionButton.colors = ButtonColors(PanelBright, new Color(accent.r, accent.g, accent.b, 0.25f));
                var optionText = Rect("Label", option, new Vector2(0.05f, 0f), new Vector2(0.82f, 1f));
                Text(optionText, options[i], 16, Cream, FontStyle.Bold, TextAnchor.MiddleLeft, 1f);
                var optionArrow = Rect("Arrow", option, new Vector2(0.84f, 0f), new Vector2(0.95f, 1f));
                Text(optionArrow, "→", 22, accent, FontStyle.Normal, TextAnchor.MiddleRight);
            }

            var close = Rect("Close", dialog, new Vector2(0.87f, 0.86f), new Vector2(0.95f, 0.94f));
            Button(close, new Color(1f, 1f, 1f, 0.065f), CloseSection);
            Text(close, "×", 28, Cream, FontStyle.Normal, TextAnchor.MiddleCenter);
        }

        private void CloseSection()
        {
            if (_dialog != null)
                Destroy(_dialog);
            _dialog = null;
            _overlay.gameObject.SetActive(false);
        }

        private RectTransform Rect(string name, Transform parent, Vector2 min, Vector2 max, Vector2? size = null)
        {
            var go = new GameObject(name, typeof(RectTransform));
            var rect = go.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            if (size.HasValue)
                rect.sizeDelta = size.Value;
            return rect;
        }

        private Image Image(RectTransform target, Color color, bool rounded = false)
        {
            var image = target.gameObject.GetComponent<Image>() ?? target.gameObject.AddComponent<Image>();
            image.color = color;
            if (rounded)
            {
                image.sprite = _roundedSprite;
                image.type = UnityEngine.UI.Image.Type.Sliced;
            }
            return image;
        }

        private Text Text(RectTransform target, string value, int size, Color color, FontStyle style, TextAnchor alignment, float spacing = 0f)
        {
            var text = target.gameObject.GetComponent<Text>() ?? target.gameObject.AddComponent<Text>();
            text.font = _font;
            text.text = value;
            text.fontSize = size;
            text.color = color;
            text.fontStyle = style;
            text.alignment = alignment;
            text.supportRichText = true;
            text.resizeTextForBestFit = false;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;
            if (Mathf.Abs(spacing) > 0.01f)
                target.gameObject.AddComponent<LetterSpacing>().spacing = spacing;
            return text;
        }

        private Button Button(RectTransform target, Color normal, UnityEngine.Events.UnityAction action)
        {
            // Selectable applies its state color through the CanvasRenderer, so the source
            // graphic stays white to avoid multiplying translucent card colors twice.
            var image = Image(target, Color.white, true);
            image.raycastTarget = true;
            var button = target.gameObject.GetComponent<Button>() ?? target.gameObject.AddComponent<Button>();
            button.targetGraphic = image;
            button.colors = ButtonColors(normal, new Color(normal.r * 1.18f, normal.g * 1.18f, normal.b * 1.18f, Mathf.Max(normal.a, 0.14f)));
            button.onClick.AddListener(action);
            return button;
        }

        private static ColorBlock ButtonColors(Color normal, Color highlighted)
        {
            return new ColorBlock
            {
                normalColor = normal,
                highlightedColor = highlighted,
                pressedColor = new Color(highlighted.r * 0.85f, highlighted.g * 0.85f, highlighted.b * 0.85f, highlighted.a),
                selectedColor = highlighted,
                disabledColor = new Color(normal.r, normal.g, normal.b, normal.a * 0.4f),
                colorMultiplier = 1f,
                fadeDuration = 0.11f
            };
        }

        private static void AddShadow(GameObject target, Color color, Vector2 distance)
        {
            var shadow = target.AddComponent<Shadow>();
            shadow.effectColor = color;
            shadow.effectDistance = distance;
            shadow.useGraphicAlpha = true;
        }

        private static Sprite CreateRoundedSprite()
        {
            const int size = 64;
            const float radius = 13f;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = "Runtime Rounded Rectangle",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave
            };

            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = Mathf.Max(radius - x, 0f, x - (size - 1 - radius));
                    float dy = Mathf.Max(radius - y, 0f, y - (size - 1 - radius));
                    float alpha = Mathf.Clamp01(radius + 0.5f - Mathf.Sqrt(dx * dx + dy * dy));
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(16, 16, 16, 16));
            sprite.name = "Runtime Rounded Rectangle";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static Color Hex(string value)
        {
            ColorUtility.TryParseHtmlString("#" + value, out var color);
            return color;
        }
    }

    public sealed class MenuCard : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        public Image target;
        public Image accent;
        public Text arrow;
        public Color accentColor;
        private Vector3 _baseScale;
        private bool _hovered;

        private void Awake() => _baseScale = transform.localScale;
        public void OnPointerEnter(PointerEventData eventData) => _hovered = true;
        public void OnPointerExit(PointerEventData eventData) => _hovered = false;

        private void Update()
        {
            transform.localScale = Vector3.Lerp(transform.localScale, _hovered ? _baseScale * 1.018f : _baseScale, Time.unscaledDeltaTime * 12f);
            if (arrow != null)
                arrow.color = Color.Lerp(arrow.color, _hovered ? accentColor : new Color(0.57f, 0.63f, 0.69f), Time.unscaledDeltaTime * 12f);
        }
    }

    public sealed class AmbientFloat : MonoBehaviour
    {
        public float amplitude = 8f;
        public float speed = 0.4f;
        public float phase;
        private Vector2 _origin;

        private void Start() => _origin = ((RectTransform)transform).anchoredPosition;
        private void Update()
        {
            var rect = (RectTransform)transform;
            rect.anchoredPosition = _origin + Vector2.up * (Mathf.Sin(Time.unscaledTime * speed + phase) * amplitude);
        }
    }

    public sealed class ButtonPulse : MonoBehaviour
    {
        private float _phase;
        private Vector3 _baseScale;

        private void Awake() => _baseScale = transform.localScale;

        private void Update()
        {
            _phase += Time.unscaledDeltaTime;
            float pulse = Mathf.Lerp(1f, 1.006f, (Mathf.Sin(_phase * 2.2f) + 1f) * 0.5f);
            transform.localScale = _baseScale * pulse;
        }
    }

    public sealed class DialogEntrance : MonoBehaviour
    {
        private float _progress;
        private RectTransform _rect;

        private void Awake()
        {
            _rect = (RectTransform)transform;
            _rect.localScale = Vector3.one * 0.94f;
        }

        private void Update()
        {
            _progress = Mathf.Min(1f, _progress + Time.unscaledDeltaTime * 7f);
            float eased = 1f - Mathf.Pow(1f - _progress, 3f);
            _rect.localScale = Vector3.one * Mathf.Lerp(0.94f, 1f, eased);
            if (_progress >= 1f)
                enabled = false;
        }
    }

    /// <summary>Simple decorative route drawn behind the menu using the Canvas mesh.</summary>
    public sealed class BoardRouteGraphic : MaskableGraphic
    {
        private static readonly Vector2[] Route =
        {
            new(0.02f, 0.22f), new(0.14f, 0.32f), new(0.20f, 0.66f), new(0.36f, 0.77f),
            new(0.48f, 0.51f), new(0.62f, 0.40f), new(0.73f, 0.68f), new(0.92f, 0.82f)
        };

        protected override void OnPopulateMesh(VertexHelper vh)
        {
            vh.Clear();
            var bounds = rectTransform.rect;
            const float width = 3f;
            for (int i = 0; i < Route.Length - 1; i++)
            {
                Vector2 a = new(bounds.xMin + Route[i].x * bounds.width, bounds.yMin + Route[i].y * bounds.height);
                Vector2 b = new(bounds.xMin + Route[i + 1].x * bounds.width, bounds.yMin + Route[i + 1].y * bounds.height);
                Vector2 normal = Vector2.Perpendicular((b - a).normalized) * width;
                int index = vh.currentVertCount;
                vh.AddVert(a - normal, color, Vector2.zero);
                vh.AddVert(a + normal, color, Vector2.zero);
                vh.AddVert(b + normal, color, Vector2.zero);
                vh.AddVert(b - normal, color, Vector2.zero);
                vh.AddTriangle(index, index + 1, index + 2);
                vh.AddTriangle(index, index + 2, index + 3);
            }
        }
    }

    /// <summary>Adds subtle tracking to legacy UI Text without requiring imported TMP resources.</summary>
    public sealed class LetterSpacing : BaseMeshEffect
    {
        public float spacing;

        public override void ModifyMesh(VertexHelper vh)
        {
            if (!IsActive() || Mathf.Abs(spacing) < 0.01f)
                return;

            var vertices = new List<UIVertex>();
            vh.GetUIVertexStream(vertices);
            int characterIndex = 0;
            for (int i = 0; i + 5 < vertices.Count; i += 6)
            {
                float offset = characterIndex * spacing;
                for (int j = 0; j < 6; j++)
                {
                    var vertex = vertices[i + j];
                    vertex.position.x += offset;
                    vertices[i + j] = vertex;
                }
                characterIndex++;
            }
            vh.Clear();
            vh.AddUIVertexTriangleStream(vertices);
        }
    }
}
