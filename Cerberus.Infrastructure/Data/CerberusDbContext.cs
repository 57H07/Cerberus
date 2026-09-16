using Cerberus.Domain.Applications;
using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Consents;
using Cerberus.Domain.Keys;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Sessions;
using Cerberus.Domain.Users;
using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;

namespace Cerberus.Infrastructure.Data;

public class CerberusDbContext(DbContextOptions<CerberusDbContext> options) : DbContext(options), IDataProtectionKeyContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Organization> Organizations => Set<Organization>();
    public DbSet<OrganizationMembership> Memberships => Set<OrganizationMembership>();
    public DbSet<Invitation> Invitations => Set<Invitation>();
    public DbSet<ClientApplication> ClientApplications => Set<ClientApplication>();
    public DbSet<ApplicationOrganizationAccess> ApplicationOrganizationAccesses => Set<ApplicationOrganizationAccess>();
    public DbSet<OidcClient> OidcClients => Set<OidcClient>();
    public DbSet<ApiScope> ApiScopes => Set<ApiScope>();
    public DbSet<Permission> Permissions => Set<Permission>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserRoleAssignment> RoleAssignments => Set<UserRoleAssignment>();
    public DbSet<UserConsent> Consents => Set<UserConsent>();
    public DbSet<UserSession> Sessions => Set<UserSession>();
    public DbSet<AuditEntry> AuditEntries => Set<AuditEntry>();
    public DbSet<SigningKey> SigningKeys => Set<SigningKey>();
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(CerberusDbContext).Assembly);
        modelBuilder.UseOpenIddict<Guid>();
    }

    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }
}
