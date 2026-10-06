using Il2CppInterop.Runtime.InteropTypes.Arrays;
using UnityEngine;

namespace TrialsSurvivors.RoomSkip;

internal static class ButtonSprite
{
    private const int Size = 64;
    private const float Radius = 18f;
    private const float Rim = 2.5f;
    private const float Border = 24f;

    private static Sprite? _sprite;

    public static Sprite Get() => _sprite != null ? _sprite : _sprite = Create();

    private static Sprite Create()
    {
        var texture = new Texture2D(Size, Size, TextureFormat.RGBA32, false)
        {
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };

        var half = Size * 0.5f;
        var pixels = new Il2CppStructArray<Color32>(Size * Size);
        for (var y = 0; y < Size; y++)
        {
            for (var x = 0; x < Size; x++)
            {
                var p = new Vector2(x + 0.5f - half, y + 0.5f - half);
                var q = new Vector2(Mathf.Abs(p.x), Mathf.Abs(p.y)) - new Vector2(half - Radius, half - Radius);
                var distance = Vector2.Max(q, Vector2.zero).magnitude + Mathf.Min(Mathf.Max(q.x, q.y), 0f) - Radius;

                var alpha = Mathf.Clamp01(0.5f - distance);
                var body = Mathf.Lerp(0.62f, 1f, (y + 0.5f) / Size);
                var rim = Mathf.Clamp01(distance + Rim + 0.5f);
                var shade = Mathf.Lerp(body, 1f, rim * 0.85f);

                var value = (byte)(shade * 255f);
                pixels[y * Size + x] = new Color32(value, value, value, (byte)(alpha * 255f));
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        texture.hideFlags = HideFlags.HideAndDontSave;

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, Size, Size), new Vector2(0.5f, 0.5f), 100f, 0u,
            SpriteMeshType.FullRect, new Vector4(Border, Border, Border, Border));
        sprite.hideFlags = HideFlags.HideAndDontSave;
        return sprite;
    }
}
