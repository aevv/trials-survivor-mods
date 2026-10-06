using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace TrialsSurvivors.EliteHealthBars;

internal sealed class OffscreenArrow
{
    private const int TextureSize = 128;
    private const float OutlineWidth = 0.075f;
    private const float ShadowSoftness = 0.09f;
    private const float ShadowOpacity = 0.6f;
    private static readonly Vector2 ShadowOffset = new(0.04f, -0.06f);

    private static readonly Vector2[] Shape =
    {
        new(0.88f, 0.5f),
        new(0.14f, 0.85f),
        new(0.3f, 0.5f),
        new(0.14f, 0.15f)
    };

    private static Sprite? _bodySprite;
    private static Sprite? _shadowSprite;

    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly RectTransform _shadowRect;
    private readonly RectTransform _bodyRect;
    private readonly Image _shadow;
    private readonly Image _body;
    private float _size = -1f;

    public OffscreenArrow(Transform parent)
    {
        (_root, _rect) = Ui.Rect("EliteArrow", parent);
        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.zero;
        _rect.pivot = new Vector2(0.5f, 0.5f);

        GameObject shadow, body;
        (shadow, _shadowRect) = Ui.Rect("Shadow", _rect);
        _shadow = Ui.Image(shadow);
        _shadow.sprite = ShadowSprite;

        (body, _bodyRect) = Ui.Rect("Body", _rect);
        _body = Ui.Image(body);
        _body.sprite = BodySprite;
    }

    public void Show(Vector2 position, float angleDegrees, float size, Color colour)
    {
        if (!_root.activeSelf) _root.SetActive(true);
        _rect.position = position;

        var rotation = Quaternion.Euler(0f, 0f, angleDegrees);
        _bodyRect.localRotation = rotation;
        _shadowRect.localRotation = rotation;

        if (!Mathf.Approximately(_size, size))
        {
            _size = size;
            _rect.sizeDelta = new Vector2(size, size);
            _shadowRect.anchoredPosition = ShadowOffset * size;
        }

        if (_body.color != colour)
        {
            _body.color = colour;
            _shadow.color = new Color(1f, 1f, 1f, colour.a);
        }
    }

    public void Hide()
    {
        if (_root.activeSelf) _root.SetActive(false);
    }

    public static (Vector2 Position, float Angle) PlaceOnEdge(Vector3 screen, float margin)
    {
        var centre = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);
        var direction = new Vector2(screen.x, screen.y) - centre;
        if (screen.z < 0f) direction = -direction;
        if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;

        var halfWidth = centre.x - margin;
        var halfHeight = centre.y - margin;
        var scale = Mathf.Min(halfWidth / Mathf.Max(Mathf.Abs(direction.x), 1e-5f), halfHeight / Mathf.Max(Mathf.Abs(direction.y), 1e-5f));

        return (centre + direction * scale, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
    }

    public static Vector2 LiftAbove(Vector2 position, float size, Rect obstacle, float gap)
    {
        var half = size * 0.5f;
        var overlaps = position.x + half > obstacle.xMin && position.x - half < obstacle.xMax &&
                       position.y + half > obstacle.yMin && position.y - half < obstacle.yMax + gap;
        return overlaps ? new Vector2(position.x, obstacle.yMax + gap + half) : position;
    }

    private static Sprite BodySprite => _bodySprite != null ? _bodySprite : _bodySprite = CreateSprite(BodyPixel);

    private static Sprite ShadowSprite => _shadowSprite != null ? _shadowSprite : _shadowSprite = CreateSprite(ShadowPixel);

    private static Color32 BodyPixel(Vector2 uv, float distance)
    {
        var pixel = 1f / TextureSize;
        var outer = Mathf.Clamp01(0.5f - distance / pixel);
        var insideFill = -(distance + OutlineWidth);
        var fill = Mathf.Clamp01(0.5f + insideFill / pixel);

        var alongArrow = Mathf.InverseLerp(Shape[1].x, Shape[0].x, uv.x);
        var bevel = Mathf.Lerp(0.7f, 1f, Mathf.Clamp01(insideFill / 0.06f));
        var brightness = Mathf.Lerp(0.55f, 1f, alongArrow) * bevel * fill;

        var shade = (byte)(brightness * 255f);
        return new Color32(shade, shade, shade, (byte)(outer * 255f));
    }

    private static Color32 ShadowPixel(Vector2 uv, float distance)
    {
        var alpha = ShadowOpacity * (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.02f, ShadowSoftness, distance)));
        return new Color32(0, 0, 0, (byte)(alpha * 255f));
    }

    private static Sprite CreateSprite(System.Func<Vector2, float, Color32> shade)
    {
        var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var pixels = new Il2CppStructArray<Color32>(TextureSize * TextureSize);
        for (var y = 0; y < TextureSize; y++)
        {
            for (var x = 0; x < TextureSize; x++)
            {
                var uv = new Vector2((x + 0.5f) / TextureSize, (y + 0.5f) / TextureSize);
                pixels[y * TextureSize + x] = shade(uv, SignedDistance(uv));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }

    private static float SignedDistance(Vector2 p)
    {
        var nearest = float.MaxValue;
        var inside = false;
        for (int i = 0, j = Shape.Length - 1; i < Shape.Length; j = i++)
        {
            var a = Shape[j];
            var b = Shape[i];
            var edge = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, edge) / edge.sqrMagnitude);
            nearest = Mathf.Min(nearest, (p - (a + edge * t)).sqrMagnitude);

            if ((a.y > p.y) != (b.y > p.y) && p.x < a.x + (p.y - a.y) / (b.y - a.y) * edge.x) inside = !inside;
        }

        var distance = Mathf.Sqrt(nearest);
        return inside ? -distance : distance;
    }
}
