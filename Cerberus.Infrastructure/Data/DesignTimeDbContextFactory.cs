using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Cerberus.Infrastructure.Data;

/// <summary>
/// Used by <c>dotnet ef</c> only. The connection string is never used to connect when generating migrations;
/// override it with the CERBERUS_DESIGN_CONNECTION variable for commands that reach the database.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<CerberusDbContext>
{
    public CerberusDbContext CreateDbContext(string[] args)
    {
        var connectionString = Environment.GetEnvironmentVariable("CERBERUS_DESIGN_CONNECTION")
            ?? "Server=localhost,1433;Database=Cerberus;Integrated Security=false;TrustServerCertificate=true";
        var options = new DbContextOptionsBuilder<CerberusDbContext>()
            .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(CerberusDbContext).Assembly.FullName))
            .Options;
        return new CerberusDbContext(options);
    }
}
