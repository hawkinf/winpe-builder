using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using WinPEBuilder.Core.Processes;

namespace WinPEBuilder.Core.Adk;

/// <summary>Baixa um arquivo informando o progresso (0..1, ou null se o tamanho for desconhecido).</summary>
public interface IFileDownloader
{
    Task DownloadAsync(Uri source, string destinationPath, IProgress<double?> progress, CancellationToken cancellationToken);
}

public sealed class HttpFileDownloader(HttpClient http) : IFileDownloader
{
    public async Task DownloadAsync(Uri source, string destinationPath, IProgress<double?> progress, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destinationPath)!);
        var partial = destinationPath + ".part";
        using (var response = await http.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength;
            await using var input = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            await using var output = new FileStream(partial, FileMode.Create, FileAccess.Write, FileShare.None, 81920, useAsync: true);
            var buffer = new byte[81920];
            long done = 0;
            int read;
            while ((read = await input.ReadAsync(buffer, cancellationToken).ConfigureAwait(false)) > 0)
            {
                await output.WriteAsync(buffer.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                done += read;
                progress.Report(total is > 0 ? (double)done / total.Value : null);
            }
        }

        File.Move(partial, destinationPath, overwrite: true);
    }
}

/// <summary>Confere se o instalador baixado é um executável assinado pela Microsoft.</summary>
public static class AuthenticodeVerifier
{
    public static bool IsSignedByMicrosoft(string path)
    {
        try
        {
#pragma warning disable SYSLIB0057 // CreateFromSignedFile é a API que lê o certificado de um arquivo assinado.
            using var certificate = new X509Certificate2(X509Certificate.CreateFromSignedFile(path));
#pragma warning restore SYSLIB0057
            using var chain = new X509Chain();
            return certificate.Subject.Contains("O=Microsoft Corporation", StringComparison.OrdinalIgnoreCase)
                && chain.Build(certificate);
        }
        catch (CryptographicException)
        {
            return false; // sem assinatura / arquivo corrompido.
        }
    }
}

public enum InstallPhase
{
    DownloadingAdk,
    DownloadingWinPeAddon,
    InstallingAdk,
    InstallingWinPeAddon,
    Done,
}

public sealed record InstallProgress(InstallPhase Phase, double? Fraction, string Message);

/// <summary>Baixa e instala (silenciosamente) o ADK e/ou o WinPE add-on.</summary>
public sealed class AdkInstaller(
    IProcessRunner runner,
    IFileDownloader downloader,
    string downloadDirectory,
    Func<string, bool>? isSignedByMicrosoft = null)
{
    private readonly Func<string, bool> _verify = isSignedByMicrosoft ?? AuthenticodeVerifier.IsSignedByMicrosoft;

    /// <summary>0 = ok; 3010 = ok, mas pede reinicialização.</summary>
    private static bool IsSuccess(int exitCode) => exitCode is 0 or 3010;

    public const string DeploymentToolsFeature = "OptionId.DeploymentTools";
    public const string WinPeFeature = "OptionId.WindowsPreinstallationEnvironment";

    public static string BuildSetupArguments(string feature, string? installPath)
    {
        var args = $"/quiet /norestart /ceip off /features {feature}";
        return installPath is null ? args : $"{args} /installpath {CommandSpec.Quote(installPath)}";
    }

    /// <summary>
    /// Instala Deployment Tools + WinPE add-on da versão pedida. Se <paramref name="installPath"/> for
    /// null, usa o caminho padrão do instalador (Program Files).
    /// </summary>
    public async Task InstallAsync(
        AdkRelease release,
        string? installPath,
        IProgress<InstallProgress> progress,
        Action<string> log,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(release);
        ArgumentNullException.ThrowIfNull(progress);
        ArgumentNullException.ThrowIfNull(log);

        var adkSetup = Path.Combine(downloadDirectory, release.Id, "adksetup.exe");
        var peSetup = Path.Combine(downloadDirectory, release.Id, "adkwinpesetup.exe");

        await FetchAsync(release.AdkUrl, adkSetup, InstallPhase.DownloadingAdk, "Baixando o instalador do ADK", progress, log, cancellationToken).ConfigureAwait(false);
        await FetchAsync(release.WinPeAddonUrl, peSetup, InstallPhase.DownloadingWinPeAddon, "Baixando o instalador do WinPE add-on", progress, log, cancellationToken).ConfigureAwait(false);

        progress.Report(new InstallProgress(InstallPhase.InstallingAdk, null, "Instalando Deployment Tools (pode demorar vários minutos)…"));
        await RunSetupAsync(adkSetup, DeploymentToolsFeature, installPath, log, cancellationToken).ConfigureAwait(false);

        progress.Report(new InstallProgress(InstallPhase.InstallingWinPeAddon, null, "Instalando o WinPE add-on (pode demorar vários minutos)…"));
        await RunSetupAsync(peSetup, WinPeFeature, installPath, log, cancellationToken).ConfigureAwait(false);

        progress.Report(new InstallProgress(InstallPhase.Done, 1, "ADK instalado."));
    }

    private async Task FetchAsync(Uri url, string destination, InstallPhase phase, string message, IProgress<InstallProgress> progress, Action<string> log, CancellationToken ct)
    {
        log($"{message}: {url}");
        progress.Report(new InstallProgress(phase, 0, message));
        var fraction = new Progress<double?>(f => progress.Report(new InstallProgress(phase, f, message)));
        await downloader.DownloadAsync(url, destination, fraction, ct).ConfigureAwait(false);

        if (!_verify(destination))
        {
            File.Delete(destination);
            throw new InvalidDataException("O instalador baixado não tem assinatura válida da Microsoft e foi descartado.");
        }
    }

    private async Task RunSetupAsync(string setupExe, string feature, string? installPath, Action<string> log, CancellationToken ct)
    {
        var command = new CommandSpec(setupExe, BuildSetupArguments(feature, installPath));
        log($"> {command}");
        var exit = await runner.RunAsync(command, log, ct).ConfigureAwait(false);
        if (!IsSuccess(exit))
        {
            throw new InvalidOperationException($"A instalação falhou (código {exit}) em {Path.GetFileName(setupExe)}. Veja o log do instalador em %TEMP%\\Microsoft Windows Kits Installer*.log.");
        }
    }
}
