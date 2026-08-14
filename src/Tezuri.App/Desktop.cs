using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Photino.NET;

namespace Tezuri;

/// <summary>
/// Tezuri as a desktop application: a native window around the system webview, pointed at a server
/// that only this machine can reach.
///
/// The window is the whole reason there is no container any more. A person who writes should be
/// able to double-click something and be writing; installing a runtime, mounting a volume, and
/// finding a URL in a log was a tax on that, paid every single time.
/// </summary>
public static class DesktopShell
{
    private const string RecentFileName = "recent-repositories.json";
    private const int MaximumRemembered = 10;

    /// <summary>
    /// True unless the caller asked for a plain server. Tests and headless environments pass
    /// <c>--server</c> or set <c>TEZURI_SHELL=server</c>; everything else gets a window.
    /// </summary>
    public static bool IsWanted(string[] args, IConfiguration configuration) =>
        !args.Contains("--server", StringComparer.Ordinal) &&
        !StringComparer.OrdinalIgnoreCase.Equals(configuration["TEZURI_SHELL"], "server");

    /// <summary>The repository named on the command line, if one was.</summary>
    public static string? RequestedWorkspace(string[] args) =>
        args.FirstOrDefault(argument => !argument.StartsWith('-'));

    public static async Task<int> RunAsync(WebApplication app, string[] args)
    {
        var workspace = app.Services.GetRequiredService<SelectedWorkspace>();
        var nonce = app.Services.GetRequiredService<BootstrapNonce>();

        await app.StartAsync();
        try
        {
            var origin = ResolveOrigin(app);
            var chosen = RequestedWorkspace(args)
                ?? app.Configuration["TEZURI_WORKSPACE"]
                ?? MostRecent();

            var window = new PhotinoWindow()
                .SetTitle(chosen is null ? "Tezuri" : $"Tezuri — {Path.GetFileName(chosen)}")
                .SetUseOsDefaultSize(false)
                .SetSize(1440, 900)
                .Center();

            // Asking for the folder needs a live native window, so the question is asked from the
            // window itself and the answer is applied before it is ever pointed at the server.
            window.WindowCreating += (_, _) => { };
            window.WindowCreated += (_, _) =>
            {
                var root = chosen ?? Ask(window);
                if (root is null)
                {
                    window.Close();
                    return;
                }

                workspace.Choose(root);
                Remember(root);
                window.SetTitle($"Tezuri — {Path.GetFileName(root)}");
                window.Load(new Uri($"{origin}/?nonce={Uri.EscapeDataString(nonce.Value)}"));
            };

            Open(window);
            return 0;
        }
        finally
        {
            await app.StopAsync();
        }
    }

    /// <summary>Opens another repository in its own Tezuri, because one session edits one repository.</summary>
    public static void OpenAnother(string root)
    {
        var executable = Environment.ProcessPath;
        if (executable is null)
        {
            return;
        }

        Process.Start(new ProcessStartInfo(executable, [root]) { UseShellExecute = false });
    }

    private static string? Ask(PhotinoWindow window)
    {
        var chosen = window.ShowOpenFolder(
            "Choose the repository you write in",
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            false);
        return chosen is { Length: > 0 } ? chosen[0] : null;
    }

    /// <summary>
    /// Photino needs a single-threaded apartment on Windows and blocks until the window closes.
    /// </summary>
    private static void Open(PhotinoWindow window)
    {
        if (!OperatingSystem.IsWindows())
        {
            window.WaitForClose();
            return;
        }

        var thread = new Thread(window.WaitForClose, 8 * 1024 * 1024);
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
    }

    /// <summary>
    /// The port is whatever the operating system handed out. Binding a fixed one would collide the
    /// moment a second repository is opened, which is an ordinary thing to do.
    /// </summary>
    private static string ResolveOrigin(WebApplication app)
    {
        var addresses = app.Services
            .GetRequiredService<IServer>()
            .Features.Get<IServerAddressesFeature>()?
            .Addresses;
        var address = addresses?.FirstOrDefault()
            ?? throw new InvalidOperationException("The local server did not report an address.");
        return address.Replace("[::]", "127.0.0.1", StringComparison.Ordinal)
            .Replace("0.0.0.0", "127.0.0.1", StringComparison.Ordinal)
            .TrimEnd('/');
    }

    public static IReadOnlyList<string> Recent()
    {
        try
        {
            var path = RecentPath();
            if (!File.Exists(path))
            {
                return [];
            }

            var stored = JsonSerializer.Deserialize<string[]>(File.ReadAllText(path)) ?? [];
            return stored.Where(Directory.Exists).Take(MaximumRemembered).ToArray();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static string? MostRecent() => Recent().FirstOrDefault();

    private static void Remember(string root)
    {
        try
        {
            var ordered = new[] { root }
                .Concat(Recent().Where(existing => !StringComparer.OrdinalIgnoreCase.Equals(existing, root)))
                .Take(MaximumRemembered)
                .ToArray();
            var path = RecentPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(ordered));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            // Forgetting which repositories you opened is a small loss. Failing to start is not.
        }
    }

    private static string RecentPath() => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Tezuri",
        RecentFileName);
}
