namespace _20strike_ui.Data.ComputersV2;

// Internal camelCase UI models. Raw v2 API payloads are normalized into these
// by ComputersV2Mapper, so the UI never deals with backend-specific key casing.

/// <summary>Concise entry from GET /v2/computers. The Location object is intentionally ignored for now.</summary>
public sealed record ComputerSummary(string Name);

/// <summary>Entry from GET /v2/ping mapped by computer name.</summary>
public sealed record PingInfo(string Name, string Ip, bool Status);

/// <summary>Entry from GET /v2/problems/{computerName}.</summary>
public sealed record ProblemInfo(string Name, string Description);

public sealed record SoftwareItem(string Name, string Version, string InstallDate, string InstallLocation, string EstimatedSize);

public sealed record ProcessItem(string Name, string ProcessId, string WorkingSetSize, string CreationDate);

/// <summary>Normalized full computer detail record (GET /v2/computers/{name}), split into sections.</summary>
public sealed class ComputerDetail
{
    public IReadOnlyDictionary<string, string> Bios { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> ComputerSystem { get; init; } = new Dictionary<string, string>();
    public IReadOnlyDictionary<string, string> OperatingSystem { get; init; } = new Dictionary<string, string>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Processor { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> PhysicalMemory { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> PhysicalDisk { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> LogicalDisk { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> VideoController { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Monitor { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> NetworkAdapter { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<IReadOnlyDictionary<string, string>> Printer { get; init; } = Array.Empty<IReadOnlyDictionary<string, string>>();
    public IReadOnlyList<SoftwareItem> Software { get; init; } = Array.Empty<SoftwareItem>();
    public IReadOnlyList<ProcessItem> Process { get; init; } = Array.Empty<ProcessItem>();
}

/// <summary>Concise summary rendered from the most important fields of a computer detail record.</summary>
public sealed record DetailSummary(
    string Name,
    string Domain,
    string UserName,
    string Manufacturer,
    string OsName,
    string OsVersion,
    string OsBuild,
    string OsArchitecture,
    string Cpu,
    string RamTotal,
    string DiskCFree,
    string LastBootUpTime,
    string LocalTime);

/// <summary>A single normalized row for virtualized tabular display. <see cref="SearchBlob"/> is precomputed for fast filtering.</summary>
public sealed record LargeRow(string[] Cells, string SearchBlob);

/// <summary>A large (potentially huge) tabular section rendered with virtualization and a search filter.</summary>
public sealed record LargeSection(string Id, string Title, IReadOnlyList<LargeRow> Rows, string[] Columns);

/// <summary>Result of a cached detail load; <see cref="ErrorMessage"/> is set when loading failed (retryable).</summary>
public sealed record DetailCacheResult(ComputerDetail? Detail, string? ErrorMessage);

/// <summary>
/// Result of the once-per-circuit users load (GET /v2/users); <see cref="ErrorMessage"/> is set when
/// loading failed so the UI can fall back to showing plain usernames.
/// </summary>
public sealed record UsersResult(IReadOnlyDictionary<string, string> UsersByLogin, string? ErrorMessage);

/// <summary>
/// The parameter type used by the v2 search endpoints (GET /v2/search/{type}/{query}).
/// Each value maps to a dedicated URL segment (user, hardware, software, mac).
/// </summary>
public enum ComputerSearchType
{
    Username,
    Hardware,
    Software,
    Mac,
}