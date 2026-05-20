namespace Fabulis.Cli;

public class SillyTavernConvertService
{
    public async Task<ConvertResult> ConvertAsync(string sourcePath, string destPath)
    {
        var source = new DirectoryInfo(sourcePath);
        if (!source.Exists)
            throw new DirectoryNotFoundException($"Source directory not found: {sourcePath}");

        if (Directory.Exists(destPath) || File.Exists(destPath))
            throw new IOException($"Destination already exists: {destPath}");

        var jsonlFiles = source.GetFiles("*.jsonl").OrderBy(f => f.Name).ToArray();
        if (jsonlFiles.Length == 0)
            throw new InvalidOperationException(
                $"No .jsonl files found in '{sourcePath}'.");

        var draftsDir = Path.Combine(destPath, "_drafts");
        Directory.CreateDirectory(draftsDir);

        var result = new ConvertResult();
        foreach (var file in jsonlFiles)
        {
            // Conversion logic lands in Tasks 4-6.
            _ = file;
        }

        await Task.CompletedTask;
        return result;
    }
}

public class ConvertResult
{
    public int DraftsWritten { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }
}
