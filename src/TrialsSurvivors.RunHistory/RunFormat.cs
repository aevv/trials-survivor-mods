using System;
using System.Globalization;

namespace TrialsSurvivors.RunHistory;

internal static class RunFormat
{
    public static string Duration(float seconds)
    {
        var time = TimeSpan.FromSeconds(seconds);
        return time.TotalHours >= 1 ? time.ToString(@"h\:mm\:ss") : time.ToString(@"mm\:ss");
    }

    public static string Number(double value) => Math.Abs(value) switch
    {
        >= 1e9 => (value / 1e9).ToString("0.##", CultureInfo.InvariantCulture) + "B",
        >= 1e6 => (value / 1e6).ToString("0.##", CultureInfo.InvariantCulture) + "M",
        >= 1e4 => (value / 1e3).ToString("0.#", CultureInfo.InvariantCulture) + "k",
        _ => value.ToString("0", CultureInfo.InvariantCulture)
    };

    public static string ResultColour(string result) => result switch
    {
        nameof(RunResultType.Victory) => "#7BD88F",
        nameof(RunResultType.EndlessOver) => "#7FB8F0",
        _ => "#E06C6C"
    };

    public static string ResultLabel(string result) => result switch
    {
        nameof(RunResultType.EndlessOver) => "Endless",
        _ => result
    };

    public static string When(DateTime utc) => utc.ToLocalTime().ToString("d MMM yyyy HH:mm", CultureInfo.InvariantCulture);
}
