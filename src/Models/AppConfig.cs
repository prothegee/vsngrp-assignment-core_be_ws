namespace VsngrpCoreBeWs.Models;

public sealed class AppConfig
{
    public int Port { get; set; }
    public string Version { get; set; } = "0.1.0";
    public string JwtSecret { get; set; } = string.Empty;
    public RedisConfig Redis { get; set; } = new();
    public RedisConfig SessionRedis { get; set; } = new();
    public DeepSeekConfig DeepSeek { get; set; } = new();
    public string[] CorsAllowedOrigins { get; set; } = [];
}

public sealed class RedisConfig
{
    public string ConnectionString { get; set; } = string.Empty;
}

public sealed class DeepSeekConfig
{
    public string ApiKey { get; set; } = string.Empty;
    public string BaseUrl { get; set; } = "https://api.deepseek.com";
    public string Model { get; set; } = "deepseek-v4-flash";
    public long DailyTokenBudgetPerAccount { get; set; } = 100_000;
    public int MaxConcurrentRequests { get; set; } = 200;
}
