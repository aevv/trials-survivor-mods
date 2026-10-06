using System;
using System.Collections.Generic;
using UnityEngine;

namespace TrialsSurvivors.EliteHealthBars;

public sealed class EliteHealthBarRenderer : MonoBehaviour
{
    private const float ReferenceHeight = 1080f;

    private readonly List<HealthBar> _pool = new();
    private Canvas? _canvas;
    private Camera? _camera;
    private string? _loggedCameraName;

    public EliteHealthBarRenderer(IntPtr ptr) : base(ptr)
    {
    }

    private void Awake()
    {
        var canvasObject = new GameObject("EliteHealthBarsCanvas");
        DontDestroyOnLoad(canvasObject);
        _canvas = canvasObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = -100;
    }

    private void LateUpdate()
    {
        var used = 0;
        var plugin = Plugin.Instance;

        if (plugin.Enabled.Value && _canvas != null && ResolveCamera() is { } camera)
        {
            var style = BuildStyle(plugin, Screen.height / ReferenceHeight);

            foreach (var elite in EliteTracker.All)
            {
                if (!TryGetFraction(elite, out var fraction))
                {
                    EliteTracker.MarkStale(elite);
                    continue;
                }

                elite.DelayedFraction = StepDelayed(elite.DelayedFraction, fraction, plugin.DelayedBarSpeed.Value);

                if (plugin.HideAtFullHealth.Value && fraction >= 0.999f) continue;

                var anchor = elite.Entity!.GetTopPosition + Vector3.up * plugin.WorldOffset.Value;
                var screen = camera.WorldToScreenPoint(anchor);
                if (screen.z <= 0f || !OnScreen(screen)) continue;

                var bar = Rent(used++);
                bar.Apply(style);
                bar.Show(new Vector2(screen.x, screen.y), fraction, elite.DelayedFraction);
            }

            EliteTracker.Sweep();
        }

        for (var i = used; i < _pool.Count; i++) _pool[i].Hide();
    }

    private static bool TryGetFraction(TrackedElite elite, out float fraction)
    {
        fraction = 0f;

        var module = elite.Module;
        if (module.WasCollected || module == null || !module.IsElite || !module.gameObject.activeInHierarchy) return false;

        elite.Entity ??= module.ARPGEntity;
        if (elite.Entity == null) return false;

        elite.Health ??= elite.Entity.HealthModule;
        if (elite.Health == null || elite.Health.IsDead) return false;

        var vitality = elite.Health.Vitality;
        if (vitality == null || vitality.Max <= 0f) return false;

        fraction = Mathf.Clamp01(vitality.Value / vitality.Max);
        return true;
    }

    private static BarStyle BuildStyle(Plugin plugin, float scale) => new(
        new Vector2(plugin.Width.Value, plugin.Height.Value) * scale,
        plugin.Border.Value * scale,
        Plugin.ParseColour(plugin.BackgroundColour.Value, new Color(0f, 0f, 0f, 0.7f)),
        Plugin.ParseColour(plugin.DelayedColour.Value, new Color(0.95f, 0.82f, 0.42f)),
        Plugin.ParseColour(plugin.FillColour.Value, new Color(0.84f, 0.19f, 0.17f)));

    private static float StepDelayed(float delayed, float fraction, float speed)
    {
        if (speed <= 0f || fraction >= delayed) return fraction;
        return Mathf.MoveTowards(delayed, fraction, speed * Time.deltaTime);
    }

    private static bool OnScreen(Vector3 screen) =>
        screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;

    private Camera? ResolveCamera()
    {
        if (_camera == null || !_camera.isActiveAndEnabled)
        {
            var manager = WorldSpaceCanvasManager.ExistingInstance;
            _camera = manager != null && manager._worldCamera != null ? manager._worldCamera : Camera.main;
        }

        if (_camera != null && Plugin.Instance.LogCamera.Value && _camera.name != _loggedCameraName)
        {
            _loggedCameraName = _camera.name;
            Plugin.Instance.Log.LogInfo($"projecting through camera '{_camera.name}'");
        }

        return _camera;
    }

    private HealthBar Rent(int index)
    {
        while (_pool.Count <= index) _pool.Add(new HealthBar(_canvas!.transform));
        return _pool[index];
    }
}
