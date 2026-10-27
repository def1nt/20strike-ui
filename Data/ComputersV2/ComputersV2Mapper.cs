using System.Globalization;
using System.Text.Json;
using _20strike_ui.Formatting;

namespace _20strike_ui.Data.ComputersV2;

/// <summary>
/// Adapter layer that normalizes raw v2 API payloads into the internal camelCase models used by
/// the UI. Display formatting shared with the UI lives in <see cref="ValueFormatters"/>; view-model
/// building (summary, tabs, large sections) lives in the presentation layer.
/// </summary>
public static class ComputersV2Mapper
{
    internal static IReadOnlyList<ComputerSummary> MapComputers(IEnumerable<ComputerDto>? dtos) =>
        (dtos ?? Enumerable.Empty<ComputerDto>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new ComputerSummary(d.Name!.Trim(), MapLocation(d.Location)))
            .ToList();

    /// <summary>
    /// Normalizes GET /v2/computers into the map's working set: every known computer with its
    /// optional <see cref="MapPoint"/>. Computers without a location keep a <c>null</c> Location so
    /// the map can offer them for marker placement.
    /// </summary>
    internal static IReadOnlyList<ComputerLocationSummary> MapComputersWithLocations(IEnumerable<ComputerDto>? dtos) =>
        (dtos ?? Enumerable.Empty<ComputerDto>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new ComputerLocationSummary(d.Name!.Trim(), MapLocation(d.Location)))
            .ToList();

    /// <summary>
    /// Normalizes a backend MapData object into the internal <see cref="MapPoint"/>. A null payload
    /// (computer without a location, or the ambiguous 404 on GET /v2/location/{name}) maps to null.
    /// Description and Phone are trimmed but kept empty rather than replaced with the "—"
    /// placeholder, because the UI always sends the complete payload back on save.
    /// </summary>
    internal static MapPoint? MapLocation(LocationDto? dto) =>
        dto is null
            ? null
            : new MapPoint(dto.X, dto.Y, (dto.Description ?? "").Trim(), (dto.Phone ?? "").Trim());

    /// <summary>
    /// Normalizes the GET /v2/search responses: a flat JSON array of computer names. Blank entries
    /// are dropped, values trimmed, and duplicates removed case-insensitively (a computer may match
    /// the query through several fields and appear more than once).
    /// </summary>
    internal static IReadOnlyList<ComputerSummary> MapSearchResults(IEnumerable<string>? raw) =>
        (raw ?? Enumerable.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => new ComputerSummary(s.Trim()))
            .DistinctBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

    internal static IReadOnlyList<PingInfo> MapPing(IEnumerable<PingDto>? dtos) =>
        (dtos ?? Enumerable.Empty<PingDto>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new PingInfo(d.Name!.Trim(), ValueFormatters.Clean(d.Ip), d.Status))
            .ToList();

    internal static IReadOnlyList<ProblemInfo> MapProblems(IEnumerable<ProblemDto>? dtos) =>
        (dtos ?? Enumerable.Empty<ProblemDto>())
            .Select(d => new ProblemInfo(ValueFormatters.Clean(d.Name), ValueFormatters.Clean(d.Description)))
            .ToList();

    /// <summary>
    /// Normalizes the GET /v2/users payload: keys become trimmed, lowercased logins (mirroring how
    /// WMI UserName values are keyed on the legacy screens), values are trimmed, blank entries are
    /// dropped, and lookups are case-insensitive.
    /// </summary>
    internal static IReadOnlyDictionary<string, string> MapUsers(Dictionary<string, string>? raw)
    {
        if (raw is null || raw.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(raw.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in raw)
        {
            var login = key.Trim().ToLowerInvariant();
            var fullName = value.Trim();
            if (login.Length > 0 && fullName.Length > 0)
            {
                result[login] = fullName;
            }
        }

        return result;
    }

    internal static ComputerDetail MapDetail(DetailDto dto) => new()
    {
        Bios = NormalizeObject(dto.Bios),
        ComputerSystem = NormalizeObject(dto.ComputerSystem),
        OperatingSystem = NormalizeObject(dto.OperatingSystem),
        Processor = NormalizeRows(dto.Processor),
        PhysicalMemory = NormalizeRows(dto.PhysicalMemory),
        PhysicalDisk = NormalizeRows(dto.PhysicalDisk),
        LogicalDisk = NormalizeRows(dto.LogicalDisk),
        VideoController = NormalizeRows(dto.VideoController),
        Monitor = NormalizeRows(dto.Monitor),
        NetworkAdapter = NormalizeRows(dto.NetworkAdapter),
        Printer = NormalizeRows(dto.Printer),
        Software = (dto.Software ?? new List<Dictionary<string, JsonElement>>()).Select(s => new SoftwareItem(
            Value(s, "Name"),
            Value(s, "Version"),
            ValueFormatters.FormatWmiDate8(Value(s, "InstallDate")),
            Value(s, "InstallLocation"),
            FormatKb(Value(s, "EstimatedSize")))).ToList(),
        Process = (dto.Process ?? new List<Dictionary<string, JsonElement>>()).Select(p => new ProcessItem(
            Value(p, "Name"),
            Value(p, "ProcessId"),
            ValueFormatters.FormatBytes(Value(p, "WorkingSetSize")),
            ValueFormatters.FormatWmiDate(Value(p, "CreationDate")))).ToList(),
    };

    // -------------------------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------------------------

    private static IReadOnlyDictionary<string, string> NormalizeObject(Dictionary<string, JsonElement>? src)
    {
        if (src is null || src.Count == 0)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }

        var result = new Dictionary<string, string>(src.Count, StringComparer.OrdinalIgnoreCase);
        foreach (var (key, element) in src)
        {
            result[ToCamel(key)] = ElementToString(element);
        }

        return result;
    }

    private static IReadOnlyList<IReadOnlyDictionary<string, string>> NormalizeRows(List<Dictionary<string, JsonElement>>? src)
    {
        if (src is null || src.Count == 0)
        {
            return Array.Empty<IReadOnlyDictionary<string, string>>();
        }

        return src.Select(row =>
        {
            var normalized = new Dictionary<string, string>(row.Count, StringComparer.OrdinalIgnoreCase);
            foreach (var (key, element) in row)
            {
                normalized[ToCamel(key)] = ElementToString(element);
            }

            return (IReadOnlyDictionary<string, string>)normalized;
        }).ToList();
    }

    private static string ElementToString(JsonElement element) => element.ValueKind switch
    {
        JsonValueKind.String => element.GetString() ?? "",
        JsonValueKind.Null or JsonValueKind.Undefined => "",
        _ => element.GetRawText(),
    };

    private static string ToCamel(string key) => key.Length switch
    {
        0 => key,
        1 => key.ToLowerInvariant(),
        _ => char.ToLowerInvariant(key[0]) + key[1..],
    };

    private static string Value(Dictionary<string, JsonElement> row, string key) =>
        row.TryGetValue(key, out var element) ? ValueFormatters.Clean(ElementToString(element)) : "—";

    private static string FormatKb(string? value)
    {
        // WMI EstimatedSize is expressed in kilobytes.
        return string.IsNullOrWhiteSpace(value)
               || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb)
            ? "—"
            : ValueFormatters.FormatBytes((kb * 1024).ToString(CultureInfo.InvariantCulture));
    }
}