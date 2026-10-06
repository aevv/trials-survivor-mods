using System;
using System.Collections.Generic;
using BlackRoseProjects.InstancedAnimationSystem;
using HarmonyLib;
using UnityEngine;
using Random = UnityEngine.Random;

namespace TrialsSurvivors.Impossible;

internal static class RedElites
{
    private const string ColourPrefix = "_Color_";
    private const string EmissionPrefix = "_Emission_";
    private static readonly Color DefaultGlow = new(1f, 0.08f, 0.03f);
    private static readonly Color DefaultBody = new(0.7f, 0.05f, 0.05f);

    private static readonly Dictionary<IntPtr, RedElite> Red = new();
    private static readonly Dictionary<IntPtr, ColorPaletteSO> Palettes = new();
    private static readonly List<IntPtr> Dead = new();
    internal static readonly Dictionary<IntPtr, float> HealthDeltas = new();

    public static bool IsRed(ARPGEntity entity) => Red.ContainsKey(entity.Pointer);

    public static bool Roll(bool hasHealthBonus)
    {
        var plugin = Plugin.Instance;
        if (!hasHealthBonus) return false;
        if (plugin.ForceRedElites.Value) return true;
        return Random.value < plugin.RedEliteChance.Value;
    }

    public static void MarkRed(ARPGEntity_Module_Elite elite, ARPGEntity entity)
    {
        var plugin = Plugin.Instance;
        var red = new RedElite(elite, entity, entity.GetComponentInChildren<MonsterRendererManager>(true));
        Red[entity.Pointer] = red;

        if (plugin.RedEliteHidePinkAura.Value)
        {
            try
            {
                var shader = elite._shaderModifier;
                var aura = elite._settings != null ? elite._settings.shaderProperty : null;
                if (shader != null && aura != null) shader.SetPropertyValue(ref aura, 0f);
            }
            catch (Exception e)
            {
                plugin.Log.LogWarning($"couldn't hide the pink aura on '{entity.name}': {e.Message}");
            }
        }

        Paint(red, "activation");
    }

    public static void Unmark(ARPGEntity entity)
    {
        if (!Red.Remove(entity.Pointer, out var red)) return;

        try
        {
            if (!entity.gameObject.activeInHierarchy) return;
            if (red.Manager != null && !red.Manager.WasCollected) red.Manager.RandomizeColors();
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't restore '{entity.name}' colours: {e.Message}");
        }
    }

    public static void OnPaletteSet(MonsterRendererManager manager, ColorPaletteSO palette)
    {
        var key = manager.Pointer;
        if (palette != null) Palettes[key] = palette;
        if (Red.Count == 0) return;

        foreach (var red in Red.Values)
        {
            if (red.ManagerKey != key) continue;
            Paint(red, "palette re-roll");
            return;
        }
    }

    public static void RepaintAll()
    {
        if (Red.Count == 0) return;
        Dead.Clear();
        foreach (var (key, red) in Red)
        {
            if (red.Elite.WasCollected || red.Entity.WasCollected || red.Elite == null || !red.Elite.IsElite) Dead.Add(key);
            else Paint(red, null);
        }
        foreach (var key in Dead) Red.Remove(key);
    }

    private static void Paint(RedElite red, string? reason)
    {
        var plugin = Plugin.Instance;
        var entity = red.Entity;

        try
        {
            var renderer = FindInstancedRenderer(red, out var source);
            if (renderer == null)
            {
                if (reason != null) plugin.Log.LogWarning($"no instanced renderer on '{entity.name}'; red elite has no glow");
                return;
            }

            ColorPaletteSO? palette = null;
            if (red.ManagerKey != IntPtr.Zero) Palettes.TryGetValue(red.ManagerKey, out palette);
            var colours = palette != null ? palette._colorsLinear : null;
            var emissions = palette != null ? palette._emissionsLinear : null;

            var glow = Plugin.ParseColour(plugin.RedEliteGlowColour, DefaultGlow) * plugin.RedEliteGlowIntensity.Value;
            var body = Plugin.ParseColour(plugin.RedEliteBodyColour, DefaultBody);
            var tint = plugin.RedEliteBodyTint.Value;
            var painted = 0;

            var holders = renderer.GetCustomShaderVectorValues();
            if (holders != null)
            {
                foreach (var holder in holders)
                {
                    var property = holder.ShaderProperty ?? "";
                    if (TrySlot(property, ColourPrefix, out var slot))
                    {
                        var original = colours != null && slot < colours.Length ? colours[slot] : holder.DefaultValue;
                        var target = new Vector4(body.r, body.g, body.b, original.w);
                        renderer.SetCustomShaderVectorValue(Vector4.Lerp(original, target, tint), holder.IdentifierIndex);
                        painted++;
                    }
                    else if (TrySlot(property, EmissionPrefix, out slot))
                    {
                        var original = emissions != null && slot < emissions.Length ? emissions[slot] : holder.DefaultValue;
                        renderer.SetCustomShaderVectorValue(new Vector4(glow.r, glow.g, glow.b, original.w), holder.IdentifierIndex);
                        painted++;
                    }
                }
            }

            if (reason == null) return;
            if (painted == 0)
                plugin.Log.LogWarning($"'{entity.name}' has no palette instanced values (renderer from {source}); red elite has no glow");
            else if (plugin.Verbose.Value || RunTally.First(ref RunTally.LoggedPaint))
                plugin.Log.LogInfo($"painted '{entity.name}' red on {reason} via {source}: {painted} values, " +
                                   $"palette {(palette != null ? palette.name : "unknown, used defaults")}, body tint {tint:0.##}, glow {glow}");
        }
        catch (Exception e)
        {
            if (reason != null) plugin.Log.LogWarning($"couldn't paint '{entity.name}' red: {e}");
        }
    }

    private static bool TrySlot(string property, string prefix, out int slot)
    {
        slot = -1;
        return property.StartsWith(prefix, StringComparison.Ordinal) &&
               int.TryParse(property.AsSpan(prefix.Length), out slot) && slot >= 0;
    }

    private static InstancedRenderer? FindInstancedRenderer(RedElite red, out string source)
    {
        var shader = red.Elite._shaderModifier?.TryCast<ARPGEntity_Module_Temp_ShaderModifier_IGPU>();
        var renderer = shader?.InstancedRenderer;
        if (renderer != null)
        {
            source = "shader modifier";
            return renderer;
        }

        renderer = red.Manager != null ? red.Manager._instancedRenderer : null;
        source = "MonsterRendererManager";
        return renderer;
    }

    private sealed class RedElite
    {
        public RedElite(ARPGEntity_Module_Elite elite, ARPGEntity entity, MonsterRendererManager? manager)
        {
            Elite = elite;
            Entity = entity;
            Manager = manager;
            ManagerKey = manager != null ? manager.Pointer : IntPtr.Zero;
        }

        public ARPGEntity_Module_Elite Elite { get; }
        public ARPGEntity Entity { get; }
        public MonsterRendererManager? Manager { get; }
        public IntPtr ManagerKey { get; }
    }
}

public sealed class RedEliteRefresher : MonoBehaviour
{
    private const float Interval = 0.25f;
    private float _next;

    public RedEliteRefresher(IntPtr ptr) : base(ptr)
    {
    }

    private void Update()
    {
        if (Time.unscaledTime < _next) return;
        _next = Time.unscaledTime + Interval;
        try
        {
            RedElites.RepaintAll();
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't repaint red elites: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(MonsterRendererManager), nameof(MonsterRendererManager.SetPalette))]
internal static class SetPalettePatch
{
    [HarmonyPostfix]
    private static void Postfix(MonsterRendererManager __instance, ColorPaletteSO palette)
    {
        try
        {
            RedElites.OnPaletteSet(__instance, palette);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't track a palette: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(ARPGEntity_Module_Elite), nameof(ARPGEntity_Module_Elite.ActivateElite))]
internal static class ActivateElitePatch
{
    [HarmonyPrefix]
    private static void Prefix(ARPGEntity_Module_Elite __instance, out float __state)
    {
        __state = float.NaN;
        try
        {
            if (__instance.IsElite || !ImpossibleState.IsActive) return;
            var stat = ImpossibleState.MaxHealthStat(__instance.ARPGEntity);
            if (stat != null) __state = stat.Value;
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't read elite health before activation: {e.Message}");
        }
    }

    [HarmonyPostfix]
    private static void Postfix(ARPGEntity_Module_Elite __instance, float __state)
    {
        if (float.IsNaN(__state)) return;

        try
        {
            if (!__instance.IsElite) return;
            var entity = __instance.ARPGEntity;
            var stat = ImpossibleState.MaxHealthStat(entity);
            if (entity == null || stat == null) return;

            var plugin = Plugin.Instance;
            var eliteBonus = stat.Value - __state;
            var extra = eliteBonus * (plugin.MonsterHealthMultiplier.Value - 1f);
            var regularElite = stat.Value + extra;

            var red = RedElites.Roll(eliteBonus > 0f);
            if (red) extra += regularElite * (plugin.RedEliteHealthMultiplier.Value - 1f);

            if (extra > 0f)
            {
                stat.AddValue(extra);
                RedElites.HealthDeltas[__instance.Pointer] = extra;
            }

            RunTally.Elites++;
            if (red)
            {
                RunTally.RedElites++;
                RedElites.MarkRed(__instance, entity);
            }

            var first = red ? RunTally.First(ref RunTally.LoggedRedElite) : RunTally.First(ref RunTally.LoggedElite);
            if (plugin.Verbose.Value || first)
            {
                var vitality = entity.HealthModule != null ? entity.HealthModule.Vitality : null;
                plugin.Log.LogInfo(
                    $"{(red ? "red elite" : "elite")} '{entity.name}': max health {__state:0} +{eliteBonus:0} elite +{extra:0} impossible = {stat.Value:0}" +
                    (vitality != null ? $", vitality {vitality.Value:0}/{vitality.Max:0}" : ""));
            }
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't scale an elite: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(ARPGEntity_Module_Elite), nameof(ARPGEntity_Module_Elite.ResetElite))]
internal static class ResetElitePatch
{
    [HarmonyPrefix]
    private static void Prefix(ARPGEntity_Module_Elite __instance)
    {
        try
        {
            var entity = __instance.ARPGEntity;
            if (RedElites.HealthDeltas.Remove(__instance.Pointer, out var extra))
                ImpossibleState.MaxHealthStat(entity)?.SubstractValue(extra);
            if (entity != null) RedElites.Unmark(entity);
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"couldn't reset an elite: {e.Message}");
        }
    }
}
