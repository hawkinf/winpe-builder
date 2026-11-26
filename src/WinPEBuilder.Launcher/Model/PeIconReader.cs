#nullable disable
using System;
using System.Collections.Generic;
using System.IO;

namespace WinPEBuilder.Launcher.Model
{
    /// <summary>
    /// Extrai o ícone de um executável lendo os recursos do PE diretamente (RT_GROUP_ICON + RT_ICON) e montando um arquivo .ico.
    /// Não usa shell32/ExtractAssociatedIcon: dentro do WinPE essas APIs falham e o Windows devolve o ícone genérico.
    /// Só lê os trechos necessários do arquivo (um .exe "single-file" do .NET pode ter dezenas de MB).
    /// </summary>
    public static class PeIconReader
    {
        private const int ResourceTypeIcon = 3;
        private const int ResourceTypeGroupIcon = 14;
        private const int MaxIconBytes = 2 * 1024 * 1024;
        private const int MaxIconCount = 64;

        private sealed class Section
        {
            public uint VirtualAddress;
            public uint VirtualSize;
            public uint RawSize;
            public uint RawPointer;
        }

        private sealed class GroupEntry
        {
            public byte Width;
            public byte Height;
            public byte ColorCount;
            public ushort Planes;
            public ushort BitCount;
            public uint BytesInRes;
            public ushort Id;
        }

        /// <summary>
        /// Conteúdo de um arquivo .ico com TODAS as imagens do primeiro grupo de ícones do executável, ou null se não houver
        /// ícone (ou o arquivo não for um PE válido). Nunca lança exceção.
        /// </summary>
        public static byte[] ReadIcoFile(string path)
        {
            try
            {
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
                {
                    return Read(stream);
                }
            }
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is ArgumentException
                || ex is NotSupportedException || ex is InvalidDataException)
            {
                return null;
            }
        }

        public static byte[] Read(Stream stream)
        {
            var dos = ReadAt(stream, 0, 64);
            if (dos == null || dos[0] != (byte)'M' || dos[1] != (byte)'Z')
            {
                return null;
            }

            var peOffset = BitConverter.ToInt32(dos, 60);
            var pe = ReadAt(stream, peOffset, 24);
            if (pe == null || pe[0] != (byte)'P' || pe[1] != (byte)'E' || pe[2] != 0 || pe[3] != 0)
            {
                return null;
            }

            var sectionCount = BitConverter.ToUInt16(pe, 6);
            var optionalSize = BitConverter.ToUInt16(pe, 20);
            var optional = ReadAt(stream, peOffset + 24, Math.Min((int)optionalSize, 240));
            if (optional == null || optional.Length < 2)
            {
                return null;
            }

            var magic = BitConverter.ToUInt16(optional, 0);
            var directoriesAt = magic == 0x20B ? 112 : magic == 0x10B ? 96 : -1;
            if (directoriesAt < 0 || optional.Length < directoriesAt + 24)
            {
                return null;
            }

            var resourceRva = BitConverter.ToUInt32(optional, directoriesAt + 16);   // diretório 2 = recursos
            if (resourceRva == 0)
            {
                return null;
            }

            var sections = new List<Section>();
            var table = ReadAt(stream, peOffset + 24 + optionalSize, Math.Min((int)sectionCount, 96) * 40);
            if (table == null)
            {
                return null;
            }

            for (var i = 0; i + 40 <= table.Length; i += 40)
            {
                sections.Add(new Section
                {
                    VirtualSize = BitConverter.ToUInt32(table, i + 8),
                    VirtualAddress = BitConverter.ToUInt32(table, i + 12),
                    RawSize = BitConverter.ToUInt32(table, i + 16),
                    RawPointer = BitConverter.ToUInt32(table, i + 20),
                });
            }

            long rootOffset = RvaToOffset(sections, resourceRva);
            if (rootOffset < 0)
            {
                return null;
            }

            // nível 1: tipo do recurso
            long groupDirectory = FindSubdirectory(stream, rootOffset, rootOffset, ResourceTypeGroupIcon);
            long iconDirectory = FindSubdirectory(stream, rootOffset, rootOffset, ResourceTypeIcon);
            if (groupDirectory < 0 || iconDirectory < 0)
            {
                return null;
            }

            // nível 2 (primeiro grupo) → nível 3 (primeiro idioma) → dados
            var groupData = ReadFirstResourceData(stream, sections, rootOffset, groupDirectory);
            if (groupData == null || groupData.Length < 6)
            {
                return null;
            }

            var count = BitConverter.ToUInt16(groupData, 4);
            if (count == 0 || count > MaxIconCount || groupData.Length < 6 + (count * 14))
            {
                return null;
            }

            var entries = new List<GroupEntry>();
            for (var i = 0; i < count; i++)
            {
                var o = 6 + (i * 14);
                entries.Add(new GroupEntry
                {
                    Width = groupData[o],
                    Height = groupData[o + 1],
                    ColorCount = groupData[o + 2],
                    Planes = BitConverter.ToUInt16(groupData, o + 4),
                    BitCount = BitConverter.ToUInt16(groupData, o + 6),
                    BytesInRes = BitConverter.ToUInt32(groupData, o + 8),
                    Id = BitConverter.ToUInt16(groupData, o + 12),
                });
            }

            var images = new List<KeyValuePair<GroupEntry, byte[]>>();
            foreach (var entry in entries)
            {
                var imageDirectory = FindSubdirectory(stream, rootOffset, iconDirectory, entry.Id);
                if (imageDirectory < 0)
                {
                    continue;
                }

                var data = ReadLeafData(stream, sections, rootOffset, imageDirectory);
                if (data != null && data.Length > 0 && data.Length <= MaxIconBytes)
                {
                    images.Add(new KeyValuePair<GroupEntry, byte[]>(entry, data));
                }
            }

            return images.Count == 0 ? null : BuildIco(images);
        }

        /// <summary>Uma imagem dentro de um arquivo .ico.</summary>
        public sealed class IconImage
        {
            public int Width;
            public int Height;
            public int BitCount;
            public byte[] Data;

            /// <summary>true se a imagem é um PNG (ícones modernos, comuns nos de 256 px e nos de apps .NET).</summary>
            public bool IsPng
            {
                get
                {
                    return Data != null && Data.Length > 8 && Data[0] == 0x89 && Data[1] == (byte)'P' && Data[2] == (byte)'N' && Data[3] == (byte)'G';
                }
            }
        }

        /// <summary>Lista as imagens de um arquivo .ico (vazia se estiver corrompido).</summary>
        public static List<IconImage> ParseIco(byte[] ico)
        {
            var result = new List<IconImage>();
            if (ico == null || ico.Length < 6 || BitConverter.ToUInt16(ico, 2) != 1)
            {
                return result;
            }

            int count = BitConverter.ToUInt16(ico, 4);
            for (var i = 0; i < count && 6 + ((i + 1) * 16) <= ico.Length; i++)
            {
                var o = 6 + (i * 16);
                var size = BitConverter.ToUInt32(ico, o + 8);
                var offset = BitConverter.ToUInt32(ico, o + 12);
                if (size == 0 || (long)offset + size > ico.Length)
                {
                    continue;
                }

                var data = new byte[size];
                Buffer.BlockCopy(ico, (int)offset, data, 0, (int)size);
                result.Add(new IconImage
                {
                    Width = ico[o] == 0 ? 256 : ico[o],
                    Height = ico[o + 1] == 0 ? 256 : ico[o + 1],
                    BitCount = BitConverter.ToUInt16(ico, o + 6),
                    Data = data,
                });
            }

            return result;
        }

        /// <summary>
        /// Melhor imagem para exibir em <paramref name="size"/> px: a menor que seja pelo menos desse tamanho (reduzir fica melhor do
        /// que ampliar); se não houver, a maior. Em empate, a de mais bits por pixel.
        /// </summary>
        public static IconImage Pick(List<IconImage> images, int size)
        {
            IconImage best = null;
            foreach (var image in images)
            {
                if (best == null)
                {
                    best = image;
                    continue;
                }

                var imageBigEnough = image.Width >= size;
                var bestBigEnough = best.Width >= size;
                if (imageBigEnough && !bestBigEnough)
                {
                    best = image;
                }
                else if (imageBigEnough && bestBigEnough)
                {
                    if (image.Width < best.Width || (image.Width == best.Width && image.BitCount > best.BitCount))
                    {
                        best = image;
                    }
                }
                else if (!imageBigEnough && !bestBigEnough)
                {
                    if (image.Width > best.Width || (image.Width == best.Width && image.BitCount > best.BitCount))
                    {
                        best = image;
                    }
                }
            }

            return best;
        }

        /// <summary>Monta um arquivo .ico (ICONDIR + ICONDIRENTRY + imagens) com as imagens encontradas.</summary>
        private static byte[] BuildIco(List<KeyValuePair<GroupEntry, byte[]>> images)
        {
            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((ushort)0);
                w.Write((ushort)1);
                w.Write((ushort)images.Count);
                var offset = 6 + (16 * images.Count);
                foreach (var pair in images)
                {
                    w.Write(pair.Key.Width);
                    w.Write(pair.Key.Height);
                    w.Write(pair.Key.ColorCount);
                    w.Write((byte)0);
                    w.Write(pair.Key.Planes);
                    w.Write(pair.Key.BitCount);
                    w.Write((uint)pair.Value.Length);
                    w.Write((uint)offset);
                    offset += pair.Value.Length;
                }

                foreach (var pair in images)
                {
                    w.Write(pair.Value);
                }

                return ms.ToArray();
            }
        }

        // ---- diretório de recursos ----

        /// <summary>
        /// Procura, no diretório em <paramref name="directory"/>, a entrada de id <paramref name="id"/> e devolve o offset (no
        /// arquivo) do diretório filho; -1 se não achar. Entradas nomeadas são ignoradas (só ids numéricos).
        /// </summary>
        private static long FindSubdirectory(Stream stream, long rootOffset, long directory, int id)
        {
            var header = ReadAt(stream, directory, 16);
            if (header == null)
            {
                return -1;
            }

            int named = BitConverter.ToUInt16(header, 12);
            int ids = BitConverter.ToUInt16(header, 14);
            var entries = ReadAt(stream, directory + 16, Math.Min(named + ids, 4096) * 8);
            if (entries == null)
            {
                return -1;
            }

            for (var i = 0; i + 8 <= entries.Length; i += 8)
            {
                var name = BitConverter.ToUInt32(entries, i);
                var target = BitConverter.ToUInt32(entries, i + 4);
                var isNamed = (name & 0x80000000) != 0;
                if (isNamed || (name & 0xFFFF) != id)
                {
                    continue;
                }

                if ((target & 0x80000000) == 0)
                {
                    return -1;   // folha onde era esperado um diretório
                }

                return rootOffset + (target & 0x7FFFFFFF);
            }

            return -1;
        }

        /// <summary>Dados do primeiro recurso (primeira entrada, primeiro idioma) de um diretório de TIPO (nível 2).</summary>
        private static byte[] ReadFirstResourceData(Stream stream, List<Section> sections, long rootOffset, long typeDirectory)
        {
            var languageDirectory = FirstEntryTarget(stream, rootOffset, typeDirectory, true);
            return languageDirectory < 0 ? null : ReadLeafData(stream, sections, rootOffset, languageDirectory);
        }

        /// <summary>Dados do primeiro idioma de um diretório de IDIOMA (nível 3), cuja primeira entrada é a folha com RVA e tamanho.</summary>
        private static byte[] ReadLeafData(Stream stream, List<Section> sections, long rootOffset, long languageDirectory)
        {
            var dataEntryOffset = FirstEntryTarget(stream, rootOffset, languageDirectory, false);
            if (dataEntryOffset < 0)
            {
                return null;
            }

            var entry = ReadAt(stream, dataEntryOffset, 16);
            if (entry == null)
            {
                return null;
            }

            var rva = BitConverter.ToUInt32(entry, 0);
            var size = BitConverter.ToUInt32(entry, 4);
            if (size == 0 || size > MaxIconBytes)
            {
                return null;
            }

            var fileOffset = RvaToOffset(sections, rva);
            return fileOffset < 0 ? null : ReadAt(stream, fileOffset, (int)size);
        }

        /// <summary>Alvo da primeira entrada de um diretório (subdiretório ou, se <paramref name="expectDirectory"/> for false, a folha).</summary>
        private static long FirstEntryTarget(Stream stream, long rootOffset, long directory, bool expectDirectory)
        {
            var header = ReadAt(stream, directory, 16);
            if (header == null || BitConverter.ToUInt16(header, 12) + BitConverter.ToUInt16(header, 14) == 0)
            {
                return -1;
            }

            var first = ReadAt(stream, directory + 16, 8);
            if (first == null)
            {
                return -1;
            }

            var target = BitConverter.ToUInt32(first, 4);
            var isDirectory = (target & 0x80000000) != 0;
            if (isDirectory != expectDirectory)
            {
                return -1;
            }

            return rootOffset + (target & 0x7FFFFFFF);
        }

        private static long RvaToOffset(List<Section> sections, uint rva)
        {
            foreach (var s in sections)
            {
                var size = Math.Max(s.VirtualSize, s.RawSize);
                if (rva >= s.VirtualAddress && rva < s.VirtualAddress + size)
                {
                    return (long)s.RawPointer + (rva - s.VirtualAddress);
                }
            }

            return -1;
        }

        /// <summary>Lê exatamente <paramref name="count"/> bytes a partir de <paramref name="offset"/>; null se não couber no arquivo.</summary>
        private static byte[] ReadAt(Stream stream, long offset, int count)
        {
            if (offset < 0 || count < 0 || offset + count > stream.Length)
            {
                return null;
            }

            var buffer = new byte[count];
            stream.Position = offset;
            var read = 0;
            while (read < count)
            {
                var n = stream.Read(buffer, read, count - read);
                if (n <= 0)
                {
                    return null;
                }

                read += n;
            }

            return buffer;
        }
    }
}
