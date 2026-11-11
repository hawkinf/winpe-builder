namespace WinPEBuilder.Core;

/// <summary>Grava o log de cada geração em arquivo (para suporte), mantendo só os mais recentes.</summary>
public sealed class LogFile : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;

    public string Path { get; }

    private LogFile(string path, StreamWriter writer)
    {
        Path = path;
        _writer = writer;
    }

    /// <summary>Cria logs\build-AAAAMMDD-HHMMSS.log e apaga os mais antigos além de <paramref name="keep"/>.</summary>
    public static LogFile Create(string directory, int keep = 20)
    {
        Directory.CreateDirectory(directory);
        var existing = new DirectoryInfo(directory).GetFiles("build-*.log").OrderByDescending(f => f.Name).Skip(Math.Max(keep - 1, 0));
        foreach (var old in existing)
        {
            try
            {
                old.Delete();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // arquivo em uso: ignora.
            }
        }

        var path = System.IO.Path.Combine(directory, $"build-{DateTime.Now:yyyyMMdd-HHmmss}.log");
        var writer = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.Read)) { AutoFlush = true };
        return new LogFile(path, writer);
    }

    public void WriteLine(string line)
    {
        lock (_gate)
        {
            _writer.WriteLine($"{DateTime.Now:HH:mm:ss} {line}");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _writer.Dispose();
        }
    }
}
