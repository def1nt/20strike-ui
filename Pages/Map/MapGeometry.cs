namespace _20strike_ui.Pages.Map;

/// <summary>
/// Pure coordinate math for the location map. Marker positions live in a normalized 0..1000
/// coordinate space (X right, Y down) that is independent of the image resolution; rendering
/// converts coordinates into CSS percentages of the image via <see cref="ToPercentage"/>.
/// </summary>
public static class MapGeometry
{
    /// <summary>The size of the normalized map coordinate space (coordinates run 0..CoordinateSpaceSize).</summary>
    public const double CoordinateSpaceSize = 1000;

    private const double MinCoordinate = 0;
    private const double MaxCoordinate = CoordinateSpaceSize;

    /// <summary>Converts a map coordinate (0..1000) into a CSS percentage (0..100), clamped.</summary>
    public static double ToPercentage(double coordinate) =>
        Clamp(coordinate / CoordinateSpaceSize * 100, 0, 100);

    /// <summary>Converts a CSS percentage (0..100) into a map coordinate (0..1000), clamped and rounded.</summary>
    public static double ToCoordinate(double percentage) =>
        RoundCoordinate(Clamp(percentage / 100 * CoordinateSpaceSize, MinCoordinate, MaxCoordinate));

    /// <summary>
    /// Rounds a coordinate to one decimal place so stored values stay short and comparable. The
    /// backend stores plain doubles, so the precision here is purely cosmetic.
    /// </summary>
    public static double RoundCoordinate(double coordinate) => Math.Round(coordinate, 1);

    private static double Clamp(double value, double min, double max) => Math.Min(max, Math.Max(min, value));
}