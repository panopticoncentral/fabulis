using System.Globalization;

namespace Fabulis.Server.Data;

public static class NarrationValidation
{
    // 100,000 chars is ~20 pages of single-spaced text; anything more is
    // almost certainly an accident. Kokoro itself handles long input by
    // chunking, so there's no upstream-imposed limit to mirror — this cap
    // exists only to reject absurd input quickly.
    public const int MaxTextLength = 100_000;
    public const double MinSpeed = 0.5;
    public const double MaxSpeed = 2.0;

    public static double NormalizeSpeed(double? requested, string? settingValue)
    {
        if (requested is { } r) return r;
        if (settingValue is not null &&
            double.TryParse(settingValue, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
            return parsed;
        return 1.0;
    }

    public static bool IsSpeedValid(double speed) =>
        !double.IsNaN(speed) && speed >= MinSpeed && speed <= MaxSpeed;

    public static string? NormalizeVoice(string? requested, string? settingValue)
    {
        if (!string.IsNullOrWhiteSpace(requested)) return requested.Trim();
        if (!string.IsNullOrWhiteSpace(settingValue)) return settingValue.Trim();
        return null;
    }

    public static string NormalizeBaseUrl(string url) => url.TrimEnd('/');

    public static bool IsBaseUrlValid(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out var parsed)) return false;
        return parsed.Scheme is "http" or "https";
    }

    public static bool IsTextLengthOk(string text) => text.Length <= MaxTextLength;
}
