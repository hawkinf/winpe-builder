#nullable disable
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace WinPEBuilder.Launcher.Model
{
    /// <summary>Conteúdo do programs.json gravado pelo WinPE Builder (versão 1).</summary>
    [DataContract]
    public class ManifestFile
    {
        [DataMember(Name = "version")]
        public int Version { get; set; }

        [DataMember(Name = "programs")]
        public List<ProgramItem> Programs { get; set; }
    }

    [DataContract]
    public class ProgramItem
    {
        [DataMember(Name = "id")]
        public string Id { get; set; }

        [DataMember(Name = "name")]
        public string Name { get; set; }

        [DataMember(Name = "category")]
        public string Category { get; set; }

        /// <summary>Caminho do executável relativo à pasta Arquivos (com "\").</summary>
        [DataMember(Name = "exe")]
        public string Exe { get; set; }

        [DataMember(Name = "arguments")]
        public string Arguments { get; set; }

        [DataMember(Name = "desktop")]
        public bool Desktop { get; set; }

        [DataMember(Name = "startMenu")]
        public bool StartMenu { get; set; }

        /// <summary>Nome da pasta da área de trabalho a que pertence (vazio = nenhuma). Campo opcional do manifesto v1.</summary>
        [DataMember(Name = "folder")]
        public string Folder { get; set; }
    }

    /// <summary>Lê e interpreta o programs.json. Sem dependências além do .NET Framework (roda no WinPE).</summary>
    public static class ManifestReader
    {
        public const string ManifestFileName = "programs.json";
        public const string FilesFolderName = "Arquivos";
        public const int SupportedVersion = 1;
        public const string DefaultCategory = "Programas";

        public static ManifestFile Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                throw new InvalidDataException("O programs.json está vazio.");
            }

            ManifestFile manifest;
            try
            {
                var serializer = new DataContractJsonSerializer(typeof(ManifestFile));
                using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(json.TrimStart('﻿'))))
                {
                    manifest = (ManifestFile)serializer.ReadObject(stream);
                }
            }
            catch (SerializationException ex)
            {
                throw new InvalidDataException("O programs.json está corrompido: " + ex.Message, ex);
            }

            if (manifest == null || manifest.Version < 1 || manifest.Version > SupportedVersion)
            {
                throw new InvalidDataException("Versão do programs.json não suportada: " + (manifest == null ? 0 : manifest.Version));
            }

            // Descarta entradas incompletas em vez de derrubar o launcher inteiro.
            var valid = (manifest.Programs ?? new List<ProgramItem>())
                .Where(p => p != null && !string.IsNullOrWhiteSpace(p.Name) && !string.IsNullOrWhiteSpace(p.Exe))
                .ToList();
            foreach (var p in valid)
            {
                if (string.IsNullOrWhiteSpace(p.Category))
                {
                    p.Category = DefaultCategory;
                }

                if (p.Arguments == null)
                {
                    p.Arguments = string.Empty;
                }

                p.Folder = (p.Folder ?? string.Empty).Trim();
            }

            manifest.Programs = valid;
            return manifest;
        }

        public static ManifestFile Load(string manifestPath)
        {
            return Parse(File.ReadAllText(manifestPath, Encoding.UTF8));
        }

        /// <summary>
        /// Caminho completo do executável, ou null se o caminho do manifesto tentar sair da pasta Arquivos
        /// (rooted, "..", etc.). O manifesto é só dado: nunca confiamos nele para apontar para fora.
        /// </summary>
        public static string ResolveExe(string filesRoot, ProgramItem program)
        {
            if (string.IsNullOrWhiteSpace(filesRoot) || program == null || string.IsNullOrWhiteSpace(program.Exe))
            {
                return null;
            }

            string root;
            string full;
            try
            {
                root = Path.GetFullPath(filesRoot).TrimEnd('\\') + "\\";
                full = Path.GetFullPath(Path.Combine(root, program.Exe));
            }
            catch (Exception ex) when (ex is ArgumentException || ex is NotSupportedException || ex is PathTooLongException)
            {
                return null;
            }

            return full.StartsWith(root, StringComparison.OrdinalIgnoreCase) ? full : null;
        }

        /// <summary>Programas do menu Iniciar agrupados por categoria (categorias e programas em ordem alfabética).</summary>
        public static List<KeyValuePair<string, List<ProgramItem>>> GroupForStartMenu(IEnumerable<ProgramItem> programs)
        {
            return programs
                .Where(p => p.StartMenu)
                .GroupBy(p => p.Category, StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new KeyValuePair<string, List<ProgramItem>>(
                    g.Key,
                    g.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList()))
                .ToList();
        }

        public static List<ProgramItem> ForDesktop(IEnumerable<ProgramItem> programs)
        {
            return programs.Where(p => p.Desktop).OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        }

        /// <summary>Pastas da área de trabalho: programas agrupados pelo campo folder (ordem alfabética; vazias não aparecem).</summary>
        public static List<KeyValuePair<string, List<ProgramItem>>> DesktopFolders(IEnumerable<ProgramItem> programs)
        {
            return programs
                .Where(p => !string.IsNullOrEmpty(p.Folder))
                .GroupBy(p => p.Folder, StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(g => g.Key, StringComparer.CurrentCultureIgnoreCase)
                .Select(g => new KeyValuePair<string, List<ProgramItem>>(
                    g.Key,
                    g.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList()))
                .ToList();
        }

        /// <summary>
        /// Acha a pasta Arquivos: primeiro a variável %ARQUIVOS% (definida pelo startnet.cmd), depois varre as unidades
        /// C..Z procurando Arquivos\programs.json. <paramref name="fileExists"/> existe para os testes.
        /// </summary>
        public static string FindFilesRoot(string arquivosEnv, Func<string, bool> fileExists)
        {
            if (!string.IsNullOrWhiteSpace(arquivosEnv) && fileExists(Path.Combine(arquivosEnv, ManifestFileName)))
            {
                return arquivosEnv;
            }

            for (var letter = 'C'; letter <= 'Z'; letter++)
            {
                var candidate = letter + ":\\" + FilesFolderName;
                if (fileExists(Path.Combine(candidate, ManifestFileName)))
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
