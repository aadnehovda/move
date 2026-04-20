using BenchmarkDotNet.Attributes;
namespace Move.PerfTests;

[MemoryDiagnoser]
[ShortRunJob]
public class ExistingTargetStrategyBenchmarks
{
    private const int ErrorAlreadyExists = unchecked((int)0x800700B7);
    private const int ErrorFileExists = unchecked((int)0x80070050);

    private string _templateRoot = null!;
    private string _workingRoot = null!;
    private string _sourceRoot = null!;
    private string _targetRoot = null!;
    private string[] _relativePaths = null!;

    [Params(480)]
    public int FileCount { get; set; }

    [Params(PerfScenario.MissingTarget, PerfScenario.ExistingMatch, PerfScenario.ExistingConflict)]
    public PerfScenario Scenario { get; set; }

    [GlobalSetup]
    public void GlobalSetup()
    {
        _templateRoot = Path.Combine(Path.GetTempPath(), "move-perf", Guid.NewGuid().ToString("N"), "template");
        _workingRoot = Path.Combine(Path.GetTempPath(), "move-perf", Guid.NewGuid().ToString("N"), "work");

        Directory.CreateDirectory(_templateRoot);

        var templateSource = Path.Combine(_templateRoot, "source");
        var templateTarget = Path.Combine(_templateRoot, "target");
        Directory.CreateDirectory(templateSource);
        Directory.CreateDirectory(templateTarget);

        _relativePaths = CreateFixture(templateSource, templateTarget, FileCount, Scenario);
    }

    [IterationSetup]
    public void IterationSetup()
    {
        if (Directory.Exists(_workingRoot))
            Directory.Delete(_workingRoot, recursive: true);

        Directory.CreateDirectory(_workingRoot);

        _sourceRoot = Path.Combine(_workingRoot, "source");
        _targetRoot = Path.Combine(_workingRoot, "target");

        CopyDirectory(Path.Combine(_templateRoot, "source"), _sourceRoot);
        CopyDirectory(Path.Combine(_templateRoot, "target"), _targetRoot);
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        if (Directory.Exists(_workingRoot))
            Directory.Delete(_workingRoot, recursive: true);
    }

    [GlobalCleanup]
    public void GlobalCleanup()
    {
        var templateParent = Directory.GetParent(_templateRoot)?.Parent?.FullName;
        if (templateParent is not null && Directory.Exists(templateParent))
            Directory.Delete(templateParent, recursive: true);

        var workParent = Directory.GetParent(_workingRoot)?.Parent?.FullName;
        if (workParent is not null && Directory.Exists(workParent))
            Directory.Delete(workParent, recursive: true);
    }

    [Benchmark(Baseline = true)]
    public int PreCheckExistsThenAct()
    {
        var processed = 0;

        foreach (var relativePath in _relativePaths)
        {
            var source = new FileInfo(Path.Combine(_sourceRoot, relativePath));
            var target = new FileInfo(Path.Combine(_targetRoot, relativePath));

            if (target.Exists)
            {
                HandleExisting(source, target);
                processed++;
                continue;
            }

            try
            {
                target.Directory?.Create();
                File.Copy(source.FullName, target.FullName, overwrite: false);
            }
            catch (IOException ex) when (LooksLikeAlreadyExists(ex, target))
            {
                HandleExisting(source, target);
            }

            processed++;
        }

        return processed;
    }

    [Benchmark]
    public int TryActionAndCatchAlreadyExists()
    {
        var processed = 0;

        foreach (var relativePath in _relativePaths)
        {
            var source = new FileInfo(Path.Combine(_sourceRoot, relativePath));
            var target = new FileInfo(Path.Combine(_targetRoot, relativePath));

            try
            {
                target.Directory?.Create();
                File.Copy(source.FullName, target.FullName, overwrite: false);
            }
            catch (IOException ex) when (LooksLikeAlreadyExists(ex, target))
            {
                HandleExisting(source, target);
            }

            processed++;
        }

        return processed;
    }

    private static void HandleExisting(FileInfo source, FileInfo target)
    {
        if (source.Length == target.Length && source.LastWriteTimeUtc == target.LastWriteTimeUtc)
            return;
    }

    private static bool LooksLikeAlreadyExists(IOException ex, FileInfo target) =>
        ex.HResult is ErrorAlreadyExists or ErrorFileExists || target.Exists;

    private static string[] CreateFixture(string sourceRoot, string targetRoot, int fileCount, PerfScenario scenario)
    {
        var relativePaths = new List<string>(fileCount);
        var leaves = new[]
        {
            "2026/08/01",
            "2026/08/02",
            "2026/09/01",
            "2026/09/02",
        };

        for (var i = 0; i < fileCount; i++)
        {
            var leaf = leaves[i % leaves.Length];
            var relativePath = Path.Combine(leaf, $"file{i:0000}.txt");
            relativePaths.Add(relativePath);

            var sourcePath = WriteFile(sourceRoot, relativePath, $"HDR|{i}\npayload-{i}\n");
            var timestamp = new DateTime(2026, 8, 1, 12, 0, 0, DateTimeKind.Utc).AddSeconds(i);
            File.SetLastWriteTimeUtc(sourcePath, timestamp);

            if (scenario == PerfScenario.MissingTarget)
                continue;

            var targetContent = scenario == PerfScenario.ExistingMatch
                ? $"HDR|{i}\npayload-{i}\n"
                : $"HDR|{i}\nconflict-payload-{i}-extra\n";

            var targetPath = WriteFile(targetRoot, relativePath, targetContent);

            if (scenario == PerfScenario.ExistingMatch)
                File.SetLastWriteTimeUtc(targetPath, timestamp);
            else
                File.SetLastWriteTimeUtc(targetPath, timestamp.AddMinutes(1));
        }

        return relativePaths.ToArray();
    }

    private static string WriteFile(string root, string relativePath, string contents)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, contents);
        return path;
    }

    private static void CopyDirectory(string sourceDir, string destinationDir)
    {
        Directory.CreateDirectory(destinationDir);

        foreach (var directory in Directory.EnumerateDirectories(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, directory);
            Directory.CreateDirectory(Path.Combine(destinationDir, relative));
        }

        foreach (var file in Directory.EnumerateFiles(sourceDir, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(sourceDir, file);
            var destination = Path.Combine(destinationDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            File.Copy(file, destination, overwrite: true);
            File.SetLastWriteTimeUtc(destination, File.GetLastWriteTimeUtc(file));
        }
    }
}

public enum PerfScenario
{
    MissingTarget,
    ExistingMatch,
    ExistingConflict,
}
