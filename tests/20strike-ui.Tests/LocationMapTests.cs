using _20strike_ui.Data.ComputersV2;
using _20strike_ui.Pages.Map;

namespace _20strike_ui.Tests;

/// <summary>
/// Tests for the location map: location mapping from the v2 API, the normalized 0..1000 coordinate
/// space math and the case-insensitive name filter used by the map's search field.
/// </summary>
public class LocationMapTests
{
    // -------------------------------------------------------------------------------------------
    // API -> model mapping (locations)
    // -------------------------------------------------------------------------------------------

    [Fact]
    public void MapComputersWithLocations_TrimsNamesAndLocations_KeepsCoordinates()
    {
        var dtos = new List<ComputerDto>
        {
            new()
            {
                Name = "  PC-1  ",
                Location = new LocationDto { X = 123.4, Y = 567.8, Description = "  Server room  ", Phone = "  1234  " },
            },
            new() { Name = "PC-2", Location = null },
        };

        var result = ComputersV2Mapper.MapComputersWithLocations(dtos);

        var pc1 = Assert.Single(result, c => c.Name == "PC-1");
        Assert.NotNull(pc1.Location);
        Assert.Equal(123.4, pc1.Location!.X);
        Assert.Equal(567.8, pc1.Location.Y);
        Assert.Equal("Server room", pc1.Location.Description);
        Assert.Equal("1234", pc1.Location.Phone);

        var pc2 = Assert.Single(result, c => c.Name == "PC-2");
        Assert.Null(pc2.Location);
    }

    [Fact]
    public void MapComputersWithLocations_DropsBlankNames()
    {
        var dtos = new List<ComputerDto>
        {
            new() { Name = null },
            new() { Name = "   " },
            new() { Name = "PC-1", Location = null },
        };

        var result = ComputersV2Mapper.MapComputersWithLocations(dtos);

        var computer = Assert.Single(result);
        Assert.Equal("PC-1", computer.Name);
    }

    [Fact]
    public void MapLocation_NullInput_ReturnsNull()
    {
        Assert.Null(ComputersV2Mapper.MapLocation(null));
    }

    [Fact]
    public void MapLocation_TrimsStrings_PreservesEmptyInsteadOfPlaceholder()
    {
        var point = ComputersV2Mapper.MapLocation(new LocationDto
        {
            X = 1.5,
            Y = 2.5,
            Description = "   ",
            Phone = null,
        });

        // The UI always sends the complete MapData back on save, so blank fields must be stored as
        // empty strings, never as the "—" placeholder used for pure display.
        Assert.NotNull(point);
        Assert.Equal(1.5, point!.X);
        Assert.Equal(2.5, point.Y);
        Assert.Equal("", point.Description);
        Assert.Equal("", point.Phone);
    }

    // -------------------------------------------------------------------------------------------
    // MapGeometry: normalized 0..1000 coordinate space <-> CSS percentages
    // -------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(0, 0)]
    [InlineData(100, 10)]
    [InlineData(250, 25)]
    [InlineData(500, 50)]
    [InlineData(1000, 100)]
    public void ToPercentage_MapsCoordinateSpaceToPercent(double coordinate, double expected)
    {
        Assert.Equal(expected, MapGeometry.ToPercentage(coordinate));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(10, 100)]
    [InlineData(25, 250)]
    [InlineData(50, 500)]
    [InlineData(100, 1000)]
    public void ToCoordinate_MapsPercentBackToCoordinateSpace(double percentage, double expected)
    {
        Assert.Equal(expected, MapGeometry.ToCoordinate(percentage));
    }

    [Fact]
    public void ToPercentage_ClampsOutOfRange()
    {
        Assert.Equal(0, MapGeometry.ToPercentage(-50));
        Assert.Equal(100, MapGeometry.ToPercentage(1500));
    }

    [Fact]
    public void ToCoordinate_ClampsOutOfRange()
    {
        Assert.Equal(0, MapGeometry.ToCoordinate(-10));
        Assert.Equal(1000, MapGeometry.ToCoordinate(120));
    }

    [Fact]
    public void PercentageRoundTrip_IsStableAndRounded()
    {
        // Drag positions arrive as percentages; converting back rounds to one decimal place so
        // stored coordinates stay short and comparable.
        Assert.Equal(345.7, MapGeometry.ToCoordinate(MapGeometry.ToPercentage(345.6789)), precision: 1);
    }

    [Theory]
    [InlineData(123.456, 123.5)]
    [InlineData(100, 100)]
    [InlineData(0, 0)]
    public void RoundCoordinate_RoundsToOneDecimal(double coordinate, double expected)
    {
        Assert.Equal(expected, MapGeometry.RoundCoordinate(coordinate));
    }

    // -------------------------------------------------------------------------------------------
    // MapComputerFilter: case-insensitive search over the map's working set
    // -------------------------------------------------------------------------------------------

    private static readonly List<ComputerLocationSummary> SampleComputers = new()
    {
        new("PC-001", new MapPoint(100, 200, "Серверная", "123")),
        new("PC-002", null),
        new("WB-LAB-1", null),
    };

    [Fact]
    public void Filter_BlankQuery_ReturnsEverything()
    {
        var result = MapComputerFilter.Apply(SampleComputers, "   ");

        Assert.Equal(3, result.Count);
    }

    [Fact]
    public void Filter_MatchesSubstringCaseInsensitively_AndKeepsLocations()
    {
        var result = MapComputerFilter.Apply(SampleComputers, "pc-00");

        Assert.Equal(2, result.Count);
        var pc1 = Assert.Single(result, c => c.Name == "PC-001");
        Assert.NotNull(pc1.Location);
        Assert.Equal("Серверная", pc1.Location!.Description);
        Assert.Contains(result, c => c.Name == "PC-002" && c.Location is null);
    }

    [Fact]
    public void Filter_NoMatch_ReturnsEmpty()
    {
        Assert.Empty(MapComputerFilter.Apply(SampleComputers, "zzz"));
    }
}