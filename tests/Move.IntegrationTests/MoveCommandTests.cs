namespace Move.IntegrationTests;

public sealed class MoveCommandTests : IClassFixture<MoveCliFixture>
{
    private readonly MoveCliFixture _cli;

    public MoveCommandTests(MoveCliFixture cli)
    {
        _cli = cli;
    }

    [Fact]
    public async Task Move_transfers_nested_tree_and_deletes_source_files()
    {
        using var workspace = new TestWorkspace();
        workspace.PopulateMonthTree(filesPerLeaf: 12);

        var expected = TestWorkspace.RelativeFiles(workspace.SourceDir);

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir);

        Assert.Equal(0, result.ExitCode);
        Assert.Empty(TestWorkspace.RelativeFiles(workspace.SourceDir));
        Assert.Equal(expected, TestWorkspace.RelativeFiles(workspace.TargetDir));
    }

    [Fact]
    public async Task Keep_source_copies_files_without_deleting_source()
    {
        using var workspace = new TestWorkspace();
        workspace.AddSourceText("2026/08/02/file001.txt", "HDR|1", "alpha");
        workspace.AddSourceGzip("2026/08/02/file002.gz", "HDR|2", "beta");

        var expected = TestWorkspace.RelativeFiles(workspace.SourceDir);

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir, "--keep-source");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(expected, TestWorkspace.RelativeFiles(workspace.SourceDir));
        Assert.Equal(expected, TestWorkspace.RelativeFiles(workspace.TargetDir));
    }

    [Fact]
    public async Task Search_filters_plain_text_and_gzip_by_first_line()
    {
        using var workspace = new TestWorkspace();
        workspace.AddSourceText("2026/08/02/file001.txt", "HDR|1", "alpha");
        workspace.AddSourceText("2026/08/02/file002.txt", "SKIP|2", "beta");
        workspace.AddSourceGzip("2026/08/02/file003.gz", "HDR|3", "gamma");
        workspace.AddSourceGzip("2026/08/02/file004.gz", "SKIP|4", "delta");

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir, "--keep-source", "--search", "HDR|");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(new[]
        {
            "2026/08/02/file001.txt",
            "2026/08/02/file003.gz",
        }, TestWorkspace.RelativeFiles(workspace.TargetDir));
    }

    [Fact]
    public async Task Move_prunes_source_when_existing_target_matches_metadata()
    {
        using var workspace = new TestWorkspace();
        const string relativePath = "2026/08/02/file001.txt";
        var source = workspace.AddSourceText(relativePath, "HDR|1", "same");
        var target = workspace.AddTargetText(relativePath, "HDR|1", "same");
        var timestamp = new DateTime(2026, 8, 2, 12, 0, 0, DateTimeKind.Utc);

        TestWorkspace.SetLastWriteTimeUtc(source, timestamp);
        TestWorkspace.SetLastWriteTimeUtc(target, timestamp);

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir);

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(target));
    }

    [Fact]
    public async Task Conflicting_existing_target_is_left_alone_without_overwrite()
    {
        using var workspace = new TestWorkspace();
        const string relativePath = "2026/08/02/file001.txt";
        var source = workspace.AddSourceText(relativePath, "HDR|1", "source-value");
        var target = workspace.AddTargetText(relativePath, "HDR|1", "target-value-extra");

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir, "--verbose");

        Assert.Equal(0, result.ExitCode);
        Assert.True(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Contains("op: \"conflict\"", result.StdOut, StringComparison.Ordinal);
        Assert.Contains("target-value", File.ReadAllText(target), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Overwrite_replaces_conflicting_target()
    {
        using var workspace = new TestWorkspace();
        const string relativePath = "2026/08/02/file001.txt";
        var source = workspace.AddSourceText(relativePath, "HDR|1", "source-value");
        var target = workspace.AddTargetText(relativePath, "HDR|1", "target-value");

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir, "--overwrite");

        Assert.Equal(0, result.ExitCode);
        Assert.False(File.Exists(source));
        Assert.True(File.Exists(target));
        Assert.Contains("source-value", File.ReadAllText(target), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Dry_run_leaves_source_and_target_unchanged()
    {
        using var workspace = new TestWorkspace();
        workspace.AddSourceText("2026/08/02/file001.txt", "HDR|1", "alpha");

        var beforeSource = TestWorkspace.RelativeFiles(workspace.SourceDir);
        var beforeTarget = TestWorkspace.RelativeFiles(workspace.TargetDir);

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir, "--dry-run", "--keep-source");

        Assert.Equal(0, result.ExitCode);
        Assert.Equal(beforeSource, TestWorkspace.RelativeFiles(workspace.SourceDir));
        Assert.Equal(beforeTarget, TestWorkspace.RelativeFiles(workspace.TargetDir));
    }

    [Fact]
    public async Task Move_does_not_prune_empty_source_directories_by_default()
    {
        using var workspace = new TestWorkspace();
        workspace.AddSourceText("2026/08/02/file001.txt", "HDR|1", "alpha");

        var leafDirectory = Path.Combine(workspace.SourceDir, "2026", "08", "02");

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir);

        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(leafDirectory));
    }

    [Fact]
    public async Task Move_prunes_empty_source_directories_when_flag_is_set()
    {
        using var workspace = new TestWorkspace();
        workspace.AddSourceText("2026/08/02/file001.txt", "HDR|1", "alpha");

        var yearDirectory = Path.Combine(workspace.SourceDir, "2026");
        var leafDirectory = Path.Combine(workspace.SourceDir, "2026", "08", "02");

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir, "--prune-empty-dirs");

        Assert.Equal(0, result.ExitCode);
        Assert.False(Directory.Exists(leafDirectory));
        Assert.False(Directory.Exists(yearDirectory));
        Assert.True(Directory.Exists(workspace.SourceDir));
    }

    [Fact]
    public async Task Prune_empty_dirs_does_not_delete_past_source_root()
    {
        using var workspace = new TestWorkspace();
        workspace.AddSourceText("2026/08/02/file001.txt", "HDR|1", "alpha");

        var siblingDirectory = Path.Combine(workspace.Root, "source-sibling");
        Directory.CreateDirectory(siblingDirectory);
        File.WriteAllText(Path.Combine(siblingDirectory, "keep.txt"), "keep");

        var result = await _cli.RunAsync(workspace.SourceDir, workspace.TargetDir, "--prune-empty-dirs");

        Assert.Equal(0, result.ExitCode);
        Assert.True(Directory.Exists(workspace.SourceDir));
        Assert.True(Directory.Exists(siblingDirectory));
        Assert.True(File.Exists(Path.Combine(siblingDirectory, "keep.txt")));
    }
}
