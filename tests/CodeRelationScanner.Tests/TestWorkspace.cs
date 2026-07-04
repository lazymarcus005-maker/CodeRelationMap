namespace CodeRelationScanner.Tests;

internal sealed class TestWorkspace : IDisposable
{
    private TestWorkspace(string path)
    {
        Path = path;
    }

    public string Path { get; }

    public static TestWorkspace Create()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"CodeRelationScannerTests-{Guid.NewGuid():N}");
        Directory.CreateDirectory(path);
        return new TestWorkspace(path);
    }

    public void Write(string relativePath, string content)
    {
        var fullPath = System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));
        var directory = System.IO.Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrWhiteSpace(directory))
        {
            Directory.CreateDirectory(directory);
        }

        File.WriteAllText(fullPath, content);
    }

    public void Dispose()
    {
        if (Directory.Exists(Path))
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
