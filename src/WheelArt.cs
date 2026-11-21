using DiceKingdoms.Game;
using UnityEngine;
using UnityEngine.UI;
using Outcome = DiceModders.Casino.Wheel.Outcome;

namespace DiceModders.Casino;

/// <summary>
/// The wheel, drawn once per match into a texture: faces with a soft light-to-dark shading, thin gold lines between them, a wooden rim with
/// gold studs, a gold hub. On top, each face carries the game's own icon (the disaster icons, the gold coin), turning with the wheel.
/// </summary>
internal static class WheelArt
{
    internal static readonly Color Jackpot = new(0.93f, 0.74f, 0.28f);
    internal static readonly Color Win = new(0.47f, 0.70f, 0.38f);
    internal static readonly Color Nothing = new(0.93f, 0.88f, 0.77f);
    private static readonly Color Nothing2 = new(0.86f, 0.80f, 0.67f);
    internal static readonly Color Bad = new(0.89f, 0.49f, 0.31f);
    internal static readonly Color Catastrophe = new(0.62f, 0.21f, 0.22f);
    private static readonly Color Wood = new(0.36f, 0.24f, 0.15f);
    private static readonly Color WoodDark = new(0.25f, 0.16f, 0.10f);
    private static readonly Color Gold = new(0.96f, 0.80f, 0.38f);
    private static readonly Color GoldDark = new(0.72f, 0.53f, 0.20f);
    private static readonly Color Clear = new(0f, 0f, 0f, 0f);

    internal static Color Of(Outcome o, int face) => o switch
    {
        Outcome.Jackpot => Jackpot,
        Outcome.Gold or Outcome.Each => Win,
        Outcome.Robbers or Outcome.Fire => Bad,
        Outcome.Catastrophe => Catastrophe,
        _ => face % 2 == 0 ? Nothing : Nothing2,
    };

    /// <summary>Face i covers i*step .. (i+1)*step degrees clockwise from the top.</summary>
    internal static Texture2D Disc(int size, Outcome[] faces)
    {
        var tex = Keep(new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
        var px = new Color[size * size];
        float c = (size - 1) / 2f, r = size / 2f - 1.5f;
        float rimIn = r * 0.86f, hub = r * 0.16f;
        int n = faces.Length;
        float step = 360f / n;
        int studs = n;   // one gold stud on every line between two faces
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float dx = x - c, dy = y - c;
            float d = Mathf.Sqrt(dx * dx + dy * dy);
            float angle = Mathf.Atan2(dx, dy) * Mathf.Rad2Deg;   // clockwise from the top
            if (angle < 0f) angle += 360f;
            Color col;
            if (d > r) col = Clear;
            else if (d > rimIn)
            {
                // wooden rim, lighter in the middle of its width, with gold studs
                float t = (d - rimIn) / (r - rimIn);
                col = Color.Lerp(WoodDark, Wood, 1f - Mathf.Abs(t - 0.5f) * 2f);
                float studAngle = (angle / (360f / studs) + 0.5f) % 1f - 0.5f;   // -0.5 .. 0.5, 0 = on a line between faces
                float sx = studAngle * (360f / studs) * Mathf.Deg2Rad * ((rimIn + r) / 2f), sy = d - (rimIn + r) / 2f;
                float sd = Mathf.Sqrt(sx * sx + sy * sy), sr = (r - rimIn) * 0.28f;
                if (sd < sr) col = Color.Lerp(Gold, GoldDark, sd / sr * 0.8f);
                if (d > r - 1.2f) col.a = Mathf.Clamp01(r - d + 0.2f);
            }
            else if (d > rimIn - 2.2f) col = GoldDark;   // gold line inside the rim
            else if (d < hub)
            {
                float t = d / hub;
                col = d < hub * 0.45f ? Color.Lerp(Gold, GoldDark, t) : Color.Lerp(GoldDark, WoodDark, (t - 0.45f) * 1.4f);
            }
            else
            {
                int face = Mathf.Min(n - 1, (int)(angle / step));
                float edge = Mathf.Min(angle - face * step, (face + 1) * step - angle) * Mathf.Deg2Rad * d;   // distance to a face line in pixels
                var baseCol = Of(faces[face], face);
                float shade = Mathf.Lerp(0.82f, 1.06f, (d - hub) / (rimIn - hub));   // darker near the hub, lighter at the rim
                col = new Color(Mathf.Clamp01(baseCol.r * shade), Mathf.Clamp01(baseCol.g * shade), Mathf.Clamp01(baseCol.b * shade), 1f);
                if (edge < 1.3f) col = Color.Lerp(GoldDark, Gold, edge / 1.3f);
            }
            px[y * size + x] = col;
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>
    /// The game unloads "unused" pictures when a match ends (Resources.UnloadUnusedAssets): a texture made by a mod is not used by anything the
    /// game knows, so it went, and the pointer showed as a white square in the next match (2026-11-21). Marked to stay.
    /// </summary>
    private static Texture2D Keep(Texture2D tex)
    {
        tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
        return tex;
    }

    /// <summary>A gold pointer with a dark outline, pointing down at the face at the top.</summary>
    internal static Texture2D Pointer(int width, int height)
    {
        var tex = Keep(new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Bilinear, wrapMode = TextureWrapMode.Clamp });
        var px = new Color[width * height];
        float mid = (width - 1) / 2f;
        for (int y = 0; y < height; y++)
        for (int x = 0; x < width; x++)
        {
            float half = (y / (float)(height - 1)) * (width * 0.24f);   // narrow at the bottom (y = 0), wide at the top
            float inside = half - Mathf.Abs(x - mid);
            px[y * width + x] = inside < 0f ? Clear : inside < 2f || y > height - 3 ? WoodDark : Color.Lerp(GoldDark, Gold, y / (float)height);
        }
        tex.SetPixels(px);
        tex.Apply();
        return tex;
    }

    /// <summary>The game's own icon for a face (the disaster's icon, the gold coin), or null for "nothing".</summary>
    internal static Sprite IconOf(Outcome o, System.Func<string, Sprite> theme)
    {
        var d = Disasters();
        try
        {
            return o switch
            {
                Outcome.Jackpot => d?.tsunami?.icon,
                Outcome.Catastrophe => d?.meteorite?.icon,
                Outcome.Robbers => d?.robbers?.icon,
                Outcome.Fire => d?.fire?.icon,
                Outcome.Gold => theme("gold"),
                Outcome.Each => theme("food"),
                _ => null,
            };
        }
        catch { return null; }
    }

    private static Disasters _disasters;
    private static Disasters Disasters()
    {
        if (_disasters != null) return _disasters;
        foreach (var d in Resources.FindObjectsOfTypeAll<Disasters>()) return _disasters = d;
        return null;
    }

    /// <summary>Puts the faces' icons on the wheel picture (children, so they turn with it), each one upright towards the rim.</summary>
    internal static void AddIcons(GameObject picture, Outcome[] faces, System.Func<string, Sprite> theme, float iconSize)
    {
        if (picture == null) return;
        for (int i = picture.transform.childCount - 1; i >= 0; i--) Object.Destroy(picture.transform.GetChild(i).gameObject);
        int n = faces.Length;
        for (int i = 0; i < n; i++)
        {
            var sprite = IconOf(faces[i], theme);
            if (sprite == null) continue;
            float a = (i + 0.5f) * 360f / n;   // clockwise from the top
            float rad = a * Mathf.Deg2Rad;
            var go = new GameObject("Casino face " + i);
            go.transform.SetParent(picture.transform, false);
            var rect = go.AddComponent<RectTransform>();
            var at = new Vector2(0.5f + 0.31f * Mathf.Sin(rad), 0.5f + 0.31f * Mathf.Cos(rad));
            rect.anchorMin = at; rect.anchorMax = at;
            rect.sizeDelta = new Vector2(iconSize, iconSize);
            rect.anchoredPosition = Vector2.zero;
            rect.localRotation = Quaternion.Euler(0f, 0f, -a);   // the icon's top faces the rim
            var image = go.AddComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
        }
    }
}
