using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Pages.ComputersV2;

namespace _20strike_ui.Tests;

public class ComputerDetailViewFactoryTests
{
    [Fact]
    public void BuildTabs_ReturnsEightTabsWithSoftwareAndProcessesAsLarge()
    {
        var tabs = ComputerDetailViewFactory.BuildTabs(new ComputerDetail());

        Assert.Equal(
            new[] { "system", "storage", "cpu", "video", "network", "printers", "software", "processes" },
            tabs.Tabs.Select(t => t.Id));
        Assert.NotNull(tabs.Tabs[6].Large); // software
        Assert.NotNull(tabs.Tabs[7].Large); // processes
        Assert.Equal(6, tabs.Sections.Count);
    }

    [Fact]
    public void BuildTabs_EmptyDetail_ProducesEmptySections()
    {
        var tabs = ComputerDetailViewFactory.BuildTabs(new ComputerDetail());

        var system = tabs.Sections["system"];
        Assert.Equal(3, system.Count); // BIOS, Компьютер, Операционная система
        Assert.All(system, s => Assert.Empty(s.Fields));
        Assert.Empty(tabs.Sections["storage"][0].Rows);
    }

    [Fact]
    public void BuildTabs_WithRows_BuildsHeadersAndFormattedCells()
    {
        var detail = new ComputerDetail
        {
            PhysicalDisk = new[]
            {
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["name"] = "Disk 0",
                    ["size"] = "1073741824",
                },
            },
        };

        var tabs = ComputerDetailViewFactory.BuildTabs(detail);

        var storage = tabs.Sections["storage"][0];
        Assert.Equal(new[] { "Name", "Size" }, storage.Headers);
        Assert.Equal(new[] { "Disk 0", "1 ГБ" }, storage.Rows[0]);
    }
}