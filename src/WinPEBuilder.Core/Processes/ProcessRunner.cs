using System.Diagnostics;
using System.Globalization;
using System.Text;

namespace WinPEBuilder.Core.Processes;

/// <summary>Executa comandos externos entregando a saída linha a linha.</summary>
public interface IProcessRunner
{
    /// <returns>Código de saída do processo.</returns>
    Task<int> RunAsync(CommandSpec command, Action<string> onOutput, CancellationToken cancellationToken);
}

public sealed class ProcessRunner : IProcessRunner
{
    public async Task<int> RunAsync(CommandSpec command, Action<string> onOutput, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(onOutput);

        // DISM, copype e oscdimg escrevem na página de código OEM do console (ex.: 850 no Brasil).
        Encoding outputEncoding;
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            outputEncoding = Encoding.GetEncoding(CultureInfo.CurrentCulture.TextInfo.OEMCodePage);
        }
        catch (ArgumentException)
        {
            outputEncoding = Encoding.UTF8;
        }

        var startInfo = new ProcessStartInfo(command.FileName, command.Arguments)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            StandardOutputEncoding = outputEncoding,
            StandardErrorEncoding = outputEncoding,
            WorkingDirectory = command.WorkingDirectory ?? Environment.CurrentDirectory,
        };
        if (command.Environment is not null)
        {
            foreach (var (key, value) in command.Environment)
            {
                startInfo.Environment[key] = value;
            }
        }

        using var process = new Process { StartInfo = startInfo };
        var gate = new object();
        void Forward(string? line)
        {
            if (line is null)
            {
                return;
            }

            lock (gate)
            {
                onOutput(line);
            }
        }

        process.OutputDataReceived += (_, e) => Forward(e.Data);
        process.ErrorDataReceived += (_, e) => Forward(e.Data);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Não foi possível iniciar: {command.FileName}");
        }

        process.StandardInput.Close(); // nenhum comando pode ficar esperando "Y/N".
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            try
            {
                process.Kill(entireProcessTree: true);
            }
            catch (InvalidOperationException)
            {
                // já terminou.
            }

            throw;
        }

        process.WaitForExit(); // garante o esvaziamento dos buffers de saída.
        return process.ExitCode;
    }
}
