namespace _20strike_ui.Data.ComputersV2;

/// <summary>
/// Configuration for the v2 inventory backend connection. Bound from the "ComputersV2" section
/// of appsettings (see <see cref="SectionName"/>).
/// </summary>
public sealed class ComputersV2Options
{
    public const string SectionName = "ComputersV2";

    /// <summary>Base URL of the v2 backend, e.g. "http://srv-backup:5310/v2/".</summary>
    public string BaseUrl { get; set; } = "http://srv-backup:5310/v2/";
}