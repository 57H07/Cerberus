using Cerberus.Application.Exceptions;
using Cerberus.Application.Interfaces.Repositories;
using Cerberus.Infrastructure.Data;
using Cerberus.Infrastructure.Data.Configurations;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Cerberus.Infrastructure.Repositories;

public class UnitOfWork(CerberusDbContext context) : IUnitOfWork
{
    private const int UniqueIndexViolation = 2601;
    private const int UniqueConstraintViolation = 2627;

    private static readonly Dictionary<string, (string Entity, string Field)> UniqueIndexes = new(StringComparer.OrdinalIgnoreCase)
    {
        [UniqueIndexNames.UserEmail] = ("user", "email address"),
        [UniqueIndexNames.UserName] = ("user", "login"),
        [UniqueIndexNames.OrganizationSlug] = ("organization", "slug"),
        [UniqueIndexNames.Membership] = ("membership", "user and organization"),
        [UniqueIndexNames.ApplicationAccess] = ("application access", "organization"),
        [UniqueIndexNames.ClientId] = ("client", "client ID"),
        [UniqueIndexNames.ScopeName] = ("scope", "name"),
        [UniqueIndexNames.Consent] = ("consent", "user, client and organization"),
        [UniqueIndexNames.RoleName] = ("role", "name"),
        [UniqueIndexNames.RoleAssignment] = ("role assignment", "user and role")
    };

    private IDbContextTransaction? _transaction;

    public async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: UniqueIndexViolation or UniqueConstraintViolation } sql)
        {
            var match = UniqueIndexes.FirstOrDefault(i => sql.Message.Contains(i.Key, StringComparison.OrdinalIgnoreCase));
            throw match.Key is null
                ? new DuplicateEntityException("record", "unique value")
                : new DuplicateEntityException(match.Value.Entity, match.Value.Field);
        }
    }

    public async Task BeginTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is not null)
        {
            throw new InvalidOperationException("A transaction is already open on this unit of work.");
        }

        _transaction = await context.Database.BeginTransactionAsync(cancellationToken);
    }

    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.CommitAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        if (_transaction is null)
        {
            return;
        }

        await _transaction.RollbackAsync(cancellationToken);
        await _transaction.DisposeAsync();
        _transaction = null;
    }
}
