using System;
using System.Linq;
using System.Reflection;

namespace TrialsSurvivors.RunHistory;

internal static class ModDifficulty
{
    private const string ImpossibleAssembly = "TrialsSurvivors.Impossible";
    private const string ImpossibleApiType = "TrialsSurvivors.Impossible.ImpossibleApi";

    private static bool _resolved;
    private static PropertyInfo? _isActive;
    private static PropertyInfo? _displayName;

    public static string? ActiveOverride()
    {
        Resolve();
        if (_isActive == null || _displayName == null) return null;
        return _isActive.GetValue(null) is true ? _displayName.GetValue(null) as string : null;
    }

    private static void Resolve()
    {
        if (_resolved) return;
        _resolved = true;

        var type = AppDomain.CurrentDomain.GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == ImpossibleAssembly)
            ?.GetType(ImpossibleApiType);
        if (type == null) return;

        _isActive = type.GetProperty("IsActive", BindingFlags.Public | BindingFlags.Static);
        _displayName = type.GetProperty("DisplayName", BindingFlags.Public | BindingFlags.Static);
        Plugin.Instance.Log.LogInfo($"found {ImpossibleAssembly}; impossible runs will be recorded as such");
    }
}
