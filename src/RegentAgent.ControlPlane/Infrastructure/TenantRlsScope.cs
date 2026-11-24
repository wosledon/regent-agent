using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using RegentAgent.ControlPlane.Data;

namespace RegentAgent.ControlPlane.Infrastructure;

public sealed class TenantRlsScope(RegentDbContext db)
{
    public async Task<T> ExecuteAsync<T>(Guid? tenantId, Guid? userId, Func<CancellationToken, Task<T>> action,
        CancellationToken cancellationToken)
    {
        if (!db.Database.IsNpgsql()) return await action(cancellationToken);
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await using var command = db.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = "SELECT set_config('app.tenant_id', COALESCE(@tenant_id, ''), true), set_config('app.user_id', COALESCE(@user_id, ''), true);";
        var tenant = command.CreateParameter();
        tenant.ParameterName = "tenant_id";
        tenant.Value = tenantId?.ToString() ?? (object)DBNull.Value;
        var user = command.CreateParameter();
        user.ParameterName = "user_id";
        user.Value = userId?.ToString() ?? (object)DBNull.Value;
        command.Parameters.Add(tenant);
        command.Parameters.Add(user);
        await command.ExecuteNonQueryAsync(cancellationToken);
        try
        {
            var result = await action(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
