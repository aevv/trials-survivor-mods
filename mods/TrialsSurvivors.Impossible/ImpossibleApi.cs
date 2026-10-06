namespace TrialsSurvivors.Impossible;

public static class ImpossibleApi
{
    public static bool IsActive => ImpossibleState.IsActive;

    public static string DisplayName => Plugin.Instance.DisplayName.Value;
}
