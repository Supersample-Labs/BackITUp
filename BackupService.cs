namespace BackITUp;

internal sealed class BackupService
{
    public async Task RunAsync(
        IReadOnlyCollection<string> sourceFolders,
        string destinationFolder,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        if (sourceFolders.Count == 0)
        {
            throw new InvalidOperationException("Add at least one folder to back up.");
        }

        if (string.IsNullOrWhiteSpace(destinationFolder))
        {
            throw new InvalidOperationException("Select a destination folder or file share.");
        }

        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(destinationFolder);

            foreach (var sourceFolder in sourceFolders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (!Directory.Exists(sourceFolder))
                {
                    progress.Report($"Skipped missing source: {sourceFolder}");
                    continue;
                }

                var targetFolder = Path.Combine(destinationFolder, BuildTargetFolderName(sourceFolder));
                CopyDirectory(sourceFolder, targetFolder, progress, cancellationToken);
            }
        }, cancellationToken);
    }

    private static void CopyDirectory(
        string sourceFolder,
        string targetFolder,
        IProgress<string> progress,
        CancellationToken cancellationToken)
    {
        var sourceRoot = Path.GetFullPath(sourceFolder);
        var targetRoot = Path.GetFullPath(targetFolder);
        var copied = 0;
        var skipped = 0;

        progress.Report($"Backing up {sourceRoot}");
        Directory.CreateDirectory(targetRoot);

        var options = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            RecurseSubdirectories = true,
            ReturnSpecialDirectories = false
        };

        foreach (var directory in Directory.EnumerateDirectories(sourceRoot, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSameOrChildPath(directory, targetRoot))
            {
                continue;
            }

            var relativePath = Path.GetRelativePath(sourceRoot, directory);
            Directory.CreateDirectory(Path.Combine(targetRoot, relativePath));
        }

        foreach (var sourceFile in Directory.EnumerateFiles(sourceRoot, "*", options))
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (IsSameOrChildPath(sourceFile, targetRoot))
            {
                skipped++;
                continue;
            }

            try
            {
                var relativePath = Path.GetRelativePath(sourceRoot, sourceFile);
                var targetFile = Path.Combine(targetRoot, relativePath);
                var targetDirectory = Path.GetDirectoryName(targetFile);

                if (!string.IsNullOrEmpty(targetDirectory))
                {
                    Directory.CreateDirectory(targetDirectory);
                }

                if (!NeedsCopy(sourceFile, targetFile))
                {
                    skipped++;
                    continue;
                }

                File.Copy(sourceFile, targetFile, overwrite: true);
                File.SetLastWriteTimeUtc(targetFile, File.GetLastWriteTimeUtc(sourceFile));
                copied++;

                if (copied % 25 == 0)
                {
                    progress.Report($"Copied {copied} files from {sourceRoot}");
                }
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                skipped++;
                progress.Report($"Skipped {sourceFile}: {ex.Message}");
            }
        }

        progress.Report($"Finished {sourceRoot}: {copied} copied, {skipped} already current/skipped.");
    }

    private static bool NeedsCopy(string sourceFile, string targetFile)
    {
        if (!File.Exists(targetFile))
        {
            return true;
        }

        var sourceInfo = new FileInfo(sourceFile);
        var targetInfo = new FileInfo(targetFile);

        return sourceInfo.Length != targetInfo.Length ||
            sourceInfo.LastWriteTimeUtc > targetInfo.LastWriteTimeUtc.AddSeconds(1);
    }

    private static string BuildTargetFolderName(string sourceFolder)
    {
        var fullPath = Path.GetFullPath(sourceFolder).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(fullPath.Select(character =>
            invalid.Contains(character) || character is ':' or '\\' or '/' ? '_' : character).ToArray());

        return string.IsNullOrWhiteSpace(safe) ? "Backup" : safe;
    }

    private static bool IsSameOrChildPath(string path, string possibleParent)
    {
        var fullPath = Path.GetFullPath(path)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var parent = Path.GetFullPath(possibleParent)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return fullPath.Equals(parent, StringComparison.OrdinalIgnoreCase) ||
            fullPath.StartsWith(parent + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }
}
