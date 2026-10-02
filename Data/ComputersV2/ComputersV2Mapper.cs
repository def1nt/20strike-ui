using System.Globalization;
using System.Text.Json;

namespace _20strike_ui.Data.ComputersV2;

/// <summary>
/// Adapter layer that normalizes raw v2 API payloads into the internal camelCase models used by
/// the UI, formats WMI dates and byte sizes, splits the single detail response into sections and
/// builds the concise summary from the most important fields.
/// </summary>
public static class ComputersV2Mapper
{
    internal static IReadOnlyList<ComputerSummary> MapComputers(IEnumerable<ComputerDto>? dtos) =>
        (dtos ?? Enumerable.Empty<ComputerDto>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new ComputerSummary(d.Name!.Trim()))
            .ToList();

    internal static IReadOnlyList<PingInfo> MapPing(IEnumerable<PingDto>? dtos) =>
        (dtos ?? Enumerable.Empty<PingDto>())
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .Select(d => new PingInfo(d.Name!.Trim(), Clean(d.Ip), d.Status))
            .ToList();

    internal static IReadOnlyList<ProblemInfo> MapProblems(IEnumerable<ProblemDto>? dtos) =>
        (dtos ?? Enumerable.Empty<ProblemDto>())
            .Select(d => new ProblemInfo(Clean(d.Name), Clean(d.Description)))
            .ToList();

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
            FormatWmiDate8(Value(s, "InstallDate")),
            Value(s, "InstallLocation"),
            FormatKb(Value(s, "EstimatedSize")))).ToList(),
        Process = (dto.Process ?? new List<Dictionary<string, JsonElement>>()).Select(p => new ProcessItem(
            Value(p, "Name"),
            Value(p, "ProcessId"),
            FormatBytes(Value(p, "WorkingSetSize")),
            FormatWmiDate(Value(p, "CreationDate")))).ToList(),
    };

    /// <summary>Builds the concise summary rendered at the top of the expanded card.</summary>
    public static DetailSummary BuildSummary(ComputerDetail d) => new(
        Name: Value(d.ComputerSystem, "name"),
        Domain: Value(d.ComputerSystem, "domain"),
        UserName: Value(d.ComputerSystem, "userName"),
        Manufacturer: Value(d.ComputerSystem, "manufacturer"),
        OsName: Value(d.OperatingSystem, "name"),
        OsVersion: Value(d.OperatingSystem, "version"),
        OsBuild: Value(d.OperatingSystem, "buildNumber"),
        OsArchitecture: Value(d.OperatingSystem, "architecture"),
        Cpu: d.Processor.FirstOrDefault() is { } cpu ? Value(cpu, "name") : "—",
        RamTotal: FormatBytes(SumBytes(d.PhysicalMemory, "capacity").ToString(CultureInfo.InvariantCulture)),
        DiskCFree: FormatDiskC(d.LogicalDisk),
        LastBootUpTime: FormatWmiDate(GetRaw(d.OperatingSystem, "lastBootUpTime")),
        LocalTime: FormatWmiDate(GetRaw(d.OperatingSystem, "localTime")));

    public static LargeSection BuildSoftwareSection(ComputerDetail d) => BuildLarge(
        "software", "Программы",
        new[] { "Имя", "Версия", "Дата установки", "Путь установки", "Размер" },
        d.Software.Select(s => new[] { s.Name, s.Version, s.InstallDate, s.InstallLocation, s.EstimatedSize }));

    public static LargeSection BuildProcessSection(ComputerDetail d) => BuildLarge(
        "processes", "Процессы",
        new[] { "Имя", "PID", "Память", "Время запуска" },
        d.Process.Select(p => new[] { p.Name, p.ProcessId, p.WorkingSetSize, p.CreationDate }));

    /// <summary>Turns the first character into upper case for display (e.g. "userName" -> "UserName").</summary>
    public static string TitleCase(string value) => value.Length switch
    {
        0 => value,
        1 => value.ToUpperInvariant(),
        _ => char.ToUpperInvariant(value[0]) + value[1..],
    };

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

    // -------------------------------------------------------------------------------------------
    // Internals
    // -------------------------------------------------------------------------------------------

    private static LargeSection BuildLarge(string id, string title, string[] columns, IEnumerable<string[]> cellSets)
    {
        var rows = cellSets
            .Select(cells => new LargeRow(cells, string.Join(' ', cells).ToLowerInvariant()))
            .ToList();
        return new LargeSection(id, title, rows, columns);
    }

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
        row.TryGetValue(key, out var element) ? Clean(ElementToString(element)) : "—";

    private static string Value(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? Clean(value) : "—";

    private static string GetRaw(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? value : "";

    private static long SumBytes(IReadOnlyList<IReadOnlyDictionary<string, string>> rows, string key)
    {
        long sum = 0;
        foreach (var row in rows)
        {
            if (row.TryGetValue(key, out var value)
                && long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var n)
                && n > 0)
            {
                sum += n;
            }
        }

        return sum;
    }

    private static string FormatDiskC(IReadOnlyList<IReadOnlyDictionary<string, string>> logicalDisks)
    {
        var c = logicalDisks.FirstOrDefault(l =>
            l.TryGetValue("name", out var name) && name.Equals("C:", StringComparison.OrdinalIgnoreCase));
        return c is null
            ? "—"
            : $"{FormatBytes(GetRaw(c, "freeSpace"))} / {FormatBytes(GetRaw(c, "size"))}";
    }

    private static string FormatKb(string? value)
    {
        // WMI EstimatedSize is expressed in kilobytes.
        return string.IsNullOrWhiteSpace(value)
               || !long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var kb)
            ? "—"
            : FormatBytes((kb * 1024).ToString(CultureInfo.InvariantCulture));
    }

    private static string FormatWmiDate8(string? value)
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

    private static string Clean(string? value)
    {
        var trimmed = value?.Trim() ?? "";
        return trimmed.Length == 0 ? "—" : trimmed;
    }
}