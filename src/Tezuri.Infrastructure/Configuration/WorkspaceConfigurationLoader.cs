using Tezuri.Infrastructure.Workspace;

namespace Tezuri.Infrastructure.Configuration;

public sealed class WorkspaceConfigurationLoader(
    WorkspaceConfigurationParser parser,
    WorkspaceConfigurationValidator validator)
{
    private const int MaximumConfigurationBytes = 262_144;

    public async Task<WorkspaceConfigurationV1> LoadAsync(
        WorkspacePathGuard workspace,
        string relativePath = "tezuri.yaml",
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workspace);
        var path = workspace.Resolve(relativePath);
        var info = new FileInfo(path);
        if (!info.Exists)
        {
            throw new WorkspaceConfigurationException(
                $"Workspace configuration '{workspace.Relative(path)}' does not exist.");
        }

        if (info.Length > MaximumConfigurationBytes)
        {
            throw new WorkspaceConfigurationException(
                $"Workspace configuration '{workspace.Relative(path)}' exceeds {MaximumConfigurationBytes} bytes.");
        }

        var source = await File.ReadAllTextAsync(path, cancellationToken);
        var configuration = parser.Parse(source, workspace.Relative(path));
        validator.EnsureValid(configuration);
        return configuration;
    }
}

