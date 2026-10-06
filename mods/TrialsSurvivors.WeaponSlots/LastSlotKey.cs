using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace TrialsSurvivors.WeaponSlots;

public sealed class LastSlotKey : MonoBehaviour
{
    public LastSlotKey(IntPtr ptr) : base(ptr)
    {
    }

    private void Update()
    {
        var key = Plugin.Instance.LastSlotKeyBinding.Value;
        var keyboard = Keyboard.current;
        if (key == Key.None || keyboard == null || !keyboard[key].wasPressedThisFrame) return;
        if (Time.timeScale <= 0f) return;

        var bar = SpellBarSlots.Current;
        if (bar == null || bar.WasCollected || !bar.isActiveAndEnabled) return;

        var skills = bar._skillsModule;
        if (skills == null) return;

        var slot = Plugin.Instance.Slots - 1;
        if (!skills.TryGetSkillAtSlot(slot, out var skill) || skill == null)
        {
            if (Plugin.Instance.Verbose.Value) Plugin.Instance.Log.LogInfo($"{key}: slot {slot + 1} is empty");
            return;
        }

        skills.SelectSkill(slot, true);
        if (Plugin.Instance.Verbose.Value) Plugin.Instance.Log.LogInfo($"{key}: selected '{skill.name}' in slot {slot + 1}");
    }
}
