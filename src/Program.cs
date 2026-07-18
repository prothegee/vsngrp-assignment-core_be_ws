using StackExchange.Redis;
using VsngrpCoreBeWs.Models;
using VsngrpCoreBeWs.Services;
using VsngrpCoreBeWs.WebSockets;

var builder = WebApplication.CreateBuilder(args);

var configPath = builder.Configuration["CONFIG_PATH"] ?? Path.Combine(Directory.GetCurrentDirectory(), "..", "config", "config.json");
builder.Configuration.AddJsonFile(configPath, optional: false, reloadOnChange: false);

var appConfig = builder.Configuration.Get<AppConfig>() ?? throw new InvalidOperationException("config.json failed to bind to AppConfig.");
builder.Services.AddSingleton(appConfig);

builder.WebHost.UseUrls($"http://0.0.0.0:{appConfig.Port}");

builder.Services.AddKeyedSingleton<IConnectionMultiplexer>("own", (_, _) => ConnectionMultiplexer.Connect(appConfig.Redis.ConnectionString));
builder.Services.AddKeyedSingleton<IConnectionMultiplexer>("session", (_, _) => ConnectionMultiplexer.Connect(appConfig.SessionRedis.ConnectionString));

builder.Services.AddScoped<IJwtVerifyService, JwtVerifyService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddScoped<IConversationService, ConversationService>();
builder.Services.AddScoped<IChatLogService, ChatLogService>();
builder.Services.AddScoped<ITokenBudgetService, TokenBudgetService>();
builder.Services.AddSingleton<HttpClient>();
builder.Services.AddSingleton<IDeepSeekClient, DeepSeekClient>();
builder.Services.AddScoped<ChatWebSocketHandler>();

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy
            .WithOrigins(appConfig.CorsAllowedOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod();
    });
});

var app = builder.Build();

app.UseCors();
app.UseWebSockets();

app.MapGet("/health", () => Results.Ok(new HealthResponse
{
    Status = "ok",
    Version = appConfig.Version,
    GitSha = Environment.GetEnvironmentVariable("GIT_SHA") ?? "dev",
}));

app.Map("/ws/chat", async context =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }

    var origin = context.Request.Headers.Origin.ToString();
    if (!string.IsNullOrEmpty(origin) && !appConfig.CorsAllowedOrigins.Contains(origin))
    {
        context.Response.StatusCode = StatusCodes.Status403Forbidden;
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var handler = context.RequestServices.GetRequiredService<ChatWebSocketHandler>();
    await handler.HandleAsync(webSocket, context.RequestAborted);
});

app.Run();

public partial class Program;
