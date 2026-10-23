namespace GenerateDocService.DocumentProcessing.Application.Tests;

internal static class TestArtifactWriter
{
    public static async Task<string> WriteAsync(string relativePath, byte[] content, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(relativePath);

        var outputPath = Path.Combine(FindSolutionRoot(), "artifacts", "test-results", relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath)!);

        await File.WriteAllBytesAsync(outputPath, content, cancellationToken);
        return outputPath;
    }

    private static string FindSolutionRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "GenerateDocService.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not locate GenerateDocService.slnx from the test output directory.");
    }
}