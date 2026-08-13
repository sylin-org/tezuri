namespace Tezuri.Infrastructure.Configuration;

public class WorkspaceConfigurationException(string message) : Exception(message);

public sealed class WorkspaceConfigurationValidationException(
    IReadOnlyList<WorkspaceConfigurationIssue> issues)
    : WorkspaceConfigurationException(FormatMessage(issues))
{
    public IReadOnlyList<WorkspaceConfigurationIssue> Issues { get; } = issues;

    private static string FormatMessage(IReadOnlyList<WorkspaceConfigurationIssue> issues) =>
        "Invalid Tezuri workspace configuration:" + Environment.NewLine +
        string.Join(Environment.NewLine, issues.Select(issue => $"- {issue.Path}: {issue.Message}"));
}

public sealed record WorkspaceConfigurationIssue(string Path, string Message);
