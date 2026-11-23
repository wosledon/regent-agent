using System.Security.Cryptography;
using System.Text;

namespace RegentAgent.Sandbox;

public sealed class LocalDirectorySandbox : ISandboxExecutor
{
    private readonly string root;

    public LocalDirectorySandbox(string root)
    {
        this.root = Path.GetFullPath(root);
        Directory.CreateDirectory(root);
    }

    public async Task<SandboxResult> WriteAsync(SandboxRequest request, CancellationToken cancellationToken)
    {
        if (Path.GetFileName(request.FileName) != request.FileName || request.FileName.Contains(".."))
        {
            throw new ArgumentException("Invalid sandbox file name.", nameof(request));
        }
        var workspace = Path.Combine(root, request.TenantId.ToString(), request.TaskId.ToString());
        Directory.CreateDirectory(workspace);
        var path = Path.Combine(workspace, request.FileName);
        var bytes = Encoding.UTF8.GetBytes(request.Content);
        await File.WriteAllBytesAsync(path, bytes, cancellationToken);
        return new SandboxResult(workspace, path, Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant());
    }
}
