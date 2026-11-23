using System.Security.Cryptography;
using System.Text;

namespace RegentAgent.Sandbox;

public sealed record SandboxRequest(Guid TenantId, Guid TaskId, string FileName, string Content);
public sealed record SandboxResult(string WorkspacePath, string FilePath, string Sha256);

public interface ISandboxExecutor
{
    Task<SandboxResult> WriteAsync(SandboxRequest request, CancellationToken cancellationToken);
}
