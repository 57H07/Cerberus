using Cerberus.Domain.Applications;
using Cerberus.Domain.Clients;
using Cerberus.Domain.Consents;
using Cerberus.Domain.Keys;
using Cerberus.Domain.Organizations;
using Cerberus.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cerberus.Infrastructure.Data.Configurations;

public class ClientApplicationConfiguration : IEntityTypeConfiguration<ClientApplication>
{
    public void Configure(EntityTypeBuilder<ClientApplication> builder)
    {
        builder.ToTable("ClientApplications");
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Name).HasMaxLength(ClientApplication.NameMaxLength).IsRequired();
        builder.Property(a => a.Description).HasMaxLength(ClientApplication.DescriptionMaxLength);
        builder.Property(a => a.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OwnerOrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasMany(a => a.OrganizationAccesses).WithOne().HasForeignKey(a => a.ClientApplicationId).OnDelete(DeleteBehavior.Cascade);
        builder.Navigation(a => a.OrganizationAccesses).UsePropertyAccessMode(PropertyAccessMode.Field).AutoInclude();
    }
}

public class ApplicationOrganizationAccessConfiguration : IEntityTypeConfiguration<ApplicationOrganizationAccess>
{
    public void Configure(EntityTypeBuilder<ApplicationOrganizationAccess> builder)
    {
        builder.ToTable("ApplicationOrganizationAccesses");
        builder.HasKey(a => a.Id);
        builder.HasIndex(a => new { a.ClientApplicationId, a.OrganizationId }).IsUnique().HasDatabaseName(UniqueIndexNames.ApplicationAccess);
        builder.HasIndex(a => a.OrganizationId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(a => a.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class OidcClientConfiguration : IEntityTypeConfiguration<OidcClient>
{
    public void Configure(EntityTypeBuilder<OidcClient> builder)
    {
        builder.ToTable("OidcClients");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.ClientId).HasMaxLength(OidcClient.ClientIdMaxLength).IsRequired();
        builder.Property(c => c.DisplayName).HasMaxLength(OidcClient.DisplayNameMaxLength).IsRequired();
        builder.Property(c => c.ClientType).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.ConsentPolicy).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(20);
        builder.PrimitiveCollection(c => c.RedirectUris);
        builder.PrimitiveCollection(c => c.PostLogoutRedirectUris);
        builder.PrimitiveCollection(c => c.AllowedScopes);
        builder.HasIndex(c => c.ClientId).IsUnique().HasDatabaseName(UniqueIndexNames.ClientId);
        builder.HasOne<ClientApplication>().WithMany().HasForeignKey(c => c.ClientApplicationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class ApiScopeConfiguration : IEntityTypeConfiguration<ApiScope>
{
    public void Configure(EntityTypeBuilder<ApiScope> builder)
    {
        builder.ToTable("ApiScopes");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.Name).HasMaxLength(100).IsRequired();
        builder.Property(s => s.DisplayName).HasMaxLength(200).IsRequired();
        builder.Property(s => s.Description).HasMaxLength(1000);
        builder.Property(s => s.Resource).HasMaxLength(100).IsRequired();
        builder.HasIndex(s => s.Name).IsUnique().HasDatabaseName(UniqueIndexNames.ScopeName);
    }
}

public class UserConsentConfiguration : IEntityTypeConfiguration<UserConsent>
{
    public void Configure(EntityTypeBuilder<UserConsent> builder)
    {
        builder.ToTable("UserConsents");
        builder.HasKey(c => c.Id);
        builder.PrimitiveCollection(c => c.GrantedScopes);
        builder.Property(c => c.AuthorizationId).HasMaxLength(100);
        builder.HasIndex(c => new { c.UserId, c.OidcClientId, c.OrganizationId }).IsUnique().HasDatabaseName(UniqueIndexNames.Consent);
        builder.HasOne<User>().WithMany().HasForeignKey(c => c.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasOne<OidcClient>().WithMany().HasForeignKey(c => c.OidcClientId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Organization>().WithMany().HasForeignKey(c => c.OrganizationId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class SigningKeyConfiguration : IEntityTypeConfiguration<SigningKey>
{
    public void Configure(EntityTypeBuilder<SigningKey> builder)
    {
        builder.ToTable("SigningKeys");
        builder.HasKey(k => k.Id);
        builder.Property(k => k.KeyId).HasMaxLength(100).IsRequired();
        builder.Property(k => k.Usage).HasConversion<string>().HasMaxLength(20);
        builder.Property(k => k.Algorithm).HasMaxLength(20).IsRequired();
        builder.Property(k => k.ProtectedPrivateKey).IsRequired();
        builder.HasIndex(k => k.KeyId).IsUnique();
    }
}
