#nullable disable
using System;
using System.IO;

namespace WinPEBuilder.Launcher.Model
{
    /// <summary>Tipo de um executável pelo cabeçalho PE (arquitetura do código).</summary>
    public enum ExecutableKind
    {
        /// <summary>Não é um executável PE (script .cmd/.bat), arquivo ilegível ou inexistente.</summary>
        Unknown,

        X86,

        X64,

        Arm64,

        /// <summary>Outra arquitetura (ia64, arm32…).</summary>
        Other,
    }

    /// <summary>
    /// Lê só o cabeçalho PE (poucos bytes) para saber se um .exe é de 32 ou 64 bits. O WinPE de 64 bits NÃO executa programas de
    /// 32 bits (não tem WOW64 completo): sem esta checagem o usuário só vê "side-by-side configuration is incorrect".
    /// O mesmo arquivo é compilado no launcher (net48), no Core e nos testes.
    /// </summary>
    public static class PeInfo
    {
        public static ExecutableKind GetKind(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    var dos = new byte[64];
                    if (stream.Read(dos, 0, dos.Length) < dos.Length || dos[0] != (byte)'M' || dos[1] != (byte)'Z')
                    {
                        return ExecutableKind.Unknown;
                    }

                    var peOffset = BitConverter.ToInt32(dos, 60);
                    if (peOffset < 64 || peOffset > stream.Length - 6)
                    {
                        return ExecutableKind.Unknown;
                    }

                    stream.Position = peOffset;
                    var pe = new byte[6];
                    if (stream.Read(pe, 0, pe.Length) < pe.Length || pe[0] != (byte)'P' || pe[1] != (byte)'E' || pe[2] != 0 || pe[3] != 0)
                    {
                        return ExecutableKind.Unknown;
                    }

                    switch (BitConverter.ToUInt16(pe, 4))
                    {
                        case 0x014C:
                            return ExecutableKind.X86;
                        case 0x8664:
                            return ExecutableKind.X64;
                        case 0xAA64:
                            return ExecutableKind.Arm64;
                        default:
                            return ExecutableKind.Other;
                    }
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException || ex is NotSupportedException)
            {
                return ExecutableKind.Unknown;
            }
        }

        public static string Describe(ExecutableKind kind)
        {
            switch (kind)
            {
                case ExecutableKind.X86:
                    return "32 bits (x86)";
                case ExecutableKind.X64:
                    return "64 bits (x64)";
                case ExecutableKind.Arm64:
                    return "ARM64";
                default:
                    return "desconhecida";
            }
        }
    }
}
