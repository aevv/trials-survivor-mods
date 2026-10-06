using System.Collections.Generic;
using UnityEngine.InputSystem;

namespace TrialsSurvivors.RunHistory;

internal static class InputBlocker
{
    private const string UiActionMap = "UI";

    private static readonly List<InputAction> Disabled = new();

    public static void Block()
    {
        if (Disabled.Count > 0) return;

        var enabled = InputSystem.ListEnabledActions();
        for (var i = 0; i < enabled.Count; i++)
        {
            var action = enabled[i];
            if (action == null || action.actionMap?.name == UiActionMap) continue;

            action.Disable();
            Disabled.Add(action);
        }
    }

    public static void Release()
    {
        foreach (var action in Disabled)
        {
            if (action != null && !action.WasCollected) action.Enable();
        }

        Disabled.Clear();
    }
}
