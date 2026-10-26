using System.Globalization;
using System.Text.Json;
using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Formatting;
using _20strike_ui.Pages.ComputersV2;

namespace _20strike_ui.Tests;

/// <summary>
/// Baseline tests locking the current behavior of <see cref="ComputersV2Mapper"/> before the
/// refactor splits mapping, formatting and view-model building into separate units.
/// </summary>
public class ComputersV2MapperTests
{
    // -------------------------------------------------------------------------------------------
    // API -> model mapping
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void MapComputers_DropsBlankNames_AndTrims()
    {
        var dtos = new List<ComputerDto>
        {
            new() { Name = "  PC-1  " },
            new() { Name = null },
            new() { Name = "   " },
            new() { Name = "PC-2" },
        };

        var result = ComputersV2Mapper.MapComputers(dtos);

        Assert.Equal(new[] { "PC-1", "PC-2" }, result.Select(r => r.Name));
    }

    [Fact]
    public void MapComputers_NullInput_ReturnsEmpty()
    {
        var result = ComputersV2Mapper.MapComputers(null);
        Assert.Empty(result);
    }

    [Fact]
    public void MapSearchResults_TrimsDropsBlanks_AndDeduplicatesCaseInsensitively()
    {
        var raw = new List<string> { " PC-1 ", "pc-1", "", " PC-2 " };

        var result = ComputersV2Mapper.MapSearchResults(raw);

        Assert.Equal(new[] { "PC-1", "PC-2" }, result.Select(r => r.Name));
    }

    [Fact]
    public void MapPing_TrimsNameAndIp_DropsBlankNames()
    {
        var dtos = new List<PingDto>
        {
            new() { Name = " PC-1 ", Ip = " 10.0.0.1 ", Status = true },
            new() { Name = null, Ip = "x", Status = false },
        };

        var result = ComputersV2Mapper.MapPing(dtos);

        var ping = Assert.Single(result);
        Assert.Equal("PC-1", ping.Name);
        Assert.Equal("10.0.0.1", ping.Ip);
        Assert.True(ping.Status);
    }

    [Fact]
    public void MapProblems_TrimsNameAndDescription()
    {
        var dtos = new List<ProblemDto> { new() { Name = "  P1  ", Description = "  desc  " } };

        var result = ComputersV2Mapper.MapProblems(dtos);

        var problem = Assert.Single(result);
        Assert.Equal("P1", problem.Name);
        Assert.Equal("desc", problem.Description);
    }

    [Fact]
    public void MapUsers_LowercasesKeysTrims_DropsBlankEntries_AndIsCaseInsensitive()
    {
        var raw = new Dictionary<string, string>
        {
            ["  Ivanov-AV  "] = "  Иван Иванов  ",
            ["empty"] = "   ",
            ["   "] = "x",
        };

        var result = ComputersV2Mapper.MapUsers(raw);

        Assert.Single(result);
        Assert.True(result.ContainsKey("ivanov-av"));
        Assert.Equal("Иван Иванов", result["ivanov-av"]);
        Assert.True(result.TryGetValue("IVANOV-AV", out _));
    }

    [Fact]
    public void MapUsers_NullInput_ReturnsEmptyCaseInsensitiveMap()
    {
        var result = ComputersV2Mapper.MapUsers(null);
        Assert.Empty(result);
        Assert.True(result.TryGetValue("ANY", out _) == false);
    }

    [Fact]
    public void MapDetail_NormalizesSectionsAndFormatsItems()
    {
        var dto = DeserializeDetail(SampleDetailJson);

        var detail = ComputersV2Mapper.MapDetail(dto);

        // Object sections: keys camelCased, values trimmed.
        Assert.Equal("AMI", detail.Bios["manufacturer"]);
        Assert.Equal("PC-1", detail.ComputerSystem["name"]);
        Assert.Equal("Windows 10", detail.OperatingSystem["name"]);

        // Row sections.
        Assert.Equal("Intel Core i5", detail.Processor[0]["name"]);
        Assert.Equal("8589934592", detail.PhysicalMemory[0]["capacity"]);

        // Software: WMI date (yyyyMMdd) and KB sizes formatted.
        var software = Assert.Single(detail.Software);
        Assert.Equal("App", software.Name);
        Assert.Equal("1.0", software.Version);
        Assert.Equal("01.01.2025", software.InstallDate);
        Assert.Equal(@"C:\App", software.InstallLocation);
        Assert.Equal("1 МБ", software.EstimatedSize);

        // Process: byte sizes and full WMI dates formatted.
        var process = Assert.Single(detail.Process);
        Assert.Equal("app.exe", process.Name);
        Assert.Equal("1234", process.ProcessId);
        Assert.Equal("1 МБ", process.WorkingSetSize);
        Assert.Equal("01.01.2026 09:00:00", process.CreationDate);
    }

    [Fact]
    public void MapDetail_NullSections_ProducesEmptyDetail()
    {
        var detail = ComputersV2Mapper.MapDetail(new DetailDto());

        Assert.Empty(detail.Bios);
        Assert.Empty(detail.ComputerSystem);
        Assert.Empty(detail.OperatingSystem);
        Assert.Empty(detail.Processor);
        Assert.Empty(detail.PhysicalMemory);
        Assert.Empty(detail.Software);
        Assert.Empty(detail.Process);
    }

    // -------------------------------------------------------------------------------------------
    // View-model building
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void BuildSummary_ExtractsKeyFields()
    {
        var detail = ComputersV2Mapper.MapDetail(DeserializeDetail(SampleDetailJson));

        var summary = ComputerDetailViewFactory.BuildSummary(detail);

        Assert.Equal("PC-1", summary.Name);
        Assert.Equal("VMZ", summary.Domain);
        Assert.Equal(@"VMZ\ivanov-av", summary.UserName);
        Assert.Equal("Dell", summary.Manufacturer);
        Assert.Equal("Windows 10", summary.OsName);
        Assert.Equal("10.0.19045", summary.OsVersion);
        Assert.Equal("19045", summary.OsBuild);
        Assert.Equal("x64", summary.OsArchitecture);
        Assert.Equal("Intel Core i5", summary.Cpu);
        Assert.Equal("8 ГБ", summary.RamTotal);
        Assert.Equal("100 ГБ / 200 ГБ", summary.DiskCFree);
        Assert.Equal("01.01.2026 09:00:00", summary.LastBootUpTime);
        Assert.Equal("01.01.2026 12:00:00", summary.LocalTime);
    }

    [Fact]
    public void BuildSoftwareSection_BuildsColumnsRowsAndSearchBlob()
    {
        var detail = ComputersV2Mapper.MapDetail(DeserializeDetail(SampleDetailJson));

        var section = ComputerDetailViewFactory.BuildSoftwareSection(detail);

        Assert.Equal("software", section.Id);
        Assert.Equal("Программы", section.Title);
        Assert.Equal(new[] { "Имя", "Версия", "Дата установки", "Путь установки", "Размер" }, section.Columns);
        var row = Assert.Single(section.Rows);
        Assert.Equal("app 1.0 01.01.2025 c:\\app 1 мб", row.SearchBlob);
    }

    [Fact]
    public void BuildProcessSection_BuildsColumnsRowsAndSearchBlob()
    {
        var detail = ComputersV2Mapper.MapDetail(DeserializeDetail(SampleDetailJson));

        var section = ComputerDetailViewFactory.BuildProcessSection(detail);

        Assert.Equal("processes", section.Id);
        Assert.Equal("Процессы", section.Title);
        Assert.Equal(new[] { "Имя", "PID", "Память", "Время запуска" }, section.Columns);
        var row = Assert.Single(section.Rows);
        Assert.Equal("app.exe 1234 1 мб 01.01.2026 09:00:00", row.SearchBlob);
    }

    // -------------------------------------------------------------------------------------------
    // Formatting helpers
    // -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("userName", "UserName")]
    [InlineData("a", "A")]
    [InlineData("", "")]
    public void TitleCase_UpperCasesFirstCharacter(string input, string expected)
    {
        Assert.Equal(expected, ValueFormatters.TitleCase(input));
    }

    [Theory]
    [InlineData(@"VMZ\ivanov-av", "ivanov-av")]
    [InlineData("ivanov-av", "ivanov-av")]
    [InlineData("  IVANOV-AV  ", "ivanov-av")]
    [InlineData("", "")]
    [InlineData("—", "")]
    public void NormalizeLogin_StripsDomainAndLowercases(string input, string expected)
    {
        Assert.Equal(expected, ValueFormatters.NormalizeLogin(input));
    }

    [Fact]
    public void FormatUserName_ShowsFullNameWithOriginalInParentheses()
    {
        var users = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ivanov-av"] = "Иван Иванов",
        };

        Assert.Equal(@"Иван Иванов (VMZ\ivanov-av)", ValueFormatters.FormatUserName(@"VMZ\ivanov-av", users));
        Assert.Equal("unknown", ValueFormatters.FormatUserName("unknown", users));
        Assert.Equal("—", ValueFormatters.FormatUserName("—", users));
        Assert.Equal("", ValueFormatters.FormatUserName("", users));
    }

    [Theory]
    [InlineData(null, "—")]
    [InlineData("", "—")]
    [InlineData("abc", "—")]
    [InlineData("1", "1 Б")]
    [InlineData("1024", "1 КБ")]
    [InlineData("1048576", "1 МБ")]
    [InlineData("1073741824", "1 ГБ")]
    [InlineData("1610612736", "1,5 ГБ")]
    public void FormatBytes_FormatsReadableUnits(string? input, string expected)
    {
        // The decimal separator follows the current culture (the UI is Russian); pin it so the
        // test is deterministic on any machine.
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("ru-RU");
            Assert.Equal(expected, ValueFormatters.FormatBytes(input));
        }
        finally
        {
            CultureInfo.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData("20260101090000.000000+000", "01.01.2026 09:00:00")]
    [InlineData(null, "—")]
    [InlineData("short", "—")]
    public void FormatWmiDate_FormatsOrFallsBack(string? input, string expected)
    {
        Assert.Equal(expected, ValueFormatters.FormatWmiDate(input));
    }

    // -------------------------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------------------------

    private const string SampleDetailJson = """
        {
          "Bios": { "Manufacturer": "AMI", "Version": "1.0" },
          "ComputerSystem": { "Name": "PC-1", "Domain": "VMZ", "UserName": "VMZ\\ivanov-av", "Manufacturer": "Dell" },
          "OperatingSystem": {
            "Name": "Windows 10", "Version": "10.0.19045", "BuildNumber": "19045", "Architecture": "x64",
            "LastBootUpTime": "20260101090000.000000+000", "LocalTime": "20260101120000.000000+000"
          },
          "Processor": [ { "Name": "Intel Core i5" } ],
          "PhysicalMemory": [ { "Capacity": "8589934592" } ],
          "LogicalDisk": [ { "Name": "C:", "FreeSpace": "107374182400", "Size": "214748364800" } ],
          "Software": [ { "Name": "App", "Version": "1.0", "InstallDate": "20250101", "InstallLocation": "C:\\App", "EstimatedSize": "1024" } ],
          "Process": [ { "Name": "app.exe", "ProcessId": "1234", "WorkingSetSize": "1048576", "CreationDate": "20260101090000.000000+000" } ]
        }
        """;

    private static DetailDto DeserializeDetail(string json) =>
        JsonSerializer.Deserialize<DetailDto>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
}