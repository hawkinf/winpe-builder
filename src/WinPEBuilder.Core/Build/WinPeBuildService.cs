using WinPEBuilder.Core.Programs;
using WinPEBuilder.Core.Adk;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.Core.Build;

/// <summary>
/// Gera o WinPE: prepara a base (ADK/copype ou boot.wim de ISO), monta o boot.wim, aplica pacotes e
/// ajustes, desmonta com commit. A etapa final (arquivos do usuário e ISO) é encadeada em BuildAsync.
/// </summary>
/// <param name="runner">Executa DISM/copype/oscdimg.</param>
/// <param name="locator">Acha o ADK instalado.</param>
/// <param name="allowedWorkRoot">
/// Pasta-mãe onde a pasta de trabalho pode ser apagada. Qualquer WorkDirectory fora dela é recusado,
/// para que um erro de configuração nunca apague pastas do usuário.
/// </param>
/// <param name="isoRootFromDrive">Raiz de uma ISO montada na letra dada (troca só em testes).</param>
public sealed class WinPeBuildService(
    IProcessRunner runner,
    AdkLocator locator,
    string allowedWorkRoot,
    Func<char, string>? isoRootFromDrive = null)
{
    private readonly Func<char, string> _isoRoot = isoRootFromDrive ?? (letter => letter + @":\");

    private sealed record BuildStep(string Name, Func<Task> Execute);

    /// <summary>Estado de uma geração em andamento.</summary>
    private sealed class BuildRun(BuildOptions options, AdkInstall kit, BuildLayout layout, Action<string> log, CancellationToken ct)
    {
        public BuildOptions Options { get; } = options;
        public AdkInstall Kit { get; } = kit;
        public BuildLayout Layout { get; } = layout;
        public Action<string> Log { get; } = log;
        public CancellationToken Ct { get; } = ct;
        public bool Mounted { get; set; }
    }

    public async Task BuildAsync(BuildOptions options, IProgress<BuildProgress> progress, Action<string> log, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(log);

        var errors = options.Validate();
        if (errors.Count > 0)
        {
            throw new BuildException(string.Join(Environment.NewLine, errors));
        }

        if (!FileSystemHelper.IsInside(options.WorkDirectory, allowedWorkRoot))
        {
            throw new BuildException($"A pasta de trabalho precisa ficar dentro de {allowedWorkRoot}.");
        }

        CheckFilesFolder(options);
        var kit = ResolveKit(options);
        var run =new BuildRun(options, kit, new BuildLayout(Path.GetFullPath(options.WorkDirectory)), log, cancellationToken);
        var steps = PlanSteps(run);

        try
        {
            for (var i = 0; i < steps.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress.Report(new BuildProgress(i + 1, steps.Count, steps[i].Name));
                log($"== [{i + 1}/{steps.Count}] {steps[i].Name}");
                await steps[i].Execute().ConfigureAwait(false);
            }
        }
        finally
        {
            if (run.Mounted)
            {
                // Falha ou cancelamento com a imagem montada: descarta, senão a pasta fica travada.
                log("Desmontando a imagem (descartando alterações)…");
                await TryDismAsync(run, $"/Unmount-Image /MountDir:{CommandSpec.Quote(run.Layout.Mount)} /Discard").ConfigureAwait(false);
                run.Mounted = false;
            }
        }

        progress.Report(new BuildProgress(steps.Count, steps.Count, "Concluído"));
    }

    // ---- planejamento -------------------------------------------------------------------------------

    private AdkInstall ResolveKit(BuildOptions options)
    {
        if (options.Source == WinPeSourceKind.Adk)
        {
            var kit = locator.FindWinPe(options.Architecture)
                ?? throw new BuildException(
                    $"O WinPE add-on do ADK para {options.Architecture.ToDisplayName()} não está instalado. Instale-o na seção Requisitos"
                    + (options.Architecture == WinPeArchitecture.X86 ? " (32 bits exige o ADK do Windows 10 2004) ou use a fonte \"boot.wim de uma ISO\"." : "."));
            if (!kit.HasDeploymentTools)
            {
                throw new BuildException($"A instalação do ADK em {kit.KitsRoot} está sem o recurso Deployment Tools (DISM/oscdimg). Reinstale o ADK.");
            }

            return kit;
        }

        return locator.FindDeploymentTools()
            ?? throw new BuildException("O Deployment Tools do ADK (DISM + oscdimg) não está instalado. Instale-o na seção Requisitos.");
    }

    private List<BuildStep> PlanSteps(BuildRun run)
    {
        var o = run.Options;
        var steps = new List<BuildStep>
        {
            new("Preparando a pasta de trabalho", () => PrepareWorkDirectoryAsync(run)),
        };

        if (o.Programs.Count > 0 && o.Source != WinPeSourceKind.Adk)
        {
            run.Log("Aviso: com a origem \"boot.wim de uma ISO\" não há .NET no WinPE, então o launcher (área de trabalho/menu Iniciar) "
                + $"não será instalado. Os programas ficam em %ARQUIVOS%\\{ProgramStaging.ProgramsFolderName} (unidade {StartnetScript.DriveLetter}:).");
        }

        if (o.Source == WinPeSourceKind.Adk)
        {
            steps.Add(new("Criando a base do WinPE (copype)", () => CopypeAsync(run)));
        }
        else
        {
            steps.Add(new("Extraindo o boot.wim da ISO", () => ExtractFromIsoAsync(run)));
        }

        steps.Add(new("Montando o boot.wim", () => MountAsync(run)));

        if (o.EffectiveComponents.Count > 0)
        {
            steps.Add(new("Adicionando pacotes opcionais", () => AddPackagesAsync(run)));
        }

        steps.Add(new("Aplicando ajustes (teclado, espaço temporário)", () => ConfigureAsync(run)));

        if (o.EffectiveFilesMode != FilesMode.None)
        {
            steps.Add(new("Criando o script de inicialização (startnet.cmd)", () => WriteStartnetAsync(run)));
        }

        if (o.InstallLauncher)
        {
            steps.Add(new("Instalando o launcher (área de trabalho e menu Iniciar)", () => InstallLauncherAsync(run)));
        }

        if (o.EffectiveFilesMode == FilesMode.InsideImage)
        {
            steps.Add(new("Copiando os arquivos para dentro da imagem", () => CopyFilesAsync(run, Path.Combine(run.Layout.Mount, StartnetScript.FilesFolderName))));
        }

        steps.Add(new("Desmontando e salvando o boot.wim", () => UnmountAsync(run)));

        if (o.EffectiveFilesMode == FilesMode.OnIso)
        {
            steps.Add(new("Copiando os arquivos para a mídia da ISO", () => CopyFilesAsync(run, Path.Combine(run.Layout.Media, StartnetScript.FilesFolderName))));
        }

        steps.Add(new("Gerando a ISO (oscdimg)", () => CreateIsoAsync(run)));
        return steps;
    }

    // ---- etapas ---------------------------------------------------------------------------------------

    private async Task PrepareWorkDirectoryAsync(BuildRun run)
    {
        var work = run.Layout.WorkDirectory;
        if (Directory.Exists(work))
        {
            run.Log("Limpando restos de uma geração anterior…");
            // Sobras de montagem de uma execução interrompida impediriam apagar a pasta.
            await TryDismAsync(run, "/Cleanup-Mountpoints").ConfigureAwait(false);
            if (Directory.Exists(run.Layout.Mount) && Directory.EnumerateFileSystemEntries(run.Layout.Mount).Any())
            {
                await TryDismAsync(run, $"/Unmount-Image /MountDir:{CommandSpec.Quote(run.Layout.Mount)} /Discard").ConfigureAwait(false);
            }

            FileSystemHelper.DeleteDirectory(work);
        }

        var parent = Path.GetDirectoryName(work);
        if (parent is not null)
        {
            Directory.CreateDirectory(parent);
        }

        if (run.Options.Source == WinPeSourceKind.BootWimFromIso)
        {
            // O copype cria a pasta sozinho; no modo ISO nós mesmos criamos o mesmo layout.
            Directory.CreateDirectory(run.Layout.Media);
            Directory.CreateDirectory(run.Layout.Mount);
            Directory.CreateDirectory(run.Layout.FwFiles);
        }
    }

    private async Task CopypeAsync(BuildRun run)
    {
        var command = CommandSpec.Cmd(
            run.Kit.CopypeCmd,
            $"{run.Options.Architecture.ToAdkName()} {CommandSpec.Quote(run.Layout.WorkDirectory)}",
            run.Kit.BuildEnvironment());
        await ExecuteAsync(run, command, "copype").ConfigureAwait(false);

        if (!File.Exists(run.Layout.BootWim))
        {
            throw new BuildException("O copype terminou sem criar media\\sources\\boot.wim. Veja o log.");
        }
    }

    private async Task ExtractFromIsoAsync(BuildRun run)
    {
        var iso = run.Options.SourceIsoPath!;
        var drive = await MountIsoAsync(run, iso).ConfigureAwait(false);
        try
        {
            var root = _isoRoot(drive);
            var sourceWim = Path.Combine(root, "sources", "boot.wim");
            if (!File.Exists(sourceWim))
            {
                throw new BuildException("A ISO não tem sources\\boot.wim — não é uma ISO de instalação do Windows.");
            }

            var expectedEfi = run.Options.Architecture == WinPeArchitecture.X64 ? "bootx64.efi" : "bootia32.efi";
            if (!File.Exists(Path.Combine(root, "efi", "boot", expectedEfi)))
            {
                run.Log($"AVISO: a ISO não tem efi\\boot\\{expectedEfi}; ela pode ser de outra arquitetura ou sem suporte a UEFI.");
            }

            run.Log("Copiando arquivos de boot (boot, efi, bootmgr)…");
            foreach (var folder in new[] { "boot", "efi" })
            {
                var from = Path.Combine(root, folder);
                if (Directory.Exists(from))
                {
                    FileSystemHelper.CopyDirectory(from, Path.Combine(run.Layout.Media, folder), run.Ct);
                }
            }

            foreach (var file in new[] { "bootmgr", "bootmgr.efi" })
            {
                var from = Path.Combine(root, file);
                if (File.Exists(from))
                {
                    var to = Path.Combine(run.Layout.Media, file);
                    File.Copy(from, to, overwrite: true);
                    File.SetAttributes(to, FileAttributes.Normal);
                }
            }

            Directory.CreateDirectory(run.Layout.SourcesDirectory);
            // Índice 1 do boot.wim de instalação = WinPE puro (o índice 2 é o Setup). /Bootable marca-o como o de boot.
            await DismAsync(
                run,
                $"/Export-Image /SourceImageFile:{CommandSpec.Quote(sourceWim)} /SourceIndex:1 " +
                $"/DestinationImageFile:{CommandSpec.Quote(run.Layout.BootWim)} /Compress:max /Bootable").ConfigureAwait(false);
        }
        finally
        {
            await DismountIsoAsync(run, iso).ConfigureAwait(false);
        }
    }

    private async Task MountAsync(BuildRun run)
    {
        Directory.CreateDirectory(run.Layout.Mount);
        await DismAsync(
            run,
            $"/Mount-Image /ImageFile:{CommandSpec.Quote(run.Layout.BootWim)} /Index:1 /MountDir:{CommandSpec.Quote(run.Layout.Mount)}").ConfigureAwait(false);
        run.Mounted = true;
    }

    private async Task AddPackagesAsync(BuildRun run)
    {
        var components = OptionalComponents.Resolve(run.Options.EffectiveComponents);
        var ocs = run.Kit.OptionalComponentsDirectory(run.Options.Architecture);
        foreach (var component in components)
        {
            var neutral = Path.Combine(ocs, component.Name + ".cab");
            if (!File.Exists(neutral))
            {
                throw new BuildException($"Pacote não encontrado no ADK: {neutral}");
            }

            await DismAsync(run, $"/Image:{CommandSpec.Quote(run.Layout.Mount)} /Add-Package /PackagePath:{CommandSpec.Quote(neutral)}").ConfigureAwait(false);

            var language = Path.Combine(ocs, "en-us", component.Name + "_en-us.cab");
            if (File.Exists(language))
            {
                await DismAsync(run, $"/Image:{CommandSpec.Quote(run.Layout.Mount)} /Add-Package /PackagePath:{CommandSpec.Quote(language)}").ConfigureAwait(false);
            }
            else
            {
                run.Log($"Aviso: {component.Name} não tem pacote de idioma en-us; seguindo sem ele.");
            }
        }
    }

    private async Task ConfigureAsync(BuildRun run)
    {
        var o = run.Options;
        if (!string.IsNullOrEmpty(o.InputLocale))
        {
            await DismAsync(run, $"/Image:{CommandSpec.Quote(run.Layout.Mount)} /Set-InputLocale:{o.InputLocale}").ConfigureAwait(false);
        }

        if (o.ScratchSpaceMb > 0)
        {
            await DismAsync(run, $"/Image:{CommandSpec.Quote(run.Layout.Mount)} /Set-ScratchSpace:{o.ScratchSpaceMb}").ConfigureAwait(false);
        }
    }

    private async Task UnmountAsync(BuildRun run)
    {
        await DismAsync(run, $"/Unmount-Image /MountDir:{CommandSpec.Quote(run.Layout.Mount)} /Commit").ConfigureAwait(false);
        run.Mounted = false;
    }

    private async Task CreateIsoAsync(BuildRun run)
    {
        var boot = BootFiles.Resolve(run.Kit, run.Options, run.Layout);
        run.Log($"Boot BIOS: {boot.EtfsBoot}");
        run.Log($"Boot UEFI: {boot.EfiSys}");

        // Deixa o layout completo (fwfiles) também no modo ISO, para o MakeWinPEMedia/pendrive.
        Directory.CreateDirectory(run.Layout.FwFiles);
        foreach (var file in boot.AllFiles)
        {
            var target = Path.Combine(run.Layout.FwFiles, Path.GetFileName(file));
            if (!string.Equals(Path.GetFullPath(file), Path.GetFullPath(target), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(file, target, overwrite: true);
            }
        }

        var output = Path.GetFullPath(run.Options.OutputIsoPath);
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        if (File.Exists(output))
        {
            try
            {
                File.Delete(output);
            }
            catch (IOException ex)
            {
                throw new BuildException($"Não foi possível substituir a ISO existente (está em uso?): {ex.Message}");
            }
        }

        var command = new CommandSpec(run.Kit.OscdimgExe, BootFiles.BuildOscdimgArguments(boot, run.Layout.Media, output, run.Options.VolumeLabel));
        await ExecuteAsync(run, command, "oscdimg").ConfigureAwait(false);

        if (!File.Exists(output) || new FileInfo(output).Length == 0)
        {
            throw new BuildException("O oscdimg terminou sem gerar a ISO. Veja o log.");
        }

        run.Log($"ISO gerada: {output} ({new FileInfo(output).Length / (1024 * 1024)} MB)");
    }

    private static async Task WriteStartnetAsync(BuildRun run)
    {
        var system32 = Path.Combine(run.Layout.Mount, "Windows", "System32");
        Directory.CreateDirectory(system32);
        var script = StartnetScript.Build(run.Options.EffectiveFilesMode, run.Options.InstallLauncher);
        await File.WriteAllBytesAsync(Path.Combine(system32, "startnet.cmd"), StartnetScript.ToBytes(script), run.Ct).ConfigureAwait(false);
        run.Log($"startnet.cmd gravado. No WinPE a pasta fica em %ARQUIVOS% e na unidade {StartnetScript.DriveLetter}: ({StartnetScript.DriveLetter}:\\startup.cmd, se existir, roda sozinho).");
    }

    private static async Task InstallLauncherAsync(BuildRun run)
    {
        var system32 = Path.Combine(run.Layout.Mount, "Windows", "System32");
        Directory.CreateDirectory(system32);
        var target = Path.Combine(system32, StartnetScript.LauncherFileName);
        await Task.Run(() => File.Copy(run.Options.LauncherExePath!, target, overwrite: true), run.Ct).ConfigureAwait(false);
        run.Log($"Launcher instalado em X:\\Windows\\System32\\{StartnetScript.LauncherFileName} (inicia pelo startnet.cmd; requer o pacote .NET, adicionado automaticamente).");
    }

    /// <summary>Copia a pasta de arquivos do usuário e os programas (com programs.json) para <paramref name="destination"/>.</summary>
    private static Task CopyFilesAsync(BuildRun run, string destination) => Task.Run(() =>
    {
        Directory.CreateDirectory(destination);

        var source = run.Options.FilesFolder;
        if (!string.IsNullOrEmpty(source))
        {
            var files = Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).ToList();
            var bytes = files.Sum(f => new FileInfo(f).Length);
            run.Log($"Copiando {files.Count} arquivo(s), {bytes / (1024 * 1024)} MB, de {source}");
            FileSystemHelper.CopyDirectory(source, destination, run.Ct);
        }

        var programs = run.Options.Programs;
        if (programs.Count > 0)
        {
            run.Log($"Copiando {programs.Count} programa(s), {ProgramStaging.TotalBytes(programs) / (1024 * 1024)} MB, para {ProgramStaging.ProgramsFolderName}\\");
            ProgramStaging.Stage(programs, destination, run.Ct, run.Options.DesktopLayout, run.Options.StartMenuOrder);
        }

        File.WriteAllText(Path.Combine(destination, StartnetScript.MarkerFileName), "WinPE Builder");
    }, run.Ct);

    /// <summary>Limite de arquivos dentro do boot.wim: tudo isso é carregado na RAM do PC.</summary>
    public const long MaxInsideImageBytes = 1024L * 1024 * 1024;

    /// <summary>Recusa pastas que se sobrepõem à de trabalho e arquivos grandes demais para a RAM.</summary>
    private static void CheckFilesFolder(BuildOptions options)
    {
        if (options.EffectiveFilesMode == FilesMode.None)
        {
            return;
        }

        var work = Path.GetFullPath(options.WorkDirectory).TrimEnd('\\');
        var copiedFolders = new List<string>();
        if (!string.IsNullOrEmpty(options.FilesFolder))
        {
            copiedFolders.Add(Path.GetFullPath(options.FilesFolder).TrimEnd('\\'));
        }

        copiedFolders.AddRange(options.Programs.Where(p => p.CopyFolder).Select(p => p.SourceFolder.TrimEnd('\\')));
        foreach (var folder in copiedFolders)
        {
            if (FileSystemHelper.IsInside(folder, work) || FileSystemHelper.IsInside(work, folder) ||
                string.Equals(folder, work, StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildException("A pasta de arquivos (ou de um programa) não pode conter nem estar dentro da pasta de trabalho do aplicativo.");
            }
        }

        if (options.EffectiveFilesMode == FilesMode.InsideImage)
        {
            var bytes = ProgramStaging.TotalBytes(options.Programs);
            if (!string.IsNullOrEmpty(options.FilesFolder))
            {
                bytes += Directory.EnumerateFiles(options.FilesFolder, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length);
            }

            if (bytes > MaxInsideImageBytes)
            {
                throw new BuildException(
                    $"Os arquivos somam {bytes / (1024 * 1024)} MB; dentro da imagem o limite é {MaxInsideImageBytes / (1024 * 1024)} MB (ocupam RAM). Use o modo \"na ISO\".");
            }
        }
    }

    // ---- ferramentas ------------------------------------------------------------------------------------

    private Task DismAsync(BuildRun run, string arguments) =>
        ExecuteAsync(run, new CommandSpec(run.Kit.DismExe, arguments, Environment: run.Kit.BuildEnvironment()), "DISM");

    /// <summary>Executa DISM sem lançar exceção (limpeza em caminhos de erro).</summary>
    private async Task TryDismAsync(BuildRun run, string arguments)
    {
        try
        {
            await runner.RunAsync(
                new CommandSpec(run.Kit.DismExe, arguments, Environment: run.Kit.BuildEnvironment()),
                line => LogLine(run, line),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            run.Log("Aviso na limpeza: " + ex.Message);
        }
    }

    private static void LogLine(BuildRun run, string line)
    {
        if (!BuildText.IsProgressNoise(line) && !string.IsNullOrWhiteSpace(line))
        {
            run.Log(line);
        }
    }

    private async Task ExecuteAsync(BuildRun run, CommandSpec command, string toolName)
    {
        run.Log("> " + command);
        var tail = new Queue<string>();
        var exit = await runner.RunAsync(command, line =>
        {
            LogLine(run, line);
            if (!string.IsNullOrWhiteSpace(line) && !BuildText.IsProgressNoise(line))
            {
                tail.Enqueue(line);
                if (tail.Count > 8)
                {
                    tail.Dequeue();
                }
            }
        }, run.Ct).ConfigureAwait(false);

        if (exit is not (0 or 3010))
        {
            throw new BuildException(
                $"{toolName} falhou (código {exit}).{Environment.NewLine}{string.Join(Environment.NewLine, tail)}");
        }
    }

    // ---- ISO montada ----------------------------------------------------------------------------------

    private static string PsQuote(string value) => "'" + value.Replace("'", "''") + "'";

    private async Task<char> MountIsoAsync(BuildRun run, string iso)
    {
        var lines = new List<string>();
        var script = $"(Mount-DiskImage -ImagePath {PsQuote(iso)} -PassThru | Get-Volume).DriveLetter";
        var command = new CommandSpec("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\"");
        run.Log("> " + command);
        var exit = await runner.RunAsync(command, lines.Add, run.Ct).ConfigureAwait(false);
        var letter = lines.Select(l => l.Trim()).FirstOrDefault(l => l.Length == 1 && char.IsLetter(l[0]));
        if (exit != 0 || letter is null)
        {
            throw new BuildException($"Não foi possível montar a ISO.{Environment.NewLine}{string.Join(Environment.NewLine, lines)}");
        }

        return letter[0];
    }

    private async Task DismountIsoAsync(BuildRun run, string iso)
    {
        try
        {
            var script = $"Dismount-DiskImage -ImagePath {PsQuote(iso)} | Out-Null";
            await runner.RunAsync(
                new CommandSpec("powershell.exe", $"-NoProfile -NonInteractive -ExecutionPolicy Bypass -Command \"{script}\""),
                line => LogLine(run, line),
                CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            run.Log("Aviso ao desmontar a ISO: " + ex.Message);
        }
    }
}
