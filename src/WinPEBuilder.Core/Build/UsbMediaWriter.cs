using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.Core.Build;

/// <summary>Pendrive/disco removível candidato à gravação.</summary>
public sealed record RemovableDrive(char Letter, string Label, long TotalBytes)
{
    public override string ToString() =>
        $"{Letter}: — {(string.IsNullOrWhiteSpace(Label) ? "(sem nome)" : Label)} — {TotalBytes / (1024.0 * 1024 * 1024):0.#} GB";
}

/// <summary>
/// Grava o WinPE gerado num pendrive com o MakeWinPEMedia /UFD. ATENÇÃO: isso FORMATA o pendrive.
/// A interface precisa confirmar com o usuário (mostrando letra, nome e tamanho) antes de chamar WriteAsync.
/// </summary>
public sealed class UsbMediaWriter(IProcessRunner runner, Func<IReadOnlyList<RemovableDrive>>? listDrives = null)
{
    private readonly Func<IReadOnlyList<RemovableDrive>> _listDrives = listDrives ?? ListRemovableDrives;

    /// <summary>Só discos removíveis prontos; nunca o disco do Windows.</summary>
    public static IReadOnlyList<RemovableDrive> ListRemovableDrives()
    {
        var system = Path.GetPathRoot(Environment.SystemDirectory);
        return DriveInfo.GetDrives()
            .Where(d => d.DriveType == DriveType.Removable && d.IsReady
                && !string.Equals(d.RootDirectory.FullName, system, StringComparison.OrdinalIgnoreCase))
            .Select(d => new RemovableDrive(char.ToUpperInvariant(d.Name[0]), d.VolumeLabel, d.TotalSize))
            .ToList();
    }

    public IReadOnlyList<RemovableDrive> GetCandidates() => _listDrives();

    public static string BuildArguments(string workDirectory, char driveLetter) =>
        $"/UFD /F {CommandSpec.Quote(workDirectory)} {char.ToUpperInvariant(driveLetter)}:";

    /// <exception cref="BuildException">Pendrive inválido ou falha do MakeWinPEMedia.</exception>
    public async Task WriteAsync(
        AdkInstall kit,
        string workDirectory,
        char driveLetter,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(kit);
        ArgumentNullException.ThrowIfNull(log);

        // Revalida na hora de gravar: a lista da tela pode estar velha (pendrive trocado).
        var target = _listDrives().FirstOrDefault(d => d.Letter == char.ToUpperInvariant(driveLetter))
            ?? throw new BuildException($"{char.ToUpperInvariant(driveLetter)}: não é um disco removível pronto. Nada foi gravado.");

        if (!File.Exists(kit.MakeWinPeMediaCmd))
        {
            throw new BuildException("MakeWinPEMedia.cmd não encontrado — a gravação em pendrive exige o WinPE add-on do ADK.");
        }

        var layout = new BuildLayout(Path.GetFullPath(workDirectory));
        if (!File.Exists(layout.BootWim) || !Directory.Exists(layout.FwFiles))
        {
            throw new BuildException("Gere o WinPE antes: a pasta de trabalho não contém media\\sources\\boot.wim.");
        }

        log($"Formatando e gravando em {target} …");
        var command = CommandSpec.Cmd(kit.MakeWinPeMediaCmd, BuildArguments(layout.WorkDirectory, target.Letter), kit.BuildEnvironment());
        log("> " + command);
        var tail = new Queue<string>();
        var exit = await runner.RunAsync(command, line =>
        {
            log(line);
            tail.Enqueue(line);
            if (tail.Count > 6)
            {
                tail.Dequeue();
            }
        }, cancellationToken).ConfigureAwait(false);

        if (exit != 0)
        {
            throw new BuildException($"MakeWinPEMedia falhou (código {exit}).{Environment.NewLine}{string.Join(Environment.NewLine, tail)}");
        }

        log("Pendrive gravado.");
    }
}
