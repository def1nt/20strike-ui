using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Pages.ComputersV2;

namespace _20strike_ui.Tests;

public class ComputerListFilterTests
{
    private static readonly IReadOnlyList<ComputerSummary> Computers = new[]
    {
        new ComputerSummary("PC-1"),
        new ComputerSummary("PC-2"),
        new ComputerSummary("SRV-1"),
    };

    private static PingInfo? Ping(string name, bool? status) =>
        status is null ? null : new PingInfo(name, "10.0.0.1", status.Value);

    [Fact]
    public void Apply_NoFilter_ReturnsAll()
    {
        var result = ComputerListFilter.Apply(Computers, _ => null, "", hideOffline: false);

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Apply_NameFilter_FiltersCaseInsensitively()
    {
        var result = ComputerListFilter.Apply(Computers, _ => null, "pc", hideOffline: false);

        Assert.Equal(new[] { "PC-1", "PC-2" }, result.Select(c => c.Name));
    }

    [Fact]
    public void Apply_HideOffline_HidesOfflineButKeepsUnknown()
    {
        var result = ComputerListFilter.Apply(
            Computers,
            name => name == "PC-1" ? Ping(name, false) : name == "PC-2" ? Ping(name, true) : null,
            "",
            hideOffline: true);

        Assert.Equal(new[] { "PC-2", "SRV-1" }, result.Select(c => c.Name));
    }

    [Fact]
    public void Apply_CombinedNameAndOfflineFilter()
    {
        var result = ComputerListFilter.Apply(
            Computers,
            name => name == "PC-1" ? Ping(name, false) : Ping(name, true),
            "pc",
            hideOffline: true);

        Assert.Equal(new[] { "PC-2" }, result.Select(c => c.Name));
    }
}