using System.IO.Compression;

namespace Move.IntegrationTests;

public sealed class TestWorkspace : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), "move-tests", Guid.NewGuid().ToString("N"));
    public string SourceDir => Path.Combine(Root, "source");
    public string TargetDir => Path.Combine(Root, "target");

    public TestWorkspace()
    {
        Directory.CreateDirectory(SourceDir);
        Directory.CreateDirectory(TargetDir);
    }

    public string AddSourceText(string relativePath, string firstLine, params string[] remainingLines) =>
        AddText(SourceDir, relativePath, firstLine, remainingLines);

    public string AddTargetText(string relativePath, string firstLine, params string[] remainingLines) =>
        AddText(TargetDir, relativePath, firstLine, remainingLines);

    public string AddSourceGzip(string relativePath, string firstLine, params string[] remainingLines) =>
        AddGzip(SourceDir, relativePath, firstLine, remainingLines);

    public string AddTargetGzip(string relativePath, string firstLine, params string[] remainingLines) =>
        AddGzip(TargetDir, relativePath, firstLine, remainingLines);

    public void PopulateMonthTree(int filesPerLeaf)
    {
        foreach (var leaf in new[] { "2026/08/01", "2026/08/02", "2026/09/01", "2026/09/02" })
        {
            for (var i = 0; i < filesPerLeaf; i++)
            {
                var stem = $"file{i:000}";
                if (i % 2 == 0)
                    AddSourceText(Path.Combine(leaf, $"{stem}.txt"), $"HDR|{i}", "payload");
                else
                    AddSourceGzip(Path.Combine(leaf, $"{stem}.gz"), $"HDR|{i}", "payload");
            }
        }
    }

    public static void SetLastWriteTimeUtc(string path, DateTime value) => File.SetLastWriteTimeUtc(path, value);

    public static IReadOnlyList<string> RelativeFiles(string root) =>
        Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
            .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();

    public void Dispose()
    {
        if (Directory.Exists(Root))
            Directory.Delete(Root, recursive: true);
    }

    private static string AddText(string root, string relativePath, string firstLine, params string[] remainingLines)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        var lines = new[] { firstLine }.Concat(remainingLines);
        File.WriteAllText(path, string.Join(Environment.NewLine, lines) + Environment.NewLine);
        return path;
    }

    private static string AddGzip(string root, string relativePath, string firstLine, params string[] remainingLines)
    {
        var path = Path.Combine(root, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        using var fs = File.Create(path);
        using var gzip = new GZipStream(fs, CompressionLevel.SmallestSize);
        using var writer = new StreamWriter(gzip);

        writer.WriteLine(firstLine);
        foreach (var line in remainingLines)
            writer.WriteLine(line);

        return path;
    }
}
