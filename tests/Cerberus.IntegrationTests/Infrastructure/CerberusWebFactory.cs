using Cerberus.Application.Interfaces.Security;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Testcontainers.MsSql;

namespace Cerberus.IntegrationTests.Infrastructure;

/// <summary>
/// Runs the real identity provider (MVC, OpenIddict, EF Core migrations) against a disposable SQL Server container.
/// </summary>
public sealed class CerberusWebFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    public const string Issuer = "https://localhost/";

    private readonly MsSqlContainer _database = new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest")
        .Build();

    public CapturingEmailSender Emails { get; } = new();

    public async Task InitializeAsync()
    {
        await _database.StartAsync();
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        await _database.DisposeAsync();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        // UseSetting is applied before Program reads its configuration (unlike ConfigureAppConfiguration).
        builder.UseSetting("ConnectionStrings:DefaultConnection", _database.GetConnectionString().Replace("Database=master", "Database=CerberusTests"));
        builder.UseSetting("Database:MigrateOnStartup", "true");
        builder.UseSetting("Oidc:Issuer", Issuer);
        builder.UseSetting("Security:RateLimiting:AuthenticationPermitsPerMinute", "10000");
        builder.UseSetting("Security:RateLimiting:TokenPermitsPerMinute", "10000");
        builder.UseSetting("Seed:Enabled", "false");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<IEmailSender>();
            services.AddSingleton<IEmailSender>(Emails);
        });
    }

    public HttpClient CreateBrowser()
        => CreateClient(new WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            BaseAddress = new Uri("https://localhost"),
            HandleCookies = true
        });
}

public sealed class CapturingEmailSender : IEmailSender
{
    private readonly List<(string To, string Subject, string Body)> _messages = [];

    public Task SendAsync(string to, string subject, string htmlBody, CancellationToken cancellationToken = default)
    {
        lock (_messages)
        {
            _messages.Add((to, subject, htmlBody));
        }

        return Task.CompletedTask;
    }

    public (string To, string Subject, string Body)? LastTo(string to)
    {
        lock (_messages)
        {
            return _messages.LastOrDefault(m => string.Equals(m.To, to, StringComparison.OrdinalIgnoreCase)) is { To: not null } message
                ? message
                : null;
        }
    }
}

[CollectionDefinition(Name)]
public sealed class IntegrationCollection : ICollectionFixture<CerberusWebFactory>
{
    public const string Name = "integration";
}
