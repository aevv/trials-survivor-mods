using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;
using UnityEngine.UI;

namespace TrialsSurvivors.EliteHealthBars;

internal sealed class OffscreenArrow
{
    private const int TextureSize = 64;

    private static Sprite? _sprite;

    private readonly GameObject _root;
    private readonly RectTransform _rect;
    private readonly Image _image;

    public OffscreenArrow(Transform parent)
    {
        (_root, _rect) = Ui.Rect("EliteArrow", parent);
        _rect.anchorMin = Vector2.zero;
        _rect.anchorMax = Vector2.zero;
        _rect.pivot = new Vector2(0.5f, 0.5f);
        _image = Ui.Image(_root);
        _image.sprite = Sprite;
    }

    public void Show(Vector2 position, float angleDegrees, float size, Color colour)
    {
        if (!_root.activeSelf) _root.SetActive(true);
        _rect.position = position;
        _rect.localRotation = Quaternion.Euler(0f, 0f, angleDegrees);
        _rect.sizeDelta = new Vector2(size, size);
        if (_image.color != colour) _image.color = colour;
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

    private static Sprite Sprite => _sprite != null ? _sprite : _sprite = CreateArrowSprite();

    private static Sprite CreateArrowSprite()
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
                var u = (x + 0.5f) / TextureSize;
                var v = (y + 0.5f) / TextureSize - 0.5f;
                var halfWidthAtU = 0.42f * (1f - u);
                var edge = halfWidthAtU - Mathf.Abs(v);
                var alpha = Mathf.Clamp01(edge * TextureSize * 0.75f) * Mathf.Clamp01((u - 0.08f) * TextureSize);
                pixels[y * TextureSize + x] = new Color32(255, 255, 255, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, TextureSize, TextureSize), new Vector2(0.5f, 0.5f));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
