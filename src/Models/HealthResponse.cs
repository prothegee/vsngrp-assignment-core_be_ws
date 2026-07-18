namespace VsngrpCoreBeWs.Models;

public sealed class HealthResponse
{
    public string Status { get; set; } = "ok";
    public string Version { get; set; } = string.Empty;
    public string GitSha { get; set; } = string.Empty;
}
