using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Aspire.Hosting.Testing;

namespace UZLLM.AppHost.IntegrationTests;

public sealed class AppHostSmokeTests
{
    [Fact]
    public void VisualStudio_launch_profile_starts_AppHost_dashboard()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "UZLLM.slnx")))
            directory = directory.Parent;

        Assert.NotNull(directory);
        var path = Path.Combine(directory.FullName, "src", "UZLLM.AppHost", "Properties", "launchSettings.json");
        Assert.True(File.Exists(path), "Visual Studio needs an AppHost launch profile for F5 startup.");

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var profile = document.RootElement.GetProperty("profiles").GetProperty("https");
        Assert.Equal("Project", profile.GetProperty("commandName").GetString());
        Assert.True(profile.GetProperty("launchBrowser").GetBoolean());
        Assert.StartsWith("https://localhost:", profile.GetProperty("applicationUrl").GetString());
        var environment = profile.GetProperty("environmentVariables");
        Assert.Equal("Development", environment.GetProperty("DOTNET_ENVIRONMENT").GetString());
        Assert.StartsWith("https://localhost:", environment.GetProperty("ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL").GetString());
        Assert.StartsWith("https://localhost:", environment.GetProperty("ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL").GetString());
    }

    [Fact(Timeout = 240_000)]
    public async Task FullStack_starts_in_order_and_connects_frontends_gateway_and_worker()
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(4));
        var builder = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.UZLLM_AppHost>(cancellationToken: timeout.Token);
        await using var app = await builder.BuildAsync(timeout.Token);
        await app.StartAsync(timeout.Token);

        await app.ResourceNotifications.WaitForResourceHealthyAsync("management", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("gateway", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("dashboard", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("admin", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("provider-fixture", timeout.Token);
        await app.ResourceNotifications.WaitForResourceHealthyAsync("mailpit", timeout.Token);

        using var management = app.CreateHttpClient("management");
        using var gateway = app.CreateHttpClient("gateway");
        using var dashboard = app.CreateHttpClient("dashboard");
        using var admin = app.CreateHttpClient("admin");
        using var provider = app.CreateHttpClient("provider-fixture", "https");
        using var mailpit = app.CreateHttpClient("mailpit", "http");

        Assert.Equal(HttpStatusCode.OK, (await management.GetAsync("/health/ready", timeout.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await gateway.GetAsync("/health/ready", timeout.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await dashboard.GetAsync("/", timeout.Token)).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync("/", timeout.Token)).StatusCode);

        var fixture = await provider.PostAsJsonAsync("/v1/chat/completions", new
        {
            model = "fixture-model",
            messages = new[] { new { role = "user", content = "hello" } },
            stream = false
        }, timeout.Token);
        Assert.Equal(HttpStatusCode.OK, fixture.StatusCode);
        using (var response = await JsonDocument.ParseAsync(await fixture.Content.ReadAsStreamAsync(timeout.Token),
                   cancellationToken: timeout.Token))
            Assert.Equal("Fixture complete.", response.RootElement.GetProperty("choices")[0]
                .GetProperty("message").GetProperty("content").GetString());


        var email = $"aspire-{Guid.NewGuid():N}@example.invalid";
        var registration = new { email, password = "Aspire-local-test-123!" };
        var dashboardRegistration = await dashboard.PostAsJsonAsync("/management/v1/auth/register",
            registration, timeout.Token);
        Assert.Equal(HttpStatusCode.Accepted, dashboardRegistration.StatusCode);
        Assert.True(dashboardRegistration.Headers.Contains("X-Request-Id"));
        var adminRegistration = await admin.PostAsJsonAsync("/management/v1/auth/register",
            new { email = $"admin-proxy-{Guid.NewGuid():N}@example.invalid", registration.password }, timeout.Token);
        Assert.Equal(HttpStatusCode.Accepted, adminRegistration.StatusCode);
        Assert.True(adminRegistration.Headers.Contains("X-Request-Id"));

        var delivered = false;
        for (var attempt = 0; attempt < 60 && !delivered; attempt++)
        {
            using var messages = await JsonDocument.ParseAsync(
                await mailpit.GetStreamAsync("/api/v1/messages", timeout.Token), cancellationToken: timeout.Token);
            delivered = messages.RootElement.GetProperty("messages").EnumerateArray().Any(message =>
                message.GetProperty("To").EnumerateArray().Any(recipient =>
                    string.Equals(recipient.GetProperty("Address").GetString(), email,
                        StringComparison.OrdinalIgnoreCase)));
            if (!delivered) await Task.Delay(500, timeout.Token);
        }
        Assert.True(delivered, "Worker did not deliver the verification email through Mailpit STARTTLS.");
    }
}
