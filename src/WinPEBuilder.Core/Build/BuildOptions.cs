using WinPEBuilder.Core.Programs;

namespace WinPEBuilder.Core.Build;

/// <summary>De onde vêm os arquivos-base do WinPE.</summary>
public enum WinPeSourceKind
{
    /// <summary>WinPE oficial do Windows ADK (copype). Suporta pacotes opcionais.</summary>
    Adk,

    /// <summary>boot.wim (índice 1) extraído de uma ISO de instalação do Windows. Sem pacotes opcionais.</summary>
    BootWimFromIso,
}

/// <summary>Onde colocar a pasta de arquivos do usuário.</summary>
public enum FilesMode
{
    /// <summary>Não incluir arquivos.</summary>
    None,

    /// <summary>Na raiz da ISO (pasta \Arquivos). Não ocupa RAM; o WinPE mapeia a pasta como a unidade P:.</summary>
    OnIso,

    /// <summary>Dentro do boot.wim (X:\Arquivos). Vai para a RAM — use só para poucos MB.</summary>
    InsideImage,
}

/// <summary>Tudo o que o usuário escolhe para gerar a ISO.</summary>
public sealed record BuildOptions
{
    public WinPeArchitecture Architecture { get; init; } = WinPeArchitecture.X64;

    public WinPeSourceKind Source { get; init; } = WinPeSourceKind.Adk;

    /// <summary>ISO do Windows com boot.wim (obrigatório quando Source = BootWimFromIso).</summary>
    public string? SourceIsoPath { get; init; }

    /// <summary>Pasta de trabalho (copype). Será apagada e recriada a cada geração.</summary>
    public required string WorkDirectory { get; init; }

    /// <summary>Caminho da ISO final.</summary>
    public required string OutputIsoPath { get; init; }

    /// <summary>Pacotes opcionais (nomes como "WinPE-PowerShell"); dependências são adicionadas sozinhas.</summary>
    public IReadOnlyList<string> OptionalComponents { get; init; } = [];

    /// <summary>Layout de teclado (ex.: "pt-BR"). Vazio mantém o padrão en-US.</summary>
    public string InputLocale { get; init; } = "pt-BR";

    /// <summary>Espaço temporário do WinPE em MB (32, 64, 128, 256 ou 512). 0 mantém o padrão.</summary>
    public int ScratchSpaceMb { get; init; } = 512;

    public string VolumeLabel { get; init; } = "WINPE";

    public string? FilesFolder { get; init; }

    public FilesMode FilesMode { get; init; } = FilesMode.None;

    /// <summary>Programas que aparecem no desktop/menu Iniciar do WinPE (copiados para Arquivos\Programas).</summary>
    public IReadOnlyList<ProgramEntry> Programs { get; init; } = [];

    /// <summary>
    /// Modo realmente usado: com programas e sem pasta de arquivos escolhida, os programas vão na ISO
    /// (não ocupam RAM); o usuário só controla o modo se escolheu um.
    /// </summary>
    public FilesMode EffectiveFilesMode => FilesMode == FilesMode.None && Programs.Count > 0 ? FilesMode.OnIso : FilesMode;

    /// <summary>Inicia direto, sem o "Pressione qualquer tecla para iniciar do CD" no UEFI.</summary>
    public bool BootWithoutPrompt { get; init; } = true;

    /// <summary>Valida as opções; devolve mensagens amigáveis (lista vazia = ok).</summary>
    public IReadOnlyList<string> Validate()
    {
        var errors = new List<string>();

        if (Source == WinPeSourceKind.BootWimFromIso)
        {
            if (string.IsNullOrWhiteSpace(SourceIsoPath) || !File.Exists(SourceIsoPath))
            {
                errors.Add("Selecione a ISO do Windows que contém o boot.wim.");
            }

            if (OptionalComponents.Count > 0)
            {
                errors.Add("Pacotes opcionais só funcionam com a fonte ADK; desmarque-os ou troque a fonte.");
            }
        }

        if (FilesMode != FilesMode.None && (string.IsNullOrWhiteSpace(FilesFolder) || !Directory.Exists(FilesFolder)))
        {
            errors.Add("A pasta de arquivos escolhida não existe.");
        }

        errors.AddRange(ProgramEntry.Validate(Programs));

        if (string.IsNullOrWhiteSpace(OutputIsoPath) || !OutputIsoPath.EndsWith(".iso", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Informe o caminho da ISO de saída (extensão .iso).");
        }

        if (ScratchSpaceMb is not (0 or 32 or 64 or 128 or 256 or 512))
        {
            errors.Add("O espaço temporário deve ser 32, 64, 128, 256 ou 512 MB.");
        }

        if (!System.Text.RegularExpressions.Regex.IsMatch(VolumeLabel ?? string.Empty, "^[A-Za-z0-9_]{1,32}$"))
        {
            errors.Add("O rótulo do volume deve ter 1 a 32 letras, números ou _.");
        }

        if (!string.IsNullOrEmpty(InputLocale) && !System.Text.RegularExpressions.Regex.IsMatch(InputLocale, "^[A-Za-z0-9:\\-]{2,32}$"))
        {
            errors.Add("Layout de teclado inválido (use, por exemplo, pt-BR).");
        }

        return errors;
    }
}
