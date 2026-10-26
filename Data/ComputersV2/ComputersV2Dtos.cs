using System.Text.Json;

namespace _20strike_ui.Data.ComputersV2;

// ---------- Raw DTOs (back-end shape). Location is intentionally ignored for now. ----------

internal sealed class ComputerDto
{
    public string? Name { get; set; }
}

internal sealed class PingDto
{
    public string? Name { get; set; }
    public string? Ip { get; set; }
    public bool Status { get; set; }
}

internal sealed class ProblemDto
{
    public string? Name { get; set; }
    public string? Description { get; set; }
}

// Section values arrive as strings but may occasionally be numbers; JsonElement keeps the
// deserialization robust for both.
internal sealed class DetailDto
{
    public Dictionary<string, JsonElement>? Bios { get; set; }
    public Dictionary<string, JsonElement>? ComputerSystem { get; set; }
    public Dictionary<string, JsonElement>? OperatingSystem { get; set; }
    public List<Dictionary<string, JsonElement>>? Processor { get; set; }
    public List<Dictionary<string, JsonElement>>? PhysicalMemory { get; set; }
    public List<Dictionary<string, JsonElement>>? PhysicalDisk { get; set; }
    public List<Dictionary<string, JsonElement>>? LogicalDisk { get; set; }
    public List<Dictionary<string, JsonElement>>? VideoController { get; set; }
    public List<Dictionary<string, JsonElement>>? Monitor { get; set; }
    public List<Dictionary<string, JsonElement>>? NetworkAdapter { get; set; }
    public List<Dictionary<string, JsonElement>>? Printer { get; set; }
    public List<Dictionary<string, JsonElement>>? Software { get; set; }
    public List<Dictionary<string, JsonElement>>? Process { get; set; }
}