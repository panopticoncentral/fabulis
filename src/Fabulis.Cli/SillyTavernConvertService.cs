namespace Fabulis.Cli;

public class SillyTavernConvertService
{
    public Task<ConvertResult> ConvertAsync(string sourcePath, string destPath)
    {
        throw new NotImplementedException(
            "SillyTavernConvertService.ConvertAsync is not implemented yet.");
    }
}

public class ConvertResult
{
    public int DraftsWritten { get; set; }
    public int FilesSkipped { get; set; }
    public int FilesFailed { get; set; }
}
