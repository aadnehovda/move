using System;
using System.Threading.Tasks;
using System.IO;
using System.Linq;
using System.Threading;
using System.Text;
using System.CommandLine;

using System.IO.Compression;
using System.Diagnostics;

Argument<DirectoryInfo> arg_source_dir = new("SourceDir") { Arity = ArgumentArity.ExactlyOne, Description = "The source directory." };
arg_source_dir.AcceptExistingOnly();
Argument<DirectoryInfo> arg_target_dir = new("TargetDir") { Arity = ArgumentArity.ExactlyOne, Description = "The target directory." };
arg_target_dir.AcceptExistingOnly();

Option<string> arg_pattern = new("--pattern", "-p") { Description = "Only process file names that match this wildcard pattern. Supports * and ?.", DefaultValueFactory = _ => "*" };
Option<int?> arg_count = new("--count", "-n") { Description = "Number of files to process." };
Option<string> arg_search = new("--search", "-s") { Description = "Only process files whose first line starts with this value. Supports .gz files as well as plain text." };
Option<int> arg_max_dop = new("--maxdop", "-j") { Description = "Maximum number of worker threads.", DefaultValueFactory = _ => -1 };
Option<bool> arg_keep_source = new("--keep-source") { Description = "Copy files into TargetDir and keep the source files in place." };
Option<bool> arg_overwrite = new("--overwrite") { Description = "Replace an existing destination file." };
Option<bool> arg_accept_existing = new("--accept-existing") { Description = "Treat any existing destination file as already synced without checking metadata." };
Option<bool> arg_dry_run = new("--dry-run") { Description = "Report what would happen without copying or moving files." };
Option<bool> arg_verbose = new("--verbose", "-v") { Description = "Show more details." };

var root = new RootCommand("Copy or move files into TargetDir in parallel, with existing-file checks suited for sync-style workflows.")
{
	arg_source_dir,
	arg_target_dir,
	arg_pattern,
	arg_count,
	arg_search,
	arg_max_dop,
	arg_keep_source,
	arg_overwrite,
	arg_accept_existing,
	arg_dry_run,
	arg_verbose
};

root.SetAction((ParseResult cli, CancellationToken token) => Run(cli, token));

return await root.Parse(args).InvokeAsync();

async Task Run(ParseResult cli, CancellationToken token)
{
	var source_dir = cli.GetRequiredValue(arg_source_dir);
	var target_dir = cli.GetRequiredValue(arg_target_dir);
	var pattern = cli.GetRequiredValue(arg_pattern);
	var search = cli.GetValue(arg_search);
	var count = cli.GetValue(arg_count);
	var keep_source = cli.GetValue(arg_keep_source);
	var overwrite = cli.GetValue(arg_overwrite);
	var accept_existing = cli.GetValue(arg_accept_existing);
	var dry_run = cli.GetValue(arg_dry_run);
	var max_dop = cli.GetValue(arg_max_dop);
	var verbose = cli.GetValue(arg_verbose);

	var processed_files = 0UL;
	var start = Stopwatch.GetTimestamp();
	var progress = new PeriodicTimer(TimeSpan.FromSeconds(3));
	var report = Reporter();

	try
	{
		var files = source_dir
			.EnumerateFiles(pattern, SearchOption.AllDirectories)
			.ToAsyncEnumerable()
			.Take(Range.EndAt(count ?? Index.End));

		ParallelOptions options = new()
		{
			MaxDegreeOfParallelism = max_dop,
			CancellationToken = token
		};

		using (progress)
			await Parallel.ForEachAsync(files, options, ProcessFile);
	}
	catch (OperationCanceledException)
	{
		await cli.InvocationConfiguration.Error.WriteLineAsync("Operation was cancelled.");
	}
	finally
	{
		await report;
	}

	return;

	async Task Reporter()
	{
		try
		{
			var last_num = 0UL;
			var last_tick = start;

			while (await progress.WaitForNextTickAsync(token))
			{
				var current = Interlocked.Read(ref processed_files);
				var delta = current - last_num;
				var elapsed = Stopwatch.GetElapsedTime(last_tick).TotalSeconds;

				await cli.InvocationConfiguration.Error.WriteLineAsync(
					$"Processed {delta:N0} files. {delta / elapsed:N0} files/s.");

				last_tick = Stopwatch.GetTimestamp();
				last_num = current;
			}
		}
		catch (OperationCanceledException)
		{
		}

		await cli.InvocationConfiguration.Error.WriteLineAsync(
			$"Processed {Interlocked.Read(ref processed_files):N0} files in {Stopwatch.GetElapsedTime(start)}.");
	}


	async ValueTask ProcessFile(FileInfo source_file, CancellationToken cancel)
	{
		var relative = Path.GetRelativePath(source_dir.FullName, source_file.FullName);
		var target_file = new FileInfo(Path.Combine(target_dir.FullName, relative));

		var details = new StringBuilder();
		details.Append($"overwrite: {overwrite}");
		details.Append($", accept-existing: {accept_existing}");
		details.Append($", dry-run: {dry_run}");
		details.Append($", source: \"{source_file.FullName}\"");
		details.Append($", target: \"{target_file.FullName}\"");

		if (!await FileHeaderMatches(source_file.FullName, search, cancel))
			return;

		if (!overwrite && target_file.Exists)
		{
			await HandleExistingTarget();
			return;
		}

		Action<string, string, bool> action = keep_source ? File.Copy : File.Move;
		const int ERROR_ALREADY_EXISTS = unchecked((int)0x800700B7);
		const int ERROR_FILE_EXISTS = unchecked((int)0x80070050);
		try
		{
			if (!dry_run)
			{
				target_file.Directory?.Create();
				action(source_file.FullName, target_file.FullName, overwrite);
			}
		}
		catch (IOException ex) when (!overwrite &&
		                             ex.HResult is ERROR_ALREADY_EXISTS or ERROR_FILE_EXISTS)
		{
			await HandleExistingTarget();
		}
		finally
		{
			Interlocked.Increment(ref processed_files);
		}

		async Task HandleExistingTarget()
		{
			if (accept_existing || (source_file.Length == target_file.Length &&
			                        source_file.LastWriteTimeUtc == target_file.LastWriteTimeUtc))
			{
				if (keep_source)
				{
					if (verbose)
						await cli.InvocationConfiguration.Output.WriteLineAsync(
							$"{{ log: \"debug\", \"op\": \"exists\", {details} }}");
				}
				else
				{
					if (!dry_run)
						source_file.Delete();
					if (verbose)
						await cli.InvocationConfiguration.Output.WriteLineAsync(
							$"{{ log: \"debug\", \"op\": \"prune-source\", {details} }}");
				}

				return;
			}

			if (verbose)
				await cli.InvocationConfiguration.Output.WriteLineAsync(
					$"{{ log: \"debug\", op: \"conflict\", {details} }}");
		}
	}
}

static async Task<bool> FileHeaderMatches(string file, string? pattern, CancellationToken cancel)
{
	if (string.IsNullOrWhiteSpace(pattern))
		return true;

	await using var fs = File.OpenRead(file);
	await using Stream data = IsGzipStream(fs) ? new GZipStream(fs, CompressionMode.Decompress, true) : fs;
	using var reader = new StreamReader(data, Encoding.UTF8);
	var line = await reader.ReadLineAsync(cancel);
	return !string.IsNullOrWhiteSpace(line) && line.StartsWith(pattern);
}

static bool IsGzipStream(Stream stream)
{
	if (!stream.CanRead)
		return false;

	Span<byte> header = stackalloc byte[2];
	var bytesRead = stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false);
	if (stream.CanSeek)
		stream.Seek(0, SeekOrigin.Begin);

	return bytesRead == header.Length && header[0] == 0x1F && header[1] == 0x8B;
}
