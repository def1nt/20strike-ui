using System.Globalization;

namespace _20strike_ui.Formatting;

/// <summary>
/// Pure display-formatting helpers shared by the data normalization layer and the UI. No API or
/// WMI knowledge lives here; values arrive already normalized. All methods are culture-safe where
/// it matters and return the "—" placeholder for blank/invalid input.
/// </summary>
public static class ValueFormatters
{
    /// <summary>Turns the first character into upper case for display (e.g. "userName" -> "UserName").</summary>
    public static string TitleCase(string value) => value.Length switch
    {
        0 => value,
        1 => value.ToUpperInvariant(),
        _ => char.ToUpperInvariant(value[0]) + value[1..],
    };

    /// <summary>
    /// Extracts the users-map lookup key from a WMI UserName value ("DOMAIN\login" or a bare
    /// "login"): everything up to and including the last backslash is dropped, then the rest is
    /// trimmed and lowercased. Blank values (including the "—" placeholder) yield an empty key
    /// that never matches.
    /// </summary>
    public static string NormalizeLogin(string userName)
    {
        var trimmed = userName?.Trim() ?? "";
        if (trimmed.Length == 0 || trimmed == "—")
        {
            return "";
        }

        var slash = trimmed.LastIndexOf('\\');
        return (slash >= 0 ? trimmed[(slash + 1)..] : trimmed).Trim().ToLowerInvariant();
    }

    /// <summary>
    /// Renders a computer's user for the summary row: when the normalized login is present in the
    /// users map the full name is shown first with the original UserName value in parentheses
    /// (e.g. "Иванов Иван (VMZ\ivanov-av)"). Unknown logins and failed loads fall back to the raw
    /// UserName unchanged.
    /// </summary>
    public static string FormatUserName(string userName, IReadOnlyDictionary<string, string> usersByLogin)
    {
        if (string.IsNullOrWhiteSpace(userName) || userName == "—")
        {
            return userName;
        }

        var login = NormalizeLogin(userName);
        if (login.Length > 0
            && usersByLogin.TryGetValue(login, out var fullName)
            && !string.IsNullOrWhiteSpace(fullName))
        {
            return $"{fullName} ({userName})";
        }

        return userName;
    }

    /// <summary>Formats a byte count (as a string) into readable units: "1 КБ", "1.5 ГБ", "—".</summary>
    public static string FormatBytes(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) || bytes < 0)
        {
            return "—";
        }

        string[] units = { "Б", "КБ", "МБ", "ГБ", "ТБ" };
        double b = bytes;
        int u = 0;
        while (b >= 1024 && u < units.Length - 1)
        {
            b /= 1024;
            u++;
        }

        return $"{b:0.#} {units[u]}";
    }

    /// <summary>Formats a WMI date ("yyyyMMddHHmmss.ffffff+ZZZ") as "dd.MM.yyyy HH:mm:ss".</summary>
    public static string FormatWmiDate(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 14)
        {
            return "—";
        }

        if (DateTime.TryParseExact(value[..14], "yyyyMMddHHmmss", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt.ToString("dd.MM.yyyy HH:mm:ss", CultureInfo.InvariantCulture);
        }

        return Clean(value);
    }

    /// <summary>Formats a WMI date ("yyyyMMdd...") as "dd.MM.yyyy".</summary>
    public static string FormatWmiDate8(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length < 8)
        {
            return "—";
        }

        if (DateTime.TryParseExact(value[..8], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            return dt.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }

        return Clean(value);
    }

    /// <summary>Limits a long value to a fixed length on display, marking truncation with "…".</summary>
    public static string Truncate(string value, int maxLength) =>
        value.Length <= maxLength ? value : value[..(maxLength - 1)] + "…";

    /// <summary>Trims a value and replaces empty results with the "—" placeholder.</summary>
    public static string Clean(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length == 0 ? "—" : trimmed;
    }
}