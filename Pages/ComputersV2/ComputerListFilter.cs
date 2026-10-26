using _20strike_ui.Data.ComputersV2;

namespace _20strike_ui.Pages.ComputersV2;

/// <summary>
/// Pure filtering for the computer list: quick name filter and the "hide offline" toggle.
/// Computers without ping data are never hidden by the offline toggle (their status is unknown).
/// </summary>
public static class ComputerListFilter
{
    public static IReadOnlyList<ComputerSummary> Apply(
        IReadOnlyList<ComputerSummary> computers,
        Func<string, PingInfo?> getPing,
        string nameFilter,
        bool hideOffline)
    {
        var query = nameFilter.Trim();

        if (query.Length == 0 && !hideOffline)
        {
            return computers;
        }

        IEnumerable<ComputerSummary> filtered = computers;

        // Hide computers whose last successful probe reported them offline (status: false).
        if (hideOffline)
        {
            filtered = filtered.Where(c => getPing(c.Name)?.Status != false);
        }

        if (query.Length > 0)
        {
            filtered = filtered.Where(c => c.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
        }

        return filtered.ToList();
    }
}