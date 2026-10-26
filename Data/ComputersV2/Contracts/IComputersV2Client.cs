namespace _20strike_ui.Data.ComputersV2;

/// <summary>
/// Contract for the v2 inventory backend. Pages and components depend on this interface instead
/// of the concrete HTTP client, so the transport can be swapped (test backend, API version,
/// caching proxy) without touching the UI. All methods are cancellable; failures surface as
/// <see cref="ComputersV2Exception"/> subtypes.
/// </summary>
public interface IComputersV2Client
{
    /// <summary>Fetches the concise computer list (GET /v2/computers).</summary>
    Task<IReadOnlyList<ComputerSummary>> GetComputersAsync(CancellationToken ct = default);

    /// <summary>
    /// Searches computers by exactly one parameter via the v2 search endpoints
    /// (GET /v2/search/{user|hardware|software|mac}/{query}). A blank query short-circuits to an
    /// empty list.
    /// </summary>
    Task<IReadOnlyList<ComputerSummary>> SearchComputersAsync(
        ComputerSearchType type, string query, CancellationToken ct = default);

    /// <summary>Returns <c>null</c> when the computer does not exist (404) or the name is invalid.</summary>
    Task<ComputerDetail?> GetComputerByNameAsync(string computerName, CancellationToken ct = default);

    /// <summary>
    /// Fetches ping statuses (GET /v2/ping). The endpoint is rate-limited by the backend; calls
    /// are serialized process-wide and the last known good payload is served while throttled.
    /// </summary>
    Task<IReadOnlyList<PingInfo>> GetPingAsync(CancellationToken ct = default);

    /// <summary>Returns an empty list when the computer does not exist (404) or the name is invalid.</summary>
    Task<IReadOnlyList<ProblemInfo>> GetProblemsAsync(string computerName, CancellationToken ct = default);

    /// <summary>Fetches the login-to-full-name map (GET /v2/users).</summary>
    Task<IReadOnlyDictionary<string, string>> GetUsersAsync(CancellationToken ct = default);

    /// <summary>True when the /ping endpoint is currently known to be throttled (stale data is being served).</summary>
    bool IsPingThrottled { get; }
}