using System;
using UnityEngine;

namespace TrialsSurvivors.RoomSkip;

public sealed class SkipButtonDriver : MonoBehaviour
{
    public SkipButtonDriver(IntPtr ptr) : base(ptr)
    {
    }

    private void Update()
    {
        try
        {
            SkipButton.Tick();
        }
        catch (Exception e)
        {
            Plugin.Instance.Log.LogWarning($"skip button update failed: {e.Message}");
            SkipButton.Reset();
        }
    }
}
