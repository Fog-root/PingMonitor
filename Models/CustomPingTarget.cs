namespace DotaPingMonitor.Models;

public class CustomPingTarget
{
    public int Id { get; set; }

    public string GameId { get; set; } = "custom";

    public string Name { get; set; } = string.Empty;

    public string Host { get; set; } = string.Empty;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
