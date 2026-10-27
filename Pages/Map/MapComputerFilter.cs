using _20strike_ui.Data.ComputersV2;

namespace _20strike_ui.Pages.Map;

/// <summary>
/// Case-insensitive substring filter over computer names for the map's search field. Kept as pure
/// logic so the match behavior is unit-testable independently of the UI.
/// </summary>
public static class MapComputerFilter
{
    /// <summary>
    /// Returns the computers whose name contains <paramref name="query"/> (trimmed), compared
    /// case-insensitively. A blank query returns the whole list unchanged. The location is carried
    /// through untouched so the results can distinguish "has a marker" (pan) from "no marker yet"
    /// (offer placement).
    /// </summary>
    public static IReadOnlyList<ComputerLocationSummary> Apply(
        IEnumerable<ComputerLocationSummary> computers, string? query)
    {
        var trimmed = query?.Trim() ?? "";
        if (trimmed.Length == 0)
        {
            return computers.ToList();
        }

        return computers
            .Where(c => c.Name.Contains(trimmed, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }
}