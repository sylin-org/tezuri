using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Tezuri.Workspace;

namespace Tezuri.Publishing;

public sealed class GitCommandRunner(WorkspacePathGuard workspace)
{
    public const int MaximumCapturedCharacters = 65_536;

    private const string TruncationMarker = "\n[output truncated]";

    private static readonly Regex SensitiveAssignmentPattern = new(
        @"\b(token|password|secret|api[_-]?key|authorization)\b\s*[:=]\s*(?:""[^""]*""|'[^']*'|[^\s]+)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    private static readonly Regex UrlAuthorityPattern = new(
        @"(?<=://)[^/\s@]+@",
        RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(250));

    internal async Task<GitCommandResult> RunAsync(
        IReadOnlyList<string> arguments,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            WorkingDirectory = workspace.Root,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
            CreateNoWindow = true
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        RemoveSensitiveEnvironment(startInfo);
        startInfo.Environment["GIT_TERMINAL_PROMPT"] = "0";
        startInfo.Environment["GCM_INTERACTIVE"] = "Never";
        startInfo.Environment["SSH_ASKPASS_REQUIRE"] = "never";
        startInfo.Environment["GIT_PAGER"] = "cat";
        startInfo.Environment["PAGER"] = "cat";
        startInfo.Environment["LC_ALL"] = "C";
        startInfo.Environment["LANG"] = "C";

        using var process = new Process { StartInfo = startInfo };
        try
        {
            if (!process.Start())
            {
                throw new GitPublicationException(
                    GitPublicationFailure.CommandFailed,
                    "Git did not start.");
            }
        }
        catch (GitPublicationException)
        {
            throw;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            throw new GitPublicationException(
                GitPublicationFailure.CommandFailed,
                "Git did not start: " + Redact(exception.Message));
        }

        process.StandardInput.Close();
        var stdoutTask = CaptureAsync(process.StandardOutput);
        var stderrTask = CaptureAsync(process.StandardError);
        using var timeoutSource = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            timeoutSource.Token);
        try
        {
            await process.WaitForExitAsync(linked.Token);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            var timedOutError = await stderrTask;
            await stdoutTask;
            var detail = Redact(timedOutError.Text, timedOutError.Truncated);
            throw new GitPublicationException(
                GitPublicationFailure.CommandFailed,
                detail.Length == 0
                    ? "Git exceeded its operation timeout."
                    : "Git exceeded its operation timeout: " + detail);
        }
        catch (OperationCanceledException)
        {
            TryKillProcessTree(process);
            await WaitForExitAfterKillAsync(process);
            await Task.WhenAll(stdoutTask, stderrTask);
            throw;
        }

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        return new GitCommandResult(
            process.ExitCode,
            Bound(stdout.Text, stdout.Truncated),
            Bound(stderr.Text, stderr.Truncated));
    }

    private static void RemoveSensitiveEnvironment(ProcessStartInfo startInfo)
    {
        foreach (var name in startInfo.Environment.Keys.ToArray())
        {
            if (IsSensitiveEnvironmentName(name))
            {
                startInfo.Environment.Remove(name);
            }
        }

        // Ambient agents/helpers are the supported credential delegation boundary.
        // Explicit askpass programs are disabled because Tezuri does not provide secrets.
        startInfo.Environment.Remove("GIT_ASKPASS");
        startInfo.Environment.Remove("SSH_ASKPASS");
    }

    private static bool IsSensitiveEnvironmentName(string name)
    {
        var normalized = name.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .ToUpperInvariant();
        return normalized.Contains("TOKEN", StringComparison.Ordinal) ||
               normalized.Contains("PASSWORD", StringComparison.Ordinal) ||
               normalized.Contains("SECRET", StringComparison.Ordinal) ||
               normalized.Contains("CREDENTIAL", StringComparison.Ordinal) ||
               normalized.Contains("APIKEY", StringComparison.Ordinal) ||
               normalized.Contains("ACCESSKEY", StringComparison.Ordinal) ||
               normalized.Contains("PRIVATEKEY", StringComparison.Ordinal) ||
               normalized.Contains("AUTHORIZATION", StringComparison.Ordinal);
    }

    private static async Task<CapturedOutput> CaptureAsync(StreamReader reader)
    {
        var buffer = new char[4_096];
        var captured = new StringBuilder(MaximumCapturedCharacters);
        var truncated = false;
        int read;
        while ((read = await reader.ReadAsync(buffer.AsMemory())) > 0)
        {
            var remaining = MaximumCapturedCharacters - captured.Length;
            if (remaining > 0)
            {
                captured.Append(buffer.AsSpan(0, Math.Min(read, remaining)));
            }

            truncated |= read > remaining;
        }

        return new CapturedOutput(captured.ToString(), truncated);
    }

    internal static string RedactOutput(string source)
    {
        var result = source;
        try
        {
            result = UrlAuthorityPattern.Replace(result, "[REDACTED]@");
            result = SensitiveAssignmentPattern.Replace(result, "$1=[REDACTED]");
        }
        catch (RegexMatchTimeoutException)
        {
            return "[output withheld because redaction exceeded its processing limit]";
        }

        return Bound(result, truncated: false);
    }

    private static string Redact(string source, bool truncated = false) =>
        Bound(RedactOutput(source), truncated);

    private static string Bound(string source, bool truncated)
    {
        var result = source;

        if (result.Length > MaximumCapturedCharacters)
        {
            result = result[..MaximumCapturedCharacters];
            truncated = true;
        }

        if (!truncated)
        {
            return result;
        }

        var contentLength = MaximumCapturedCharacters - TruncationMarker.Length;
        if (result.Length > contentLength)
        {
            result = result[..contentLength];
        }

        return result + TruncationMarker;
    }

    private static void TryKillProcessTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and kill request.
        }
    }

    private static async Task WaitForExitAfterKillAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
        catch (InvalidOperationException)
        {
            // A completed process can race with the final wait.
        }
    }

    private sealed record CapturedOutput(string Text, bool Truncated);
}

internal sealed record GitCommandResult(int ExitCode, string StandardOutput, string StandardError);
