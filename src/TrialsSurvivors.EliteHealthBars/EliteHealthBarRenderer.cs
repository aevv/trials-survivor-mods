using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using TMPro;
using UnityEngine;

namespace TrialsSurvivors.EliteHealthBars;

public sealed class EliteHealthBarRenderer : MonoBehaviour
{
    private const float ReferenceHeight = 1080f;

    private readonly List<HealthBar> _pool = new();
    private readonly List<OffscreenArrow> _arrows = new();
    private readonly List<BuffIcon> _buffs = new();
    private TMP_FontAsset? _font;
    private bool _fontResolved;
    private Canvas? _canvas;
    private Camera? _camera;
    private string? _loggedCameraName;
    private int _canvasesCreated;
    private float _nextHeartbeat;

    public EliteHealthBarRenderer(IntPtr ptr) : base(ptr)
    {
    }

    private void LateUpdate()
    {
        var used = 0;
        var arrowsUsed = 0;
        var plugin = Plugin.Instance;

        if (plugin.Enabled.Value)
        {
            EnsureCanvas();
            var scale = Screen.height / ReferenceHeight;
            var style = BuildStyle(plugin, scale);
            var arrowColour = Plugin.ParseColour(plugin.ArrowColour.Value, new Color(0.95f, 0.64f, 0.23f, 0.9f));

            foreach (var elite in EliteTracker.All)
            {
                if (!TryGetFraction(elite, out var fraction))
                {
                    EliteTracker.MarkStale(elite);
                    continue;
                }

                elite.DelayedFraction = StepDelayed(elite.DelayedFraction, fraction, plugin.DelayedBarSpeed.Value);

                var entity = elite.Entity!;
                if (ResolveCamera(entity.gameObject.layer) is not { } camera) continue;

                var screen = camera.WorldToScreenPoint(entity.GetTopPosition + Vector3.up * plugin.WorldOffset.Value);
                if (screen.z <= 0f || !OnScreen(screen))
                {
                    if (!plugin.ShowOffscreenArrows.Value) continue;
                    var (edge, angle) = OffscreenArrow.PlaceOnEdge(screen, plugin.ArrowMargin.Value * scale);
                    RentArrow(arrowsUsed++).Show(edge, angle, plugin.ArrowSize.Value * scale, arrowColour);
                    continue;
                }

                if (plugin.HideAtFullHealth.Value && fraction >= 0.999f) continue;

                if (plugin.ShowBuffs.Value) BuffReader.Read(entity, _buffs, plugin.MaxBuffIcons.Value);
                else _buffs.Clear();

                var bar = Rent(used++);
                bar.Apply(style);
                bar.Show(new Vector2(screen.x, screen.y), fraction, elite.DelayedFraction, FormatHp(elite.Health!), _buffs);
            }

            EliteTracker.Sweep();
        }

        for (var i = used; i < _pool.Count; i++) _pool[i].Hide();
        for (var i = arrowsUsed; i < _arrows.Count; i++) _arrows[i].Hide();

        LogHeartbeat(used);
    }

    private void EnsureCanvas()
    {
        if (_canvas != null) return;

        _pool.Clear();
        _arrows.Clear();
        var canvasObject = new GameObject("EliteHealthBarsCanvas");
        DontDestroyOnLoad(canvasObject);
        _canvas = canvasObject.AddComponent<Canvas>();
        _canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        _canvas.sortingOrder = -100;

        _canvasesCreated++;
        if (_canvasesCreated > 1) Plugin.Instance.Log.LogWarning($"overlay canvas was destroyed; recreated (#{_canvasesCreated})");
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
        Plugin.ParseColour(plugin.FillColour.Value, new Color(0.84f, 0.19f, 0.17f)),
        plugin.TextSize.Value * scale,
        plugin.IconSize.Value * scale,
        plugin.ShowHpText.Value);

    private static string FormatHp(ARPGEntity_Module_Health health)
    {
        var vitality = health.Vitality;
        return $"{Abbreviate(vitality.Value)} / {Abbreviate(vitality.Max)}";
    }

    private static string Abbreviate(float value) => value switch
    {
        >= 1e9f => (value / 1e9f).ToString("0.#", CultureInfo.InvariantCulture) + "B",
        >= 1e6f => (value / 1e6f).ToString("0.#", CultureInfo.InvariantCulture) + "M",
        >= 1e4f => (value / 1e3f).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => Mathf.CeilToInt(value).ToString(CultureInfo.InvariantCulture)
    };

    private static float StepDelayed(float delayed, float fraction, float speed)
    {
        if (speed <= 0f || fraction >= delayed) return fraction;
        return Mathf.MoveTowards(delayed, fraction, speed * Time.deltaTime);
    }

    private static bool OnScreen(Vector3 screen) =>
        screen.x >= 0f && screen.x <= Screen.width && screen.y >= 0f && screen.y <= Screen.height;

    private Camera? ResolveCamera(int layer)
    {
        if (!Renders(_camera, layer))
        {
            _camera = null;
            foreach (var camera in Camera.allCameras)
            {
                if (Renders(camera, layer) && (_camera == null || camera.depth > _camera.depth)) _camera = camera;
            }
        }

        if (_camera != null && Plugin.Instance.LogCamera.Value && _camera.name != _loggedCameraName)
        {
            _loggedCameraName = _camera.name;
            Plugin.Instance.Log.LogInfo($"projecting through camera '{_camera.name}' (renders elite layer {LayerMask.LayerToName(layer)})");
        }

        return _camera;
    }

    private static bool Renders(Camera? camera, int layer) =>
        camera != null && camera.isActiveAndEnabled && (camera.cullingMask & (1 << layer)) != 0;

    private void LogHeartbeat(int drawn)
    {
        if (!Plugin.Instance.Verbose.Value || Time.unscaledTime < _nextHeartbeat) return;
        _nextHeartbeat = Time.unscaledTime + 5f;

        var cameras = string.Join(", ", Camera.allCameras.Select(c =>
            $"{c.name}(depth {c.depth}, enabled {c.isActiveAndEnabled}, mask 0x{c.cullingMask:X8})"));

        Plugin.Instance.Log.LogInfo(
            $"heartbeat: tracked={EliteTracker.Count} drawn={drawn} activations={EliteTracker.Activations} " +
            $"canvas={(_canvas != null ? "ok" : "missing")} using='{(_camera != null ? _camera.name : "none")}' " +
            $"all=[{cameras}]");
    }

    private HealthBar Rent(int index)
    {
        if (!_fontResolved)
        {
            _font = Ui.FindGameFont();
            _fontResolved = _font != null;
        }

        while (_pool.Count <= index) _pool.Add(new HealthBar(_canvas!.transform, _font));
        return _pool[index];
    }

    private OffscreenArrow RentArrow(int index)
    {
        while (_arrows.Count <= index) _arrows.Add(new OffscreenArrow(_canvas!.transform));
        return _arrows[index];
    }
}
