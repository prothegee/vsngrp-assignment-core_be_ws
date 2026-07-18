using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using VsngrpCoreBeWs.Services;

namespace VsngrpCoreBeWs.Tests.Integration;

public sealed class CoreBeWsWebApplicationFactory(string configPath, FakeDeepSeekClient fakeDeepSeekClient) : WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        builder.UseSetting("CONFIG_PATH", configPath);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IDeepSeekClient>();
            services.AddSingleton<IDeepSeekClient>(fakeDeepSeekClient);
        });
    }
}
