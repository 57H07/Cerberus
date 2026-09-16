using Cerberus.Domain.Auditing;
using Cerberus.Domain.Authorization;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cerberus.Infrastructure.Data.Configurations;

public class PermissionConfiguration : IEntityTypeConfiguration<Permission>
{
    public void Configure(EntityTypeBuilder<Permission> builder)
    {
        builder.ToTable("Permissions");
        builder.HasKey(p => p.Code);
        builder.Property(p => p.Code).HasMaxLength(100);
        builder.Property(p => p.Scope).HasConversion<string>().HasMaxLength(20);
        builder.Property(p => p.Description).HasMaxLength(500).IsRequired();

        // The catalogue is code-defined: seeding it keeps foreign keys from role permissions consistent in every environment.
        builder.HasData(PermissionCatalog.All.Select(p => new { p.Code, p.Scope, p.Description }));
    }
}

public class RoleConfiguration : IEntityTypeConfiguration<Role>
{
    public void Configure(EntityTypeBuilder<Role> builder)
    {
        builder.ToTable("Roles", t => t.HasCheckConstraint(
            "CK_Roles_ScopeOrganization",
            "([Scope] = 'Platform' AND [OrganizationId] IS NULL) OR ([Scope] = 'Organization' AND [OrganizationId] IS NOT NULL)"));
        builder.HasKey(r => r.Id);
        builder.Property(r => r.Name).HasMaxLength(Role.NameMaxLength).IsRequired();
        builder.Property(r => r.NormalizedName).HasMaxLength(Role.NameMaxLength).IsRequired();
        builder.Property(r => r.Description).HasMaxLength(500);
        builder.Property(r => r.Scope).HasConversion<string>().HasMaxLength(20);
        // No filter: SQL Server treats NULL organization ids as equal, so platform role names are unique too.
        builder.HasIndex(r => new { r.Scope, r.OrganizationId, r.NormalizedName }).IsUnique().HasDatabaseName(UniqueIndexNames.RoleName)
            .HasFilter(null);
        builder.HasOne<Organization>().WithMany().HasForeignKey(r => r.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(r => r.Permissions).WithOne().HasForeignKey(p => p.RoleId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(r => r.Permissions).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public class RolePermissionConfiguration : IEntityTypeConfiguration<RolePermission>
{
    public void Configure(EntityTypeBuilder<RolePermission> builder)
    {
        builder.ToTable("RolePermissions");
        builder.HasKey(rp => new { rp.RoleId, rp.PermissionCode });
        builder.Property(rp => rp.PermissionCode).HasMaxLength(100);
        builder.HasOne<Permission>().WithMany().HasForeignKey(rp => rp.PermissionCode).OnDelete(DeleteBehavior.Restrict);
    }
}

public class UserRoleAssignmentConfiguration : IEntityTypeConfiguration<UserRoleAssignment>
{
    public void Configure(EntityTypeBuilder<UserRoleAssignment> builder)
    {
        builder.ToTable("UserRoleAssignments");
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.UserId, a.RoleId }).IsUnique().HasDatabaseName(UniqueIndexNames.RoleAssignment);
        builder.HasIndex(a => new { a.OrganizationId, a.UserId });
        builder.HasOne<User>().WithMany().HasForeignKey(a => a.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<Role>().WithMany().HasForeignKey(a => a.RoleId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class AuditEntryConfiguration : IEntityTypeConfiguration<AuditEntry>
{
    public void Configure(EntityTypeBuilder<AuditEntry> builder)
    {
        builder.ToTable("AuditEntries");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Action).HasMaxLength(100).IsRequired();
        builder.Property(a => a.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.Property(a => a.TargetType).HasMaxLength(100);
        builder.Property(a => a.TargetId).HasMaxLength(100);
        builder.Property(a => a.IpAddress).HasMaxLength(64);
        builder.Property(a => a.Details).HasMaxLength(AuditEntry.DetailsMaxLength);
        builder.HasIndex(a => a.OccurredAt);
        builder.HasIndex(a => new { a.OrganizationId, a.OccurredAt });
    }
}

public static class UniqueIndexNames
{
    public const string UserEmail = "IX_Users_NormalizedEmail";
    public const string UserName = "IX_Users_NormalizedUserName";
    public const string OrganizationSlug = "IX_Organizations_Slug";
    public const string Membership = "IX_OrganizationMemberships_OrganizationId_UserId";
    public const string ApplicationAccess = "IX_ApplicationOrganizationAccesses_Application_Organization";
    public const string ClientId = "IX_OidcClients_ClientId";
    public const string ScopeName = "IX_ApiScopes_Name";
    public const string Consent = "IX_UserConsents_User_Client_Organization";
    public const string RoleName = "IX_Roles_Scope_Organization_Name";
    public const string RoleAssignment = "IX_UserRoleAssignments_UserId_RoleId";
}
