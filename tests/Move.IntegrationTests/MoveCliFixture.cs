using System.Diagnostics;

namespace Move.IntegrationTests;

public sealed class MoveCliFixture : IAsyncLifetime
{
    public string RepoRoot { get; private set; } = null!;
    public string AppDllPath { get; private set; } = null!;

    public async Task InitializeAsync()
    {
        RepoRoot = FindRepoRoot();

        var build = await RunProcessAsync(
            "dotnet",
            ["build", "src/move/move.csproj", "-c", "Debug", "--nologo"],
            RepoRoot);

        Assert.True(build.ExitCode == 0, $"dotnet build failed:{Environment.NewLine}{build.StdErr}");

        AppDllPath = Path.Combine(RepoRoot, "src", "move", "bin", "Debug", "net10.0", "move.dll");
        Assert.True(File.Exists(AppDllPath), $"Missing app binary: {AppDllPath}");
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public Task<CommandResult> RunAsync(params string[] args) => RunProcessAsync("dotnet", [AppDllPath, .. args], RepoRoot);

    private static async Task<CommandResult> RunProcessAsync(string fileName, IEnumerable<string> args, string workingDirectory)
    {
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = fileName,
                WorkingDirectory = workingDirectory,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            }
        };

        foreach (var arg in args)
            process.StartInfo.ArgumentList.Add(arg);

        process.Start();

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var timeout = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await process.WaitForExitAsync(timeout.Token);

        return new CommandResult(process.ExitCode, await stdout, await stderr);
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);

        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir.FullName, "move.slnx")))
                return dir.FullName;

            dir = dir.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate repository root.");
    }
}

public sealed record CommandResult(int ExitCode, string StdOut, string StdErr);
