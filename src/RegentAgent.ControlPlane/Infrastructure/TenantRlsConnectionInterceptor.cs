using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace RegentAgent.ControlPlane.Infrastructure;

public sealed class TenantRlsConnectionInterceptor(IHttpContextAccessor accessor) : DbConnectionInterceptor
{
    private string? TenantId => accessor.HttpContext?.User.FindFirstValue("tenant_id")
        ?? accessor.HttpContext?.Items["Regent.TenantId"] as string;

    public override async Task ConnectionOpenedAsync(DbConnection connection,
        ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
        if (!Guid.TryParse(TenantId, out var tenantId)) return;
        await using var command = connection.CreateCommand();
        command.CommandText = "SELECT set_config('app.tenant_id', @tenant_id, false)";
        var parameter = command.CreateParameter();
        parameter.ParameterName = "tenant_id";
        parameter.Value = tenantId.ToString();
        command.Parameters.Add(parameter);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public override async ValueTask<InterceptionResult> ConnectionClosingAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result)
    {
        if (connection.State == System.Data.ConnectionState.Open)
        {
            await using var command = connection.CreateCommand();
            command.CommandText = "RESET app.tenant_id; RESET app.user_id";
            await command.ExecuteNonQueryAsync();
        }
        await base.ConnectionClosingAsync(connection, eventData, result);
        return result;
    }
}
