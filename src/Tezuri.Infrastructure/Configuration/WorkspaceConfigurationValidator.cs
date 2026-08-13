using System.Text.RegularExpressions;

namespace Tezuri.Infrastructure.Configuration;

public sealed partial class WorkspaceConfigurationValidator
{
    private const long MaximumSupportedAssetBytes = 1_073_741_824;
    private const int MaximumProofTimeoutSeconds = 1_800;

    private static readonly HashSet<string> ShellExecutables = new(StringComparer.OrdinalIgnoreCase)
    {
        "bash",
        "cmd",
        "cmd.exe",
        "cscript",
        "cscript.exe",
        "dash",
        "fish",
        "powershell",
        "powershell.exe",
        "pwsh",
        "pwsh.exe",
        "sh",
        "wscript",
        "wscript.exe",
        "zsh"
    };

    public IReadOnlyList<WorkspaceConfigurationIssue> Validate(WorkspaceConfigurationV1 configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        var issues = new List<WorkspaceConfigurationIssue>();

        if (!StringComparer.Ordinal.Equals(configuration.Schema, WorkspaceConfigurationV1.SchemaName))
        {
            Add(issues, "schema", $"must be '{WorkspaceConfigurationV1.SchemaName}'.");
        }

        ValidateSite(configuration.Site, issues);
        ValidateArticles(configuration.Articles, issues);
        ValidateMedia(configuration.Media, issues);
        ValidateProof(configuration.Proof, issues);
        ValidateGit(configuration.Git, issues);
        return issues;
    }

    public void EnsureValid(WorkspaceConfigurationV1 configuration)
    {
        var issues = Validate(configuration);
        if (issues.Count > 0)
        {
            throw new WorkspaceConfigurationValidationException(issues);
        }
    }

    private static void ValidateSite(
        SiteConfiguration site,
        ICollection<WorkspaceConfigurationIssue> issues)
    {
        if (!Uri.TryCreate(site.Url, UriKind.Absolute, out var url) ||
            (url.Scheme != Uri.UriSchemeHttp && url.Scheme != Uri.UriSchemeHttps))
        {
            Add(issues, "site.url", "must be an absolute HTTP or HTTPS URL.");
            return;
        }

        if (!string.IsNullOrEmpty(url.UserInfo) ||
            !string.IsNullOrEmpty(url.Query) ||
            !string.IsNullOrEmpty(url.Fragment))
        {
            Add(issues, "site.url", "must not contain credentials, a query, or a fragment.");
        }
    }

    private static void ValidateArticles(
        ArticleLayoutConfiguration articles,
        ICollection<WorkspaceConfigurationIssue> issues)
    {
        ValidateRepositoryPath(articles.Root, "articles.root", allowCurrentDirectory: false, allowGlob: false, issues);

        if (articles.FileName != Path.GetFileName(articles.FileName) ||
            !articles.FileName.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "articles.fileName", "must be one Markdown file name without directories.");
        }

        if (!SafeSegmentPattern().IsMatch(articles.MediaDirectory))
        {
            Add(issues, "articles.mediaDirectory", "must be one portable directory name.");
        }

        ValidateRepositoryPath(
            articles.MetadataSchema,
            "articles.metadataSchema",
            allowCurrentDirectory: false,
            allowGlob: false,
            issues);
        if (!articles.MetadataSchema.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            Add(issues, "articles.metadataSchema", "must reference a JSON Schema file.");
        }

        if (articles.EditorHints is not null)
        {
            ValidateRepositoryPath(
                articles.EditorHints,
                "articles.editorHints",
                allowCurrentDirectory: false,
                allowGlob: false,
                issues);
            if (!articles.EditorHints.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                Add(issues, "articles.editorHints", "must reference a JSON editor-hints file.");
            }
        }
    }

    private static void ValidateMedia(
        MediaPolicyConfiguration media,
        ICollection<WorkspaceConfigurationIssue> issues)
    {
        if (!media.RequireOwnedAssets)
        {
            Add(issues, "media.requireOwnedAssets", "must be true for the v1 owned-media contract.");
        }

        if (media.MaximumAssetBytes <= 0 || media.MaximumAssetBytes > MaximumSupportedAssetBytes)
        {
            Add(
                issues,
                "media.maximumAssetBytes",
                $"must be between 1 and {MaximumSupportedAssetBytes} bytes.");
        }

        if (media.AllowedExtensions.Count == 0)
        {
            Add(issues, "media.allowedExtensions", "must contain at least one extension.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < media.AllowedExtensions.Count; index++)
        {
            var extension = media.AllowedExtensions[index];
            if (!ExtensionPattern().IsMatch(extension))
            {
                Add(
                    issues,
                    $"media.allowedExtensions[{index}]",
                    "must be a lowercase portable extension such as '.png'.");
            }

            if (!seen.Add(extension))
            {
                Add(issues, $"media.allowedExtensions[{index}]", "duplicates an earlier extension.");
            }
        }
    }

    private static void ValidateProof(
        ProofConfiguration proof,
        ICollection<WorkspaceConfigurationIssue> issues)
    {
        ValidateRepositoryPath(
            proof.WorkingDirectory,
            "proof.workingDirectory",
            allowCurrentDirectory: true,
            allowGlob: false,
            issues);

        if (proof.Commands.Count == 0)
        {
            Add(issues, "proof.commands", "must contain at least one structured command.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < proof.Commands.Count; index++)
        {
            var command = proof.Commands[index];
            var path = $"proof.commands[{index}]";
            if (!IdentifierPattern().IsMatch(command.Id))
            {
                Add(issues, $"{path}.id", "must use lowercase letters, digits, and single hyphens.");
            }
            else if (!ids.Add(command.Id))
            {
                Add(issues, $"{path}.id", "duplicates an earlier command id.");
            }

            if (!ExecutablePattern().IsMatch(command.Executable) ||
                command.Executable.Contains("..", StringComparison.Ordinal) ||
                Path.IsPathRooted(command.Executable))
            {
                Add(issues, $"{path}.executable", "must be one portable executable token.");
            }

            var executableName = Path.GetFileName(command.Executable.Replace('/', Path.DirectorySeparatorChar));
            if (ShellExecutables.Contains(executableName))
            {
                Add(
                    issues,
                    $"{path}.executable",
                    "must not be a shell interpreter; configure the target executable and arguments directly.");
            }

            for (var argumentIndex = 0; argumentIndex < command.Arguments.Count; argumentIndex++)
            {
                var argument = command.Arguments[argumentIndex];
                if (argument.Length > 4_096 || argument.Any(character => character is '\0' or '\r' or '\n'))
                {
                    Add(
                        issues,
                        $"{path}.arguments[{argumentIndex}]",
                        "must be one bounded process argument without control line breaks.");
                }
            }

            if (command.TimeoutSeconds is < 1 or > MaximumProofTimeoutSeconds)
            {
                Add(
                    issues,
                    $"{path}.timeoutSeconds",
                    $"must be between 1 and {MaximumProofTimeoutSeconds} seconds.");
            }

            if (command.OutputDirectory is not null)
            {
                ValidateRepositoryPath(
                    command.OutputDirectory,
                    $"{path}.outputDirectory",
                    allowCurrentDirectory: false,
                    allowGlob: false,
                    issues);
            }
        }
    }

    private static void ValidateGit(
        GitPublicationConfiguration git,
        ICollection<WorkspaceConfigurationIssue> issues)
    {
        if (git.AllowedPaths.Count == 0)
        {
            Add(issues, "git.allowedPaths", "must contain at least one publication path.");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 0; index < git.AllowedPaths.Count; index++)
        {
            var path = git.AllowedPaths[index];
            ValidateRepositoryPath(
                path,
                $"git.allowedPaths[{index}]",
                allowCurrentDirectory: false,
                allowGlob: true,
                issues);

            if (path.Split('/').Any(segment => segment.Equals(".git", StringComparison.OrdinalIgnoreCase)))
            {
                Add(issues, $"git.allowedPaths[{index}]", "must not grant access to Git internals.");
            }

            if (!seen.Add(path))
            {
                Add(issues, $"git.allowedPaths[{index}]", "duplicates an earlier allowed path.");
            }
        }
    }

    private static void ValidateRepositoryPath(
        string path,
        string issuePath,
        bool allowCurrentDirectory,
        bool allowGlob,
        ICollection<WorkspaceConfigurationIssue> issues)
    {
        if (allowCurrentDirectory && path == ".")
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.Contains("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            path.Any(char.IsControl))
        {
            Add(issues, issuePath, "must be a non-empty, repository-relative path using '/'.");
            return;
        }

        foreach (var segment in path.Split('/'))
        {
            if (segment is "" or "." or ".." ||
                (!allowGlob && segment.IndexOfAny(['*', '?', '[', ']']) >= 0) ||
                (allowGlob && !GlobSegmentPattern().IsMatch(segment)))
            {
                Add(issues, issuePath, "contains an unsafe or unsupported path segment.");
                return;
            }
        }
    }

    private static void Add(
        ICollection<WorkspaceConfigurationIssue> issues,
        string path,
        string message) => issues.Add(new WorkspaceConfigurationIssue(path, message));

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]*$")]
    private static partial Regex SafeSegmentPattern();

    [GeneratedRegex("^\\.[a-z0-9]+$")]
    private static partial Regex ExtensionPattern();

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+/-]*$")]
    private static partial Regex ExecutablePattern();

    [GeneratedRegex("^[A-Za-z0-9._*?\\[\\]-]+$")]
    private static partial Regex GlobSegmentPattern();
}
