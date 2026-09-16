using Cerberus.Domain.Organizations;
using Cerberus.Domain.Sessions;
using Cerberus.Domain.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Cerberus.Infrastructure.Data.Configurations;

public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.FirstName).HasMaxLength(User.NameMaxLength).IsRequired();
        builder.Property(u => u.LastName).HasMaxLength(User.NameMaxLength).IsRequired();
        builder.Property(u => u.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.NormalizedEmail).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(u => u.UserName).HasMaxLength(User.UserNameMaxLength).IsRequired();
        builder.Property(u => u.NormalizedUserName).HasMaxLength(User.UserNameMaxLength).IsRequired();
        builder.Property(u => u.PasswordHash).HasMaxLength(500);
        builder.Property(u => u.SecurityStamp).HasMaxLength(128).IsRequired().IsConcurrencyToken();
        builder.Property(u => u.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(u => u.NormalizedEmail).IsUnique().HasDatabaseName(UniqueIndexNames.UserEmail);
        builder.HasIndex(u => u.NormalizedUserName).IsUnique().HasDatabaseName(UniqueIndexNames.UserName);
    }
}

public class OrganizationConfiguration : IEntityTypeConfiguration<Organization>
{
    public void Configure(EntityTypeBuilder<Organization> builder)
    {
        builder.ToTable("Organizations");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.Name).HasMaxLength(Organization.NameMaxLength).IsRequired();
        builder.Property(o => o.Slug).HasMaxLength(63).IsRequired();
        builder.Property(o => o.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(o => o.Slug).IsUnique().HasDatabaseName(UniqueIndexNames.OrganizationSlug);
    }
}

public class OrganizationMembershipConfiguration : IEntityTypeConfiguration<OrganizationMembership>
{
    public void Configure(EntityTypeBuilder<OrganizationMembership> builder)
    {
        builder.ToTable("OrganizationMemberships");
        builder.HasKey(m => m.Id);
        builder.Property(m => m.Status).HasConversion<string>().HasMaxLength(20);
        builder.HasIndex(m => new { m.OrganizationId, m.UserId }).IsUnique().HasDatabaseName(UniqueIndexNames.Membership);
        builder.HasIndex(m => m.UserId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(m => m.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(m => m.UserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class InvitationConfiguration : IEntityTypeConfiguration<Invitation>
{
    public void Configure(EntityTypeBuilder<Invitation> builder)
    {
        builder.ToTable("Invitations");
        builder.HasKey(i => i.Id);
        builder.Property(i => i.Email).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(i => i.NormalizedEmail).HasMaxLength(User.EmailMaxLength).IsRequired();
        builder.Property(i => i.TokenHash).HasMaxLength(128).IsRequired();
        builder.HasIndex(i => i.TokenHash).IsUnique();
        builder.HasIndex(i => i.OrganizationId);
        builder.HasOne<Organization>().WithMany().HasForeignKey(i => i.OrganizationId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<User>().WithMany().HasForeignKey(i => i.InvitedByUserId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class UserSessionConfiguration : IEntityTypeConfiguration<UserSession>
{
    public void Configure(EntityTypeBuilder<UserSession> builder)
    {
        builder.ToTable("UserSessions");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.RevocationReason).HasMaxLength(100);
        builder.Property(s => s.IpAddress).HasMaxLength(64);
        builder.Property(s => s.UserAgent).HasMaxLength(512);
        builder.HasIndex(s => new { s.UserId, s.RevokedAt });
        builder.HasOne<User>().WithMany().HasForeignKey(s => s.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}
