namespace WinPEBuilder.Core.Processes;

/// <summary>Comando externo a executar (sem shell, a menos que o FileName seja o próprio cmd.exe).</summary>
public sealed record CommandSpec(
    string FileName,
    string Arguments,
    string? WorkingDirectory = null,
    IReadOnlyDictionary<string, string>? Environment = null)
{
    /// <summary>Texto para o log; não contém segredos (nenhum comando deste app recebe credenciais).</summary>
    public override string ToString() => $"{FileName} {Arguments}";

    /// <summary>
    /// Executa um .cmd/.bat via cmd.exe. Com "/s /c" o cmd remove só as aspas externas, então caminhos
    /// com espaços (como "Program Files (x86)") continuam corretos.
    /// </summary>
    public static CommandSpec Cmd(string scriptPath, string arguments, IReadOnlyDictionary<string, string>? environment = null) =>
        new("cmd.exe", $"/s /c \"\"{scriptPath}\" {arguments}\"", Environment: environment);

    /// <summary>Coloca o argumento entre aspas para a linha de comando do Windows.</summary>
    public static string Quote(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        // Barras invertidas finais antes da aspa de fechamento precisam ser dobradas.
        var trimmed = value.TrimEnd('\\');
        var trailing = value.Length - trimmed.Length;
        return "\"" + trimmed.Replace("\"", "\\\"") + new string('\\', trailing * 2) + "\"";
    }
}
