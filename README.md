# move

`move` is a .NET 10 command-line tool for copying or moving files from one directory tree into another in parallel.

It is designed for sync-style workflows where destination files may already exist.

## What It Does

- By default, `move` moves matching files into the target tree.
- `--keep-source` switches the operation to copy mode and leaves the source files in place.
- If a destination file already exists and is considered already synced, default move mode deletes the source file instead of treating that as an error.
- Files can be filtered by wildcard path pattern and by the first line of the file.
- First-line filtering supports both plain text files and `.gz` files.

## Build

```bash
dotnet build
```

## Command Shape

```text
move <SourceDir> <TargetDir> [options]
```

`SourceDir` and `TargetDir` must already exist.

## Options

- `--pattern`, `-p`: Only process file names that match a wildcard pattern. Default: `*`
- `--count`, `-n`: Stop after processing this many matching files
- `--search`, `-s`: Only process files whose first line starts with the given text
- `--maxdop`, `-j`: Maximum degree of parallelism. Default: `-1`
- `--keep-source`: Copy files instead of moving them
- `--overwrite`: Replace an existing destination file
- `--accept-existing`: Treat any existing destination file as already synced without checking metadata
- `--dry-run`: Report what would happen without copying or moving files
- `--verbose`, `-v`: Emit debug-style output for existing files and conflicts

## Existing File Behavior

When `--overwrite` is not used and the destination file already exists:

- If `--accept-existing` is set, the destination is treated as already synced
- Otherwise, the destination is treated as already synced only when file length and `LastWriteTimeUtc` match
- In default move mode, synced destination files cause the source file to be deleted
- With `--keep-source`, synced destination files leave the source file untouched
- Non-matching existing files are reported only when `--verbose` is enabled

## Usage Examples

Copy everything from one directory tree into another:

```bash
dotnet run -- /data/incoming /data/archive --keep-source
```

Move only `.csv` files:

```bash
dotnet run -- /data/incoming /data/archive --pattern "*.csv"
```

Move up to 500 gzip or text files whose first line starts with `HDR|`:

```bash
dotnet run -- /data/incoming /data/archive --search "HDR|" --count 500
```

Preview what a move would do without changing anything:

```bash
dotnet run -- /data/incoming /data/archive --dry-run --verbose
```

Copy using up to 8 workers and overwrite existing destination files:

```bash
dotnet run -- /data/incoming /data/archive --keep-source --maxdop 8 --overwrite
```

Prune source files when matching destination files already exist, without checking timestamps or size:

```bash
dotnet run -- /data/incoming /data/archive --accept-existing
```

## Output

- Progress is written periodically to standard error
- Final totals are written to standard error
- Verbose per-file diagnostics are written to standard output
