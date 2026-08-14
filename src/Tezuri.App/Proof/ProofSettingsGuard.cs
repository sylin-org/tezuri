using System.Text.RegularExpressions;
using Tezuri.Workspace;

namespace Tezuri.Proof;

/// <summary>
/// Checks proof settings before anything is copied or launched.
///
/// The load-bearing rule is the shell-interpreter refusal. Tezuri never runs a proof command through
/// a shell — executable and argument list stay separate, so no configured string can become shell
/// syntax. Naming <c>sh</c> or <c>pwsh</c> as the executable would hand that separation straight
/// back, because everything after <c>-c</c> is then a script. It is refused rather than discouraged.
///
/// The remaining checks keep a misconfigured workspace from doing work before it fails: an unsafe
/// working directory is caught here rather than after a full isolated copy has been made.
/// </summary>
internal static partial class ProofSettingsGuard
{
    private const int MaximumTimeoutSeconds = 1_800;
    private const int MaximumArgumentCharacters = 4_096;

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

    public static void EnsureRunnable(ProofSettings proof)
    {
        ArgumentNullException.ThrowIfNull(proof);
        EnsureRepositoryPath(proof.WorkingDirectory, "proof working directory", allowCurrentDirectory: true);

        if (proof.Commands.Count == 0)
        {
            throw new SiteProofException("Proof needs at least one command to run.");
        }

        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var command in proof.Commands)
        {
            if (!IdentifierPattern().IsMatch(command.Id) || !ids.Add(command.Id))
            {
                throw new SiteProofException(
                    $"Proof command id '{command.Id}' must be a unique lowercase hyphenated name.");
            }

            EnsureExecutable(command.Executable);

            foreach (var argument in command.Arguments)
            {
                if (argument.Length > MaximumArgumentCharacters ||
                    argument.Any(character => character is '\0' or '\r' or '\n'))
                {
                    throw new SiteProofException(
                        $"Proof command '{command.Id}' has an argument that is oversized or contains a control line break.");
                }
            }

            if (command.TimeoutSeconds is < 1 or > MaximumTimeoutSeconds)
            {
                throw new SiteProofException(
                    $"Proof command '{command.Id}' must time out between 1 and {MaximumTimeoutSeconds} seconds.");
            }

            if (command.OutputDirectory is not null)
            {
                EnsureRepositoryPath(
                    command.OutputDirectory,
                    $"output directory for proof command '{command.Id}'",
                    allowCurrentDirectory: false);
            }
        }
    }

    private static void EnsureExecutable(string executable)
    {
        if (!ExecutablePattern().IsMatch(executable) ||
            executable.Contains("..", StringComparison.Ordinal) ||
            Path.IsPathRooted(executable))
        {
            throw new SiteProofException(
                $"Proof executable '{executable}' must be one portable executable token.");
        }

        var name = Path.GetFileName(executable.Replace('/', Path.DirectorySeparatorChar));
        if (ShellExecutables.Contains(name))
        {
            throw new SiteProofException(
                $"Proof executable '{executable}' is a shell interpreter. " +
                "Configure the target executable and its arguments directly.");
        }
    }

    private static void EnsureRepositoryPath(
        string path,
        string description,
        bool allowCurrentDirectory)
    {
        if (allowCurrentDirectory && StringComparer.Ordinal.Equals(path, "."))
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(path) ||
            path.StartsWith('/') ||
            path.Contains('\\', StringComparison.Ordinal) ||
            path.Contains("//", StringComparison.Ordinal) ||
            Path.IsPathRooted(path) ||
            path.Any(char.IsControl) ||
            path.Split('/').Any(segment => segment is "" or "." or ".."))
        {
            throw new SiteProofException(
                $"The {description} must be a repository-relative path using '/'.");
        }
    }

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$")]
    private static partial Regex IdentifierPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._+/-]*$")]
    private static partial Regex ExecutablePattern();
}
