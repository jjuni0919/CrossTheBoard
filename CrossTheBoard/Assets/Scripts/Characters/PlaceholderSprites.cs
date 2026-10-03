using System.Collections.Generic;
using UnityEngine;

namespace CrossTheBoard
{
    /// <summary>Small code-generated placeholders; replace the catalog art when final assets arrive.</summary>
    public static class PlaceholderSprites
    {
        private static readonly Dictionary<string, Sprite> Cache = new();

        public static Sprite Character(string key, int style, Color bodyColor)
        {
            if (Cache.TryGetValue(key, out var existing) && existing != null)
                return existing;
            const int size = 24;
            var colors = new Color[size * size];
            bool Body(int x, int y)
            {
                if (x < 2 || x > 21 || y < 2 || y > 21) return false;
                float dx = x - 11.5f;
                switch (style)
                {
                    case 0: return y <= 14 && dx * dx + (y - 8f) * (y - 8f) < 98;
                    case 1: return x >= 4 && x <= 19 && y >= 4 && y <= 17 || (x >= 10 && x <= 13 && y >= 18);
                    case 2: return x >= 4 && x <= 19 && y >= 4 && y <= 16 || (y >= 17 && y <= 21 && (x >= 4 && x <= 8 || x >= 15 && x <= 19));
                    case 3: return x >= 4 && x <= 19 && y >= 3 && y <= 18 || x >= 10 && x <= 13 && y >= 19;
                    case 4: return y >= 3 && y <= 16 && Mathf.Abs(dx) < 10f - (16 - y) * 0.28f || y >= 17 && (x >= 3 && x <= 7 || x >= 16 && x <= 20);
                    case 5: return dx * dx + (y - 10) * (y - 10) < 80 || y >= 16 && y <= 20 && (x >= 4 && x <= 8 || x >= 15 && x <= 19);
                    case 6: return dx * dx + (y - 13) * (y - 13) < 78 && y >= 7 || y >= 3 && y <= 9 && x >= 3 && x <= 20 && (x % 5 < 3 || y >= 6);
                    case 7: return y >= 11 && y <= 19 && dx * dx + (y - 12) * (y - 12) < 95 || x >= 7 && x <= 16 && y >= 3 && y <= 10;
                    case 8: return dx * dx / 100f + (y - 11) * (y - 11) / 72f <= 1 || x >= 10 && x <= 13 && y >= 19;
                    default: return x >= 3 && x <= 20 && y >= 3 && y <= 19 && !(y >= 18 && (x < 5 || x > 18));
                }
            }
            var outline = new Color(0.08f, 0.12f, 0.17f);
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    if (!Body(x, y)) continue;
                    bool edge = !Body(x - 1, y) || !Body(x + 1, y) || !Body(x, y - 1) || !Body(x, y + 1);
                    colors[y * size + x] = edge ? outline : Color.Lerp(bodyColor, Color.white, y >= 14 ? 0.13f : 0f);
                }
            for (int y = 9; y <= 11; y++)
                foreach (int x in new[] { 7, 8, 15, 16 })
                    if (Body(x, y)) colors[y * size + x] = outline;
            for (int x = 10; x <= 13; x++)
                if (Body(x, 6)) colors[6 * size + x] = outline;
            if (style == 7)
                foreach (int x in new[] { 6, 11, 17 }) colors[15 * size + x] = Color.white;
            var sprite = MakeSprite(key, size, colors, size);
            Cache[key] = sprite;
            return sprite;
        }

        public static Sprite Coin()
        {
            if (Cache.TryGetValue("coin", out var existing) && existing != null) return existing;
            const int size = 16;
            var colors = new Color[size * size];
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float distance = Vector2.Distance(new Vector2(x, y), new Vector2(7.5f, 7.5f));
                    if (distance > 6.5f) continue;
                    colors[y * size + x] = distance > 5.2f ? new Color(0.65f, 0.37f, 0.08f) : new Color(1f, 0.79f, 0.22f);
                    if (x >= 7 && x <= 8 && y >= 4 && y <= 11) colors[y * size + x] = new Color(1f, 0.95f, 0.65f);
                }
            Cache["coin"] = MakeSprite("Coin", size, colors, 24f);
            return Cache["coin"];
        }

        private static Sprite MakeSprite(string name, int size, Color[] colors, float pixelsPerUnit)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            { name = name, filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp, hideFlags = HideFlags.DontSave };
            texture.SetPixels(colors);
            texture.Apply();
            var sprite = Sprite.Create(texture, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), pixelsPerUnit);
            sprite.name = name;
            sprite.hideFlags = HideFlags.DontSave;
            return sprite;
        }
    }
}
