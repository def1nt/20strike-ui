using System.Globalization;
using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Formatting;

namespace _20strike_ui.Pages.ComputersV2;

/// <summary>A single tab of the detail card; <see cref="Large"/> is set for virtualized tabular sections.</summary>
public sealed record DetailTab(string Id, string Title, LargeSection? Large = null);

/// <summary>A rendered section inside a tab: either key/value fields or a table with headers and rows.</summary>
public sealed record SectionView(
    string Title,
    IReadOnlyList<KeyValuePair<string, string>> Fields,
    IReadOnlyList<IReadOnlyList<string>> Rows,
    IReadOnlyList<string> Headers);

/// <summary>The tab list and per-tab sections built from a <see cref="ComputerDetail"/>.</summary>
public sealed record DetailTabs(
    IReadOnlyList<DetailTab> Tabs,
    IReadOnlyDictionary<string, IReadOnlyList<SectionView>> Sections);

/// <summary>
/// Builds the view models rendered by the detail card: the concise summary, the virtualized
/// software/process sections and the tab/section structure. Pure logic with no component state,
/// so it is unit-testable and independent of the rendering layer.
/// </summary>
public static class ComputerDetailViewFactory
{
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
        RamTotal: ValueFormatters.FormatBytes(SumBytes(d.PhysicalMemory, "capacity").ToString(CultureInfo.InvariantCulture)),
        DiskCFree: FormatDiskC(d.LogicalDisk),
        LastBootUpTime: ValueFormatters.FormatWmiDate(GetRaw(d.OperatingSystem, "lastBootUpTime")),
        LocalTime: ValueFormatters.FormatWmiDate(GetRaw(d.OperatingSystem, "localTime")));

    public static LargeSection BuildSoftwareSection(ComputerDetail d) => BuildLarge(
        "software", "Программы",
        new[] { "Имя", "Версия", "Дата установки", "Путь установки", "Размер" },
        d.Software.Select(s => new[] { s.Name, s.Version, s.InstallDate, s.InstallLocation, s.EstimatedSize }));

    public static LargeSection BuildProcessSection(ComputerDetail d) => BuildLarge(
        "processes", "Процессы",
        new[] { "Имя", "PID", "Память", "Время запуска" },
        d.Process.Select(p => new[] { p.Name, p.ProcessId, p.WorkingSetSize, p.CreationDate }));

    /// <summary>Builds the tab list and per-tab sections for the detail card.</summary>
    public static DetailTabs BuildTabs(ComputerDetail detail)
    {
        var tabs = new List<DetailTab>
        {
            new("system", "Система"),
            new("storage", "Диски"),
            new("cpu", "Процессор и память"),
            new("video", "Видео и мониторы"),
            new("network", "Сеть"),
            new("printers", "Принтеры"),
            new("software", "Программы", Large: BuildSoftwareSection(detail)),
            new("processes", "Процессы", Large: BuildProcessSection(detail)),
        };

        var sections = new Dictionary<string, IReadOnlyList<SectionView>>
        {
            ["system"] = new[]
            {
                Section("BIOS", detail.Bios),
                Section("Компьютер", detail.ComputerSystem),
                Section("Операционная система", detail.OperatingSystem),
            },
            ["storage"] = new[]
            {
                RowsSection("Физические диски", detail.PhysicalDisk),
                RowsSection("Логические диски", detail.LogicalDisk),
            },
            ["cpu"] = new[]
            {
                RowsSection("Процессор", detail.Processor),
                RowsSection("Физическая память", detail.PhysicalMemory),
            },
            ["video"] = new[]
            {
                RowsSection("Видеокарты", detail.VideoController),
                RowsSection("Мониторы", detail.Monitor),
            },
            ["network"] = new[] { RowsSection("Сетевые адаптеры", detail.NetworkAdapter) },
            ["printers"] = new[] { RowsSection("Принтеры", detail.Printer) },
        };

        return new DetailTabs(tabs, sections);
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

    private static SectionView Section(string title, IReadOnlyDictionary<string, string> fields)
    {
        var list = fields
            .Select(f => new KeyValuePair<string, string>(ValueFormatters.TitleCase(f.Key), f.Value))
            .ToList();
        return new SectionView(title, list, Array.Empty<IReadOnlyList<string>>(), Array.Empty<string>());
    }

    private static SectionView RowsSection(string title, IReadOnlyList<IReadOnlyDictionary<string, string>> rows)
    {
        if (rows.Count == 0)
        {
            return new SectionView(title, Array.Empty<KeyValuePair<string, string>>(), Array.Empty<IReadOnlyList<string>>(), Array.Empty<string>());
        }

        var keys = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var row in rows)
        {
            foreach (var key in row.Keys)
            {
                if (seen.Add(key))
                {
                    keys.Add(key);
                }
            }
        }

        var headers = keys.Select(ValueFormatters.TitleCase).ToList();
        var cells = rows
            .Select(row => (IReadOnlyList<string>)keys.Select(k => row.TryGetValue(k, out var v) ? FormatField(k, v) : "—").ToList())
            .ToList();
        return new SectionView(title, Array.Empty<KeyValuePair<string, string>>(), cells, headers);
    }

    // Raw WMI values are decoded for display: dates -> "dd.MM.yyyy HH:mm:ss", byte sizes -> readable units.
    private static string FormatField(string camelKey, string value) => camelKey switch
    {
        "releaseDate" or "lastBootUpTime" or "localTime" or "installDate" => ValueFormatters.FormatWmiDate(value),
        "size" or "freeSpace" or "capacity" or "memory" => ValueFormatters.FormatBytes(value),
        "paperSize" => ValueFormatters.Truncate(value, 300),
        _ => value,
    };

    private static string Value(IReadOnlyDictionary<string, string> row, string key) =>
        row.TryGetValue(key, out var value) ? ValueFormatters.Clean(value) : "—";

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
            : $"{ValueFormatters.FormatBytes(GetRaw(c, "freeSpace"))} / {ValueFormatters.FormatBytes(GetRaw(c, "size"))}";
    }
}