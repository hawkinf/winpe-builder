using WinPEBuilder.Launcher.Model;

namespace WinPEBuilder.Tests;

/// <summary>
/// Leitura do ícone direto dos recursos do PE (sem APIs do shell, que falham no WinPE e fazem o launcher mostrar o ícone
/// genérico). O teste monta um executável mínimo com recursos RT_GROUP_ICON/RT_ICON.
/// </summary>
public sealed class PeIconReaderTests
{
    private static readonly byte[] PngSignature = [0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A];

    private sealed record Icon(int Width, int Height, int Bits, byte[] Data);

    private static byte[] Png(int marker, int length = 64)
    {
        var data = new byte[length];
        PngSignature.CopyTo(data, 0);
        data[10] = (byte)marker;
        return data;
    }

    private static byte[] Dib(int marker, int length = 80)
    {
        var data = new byte[length];
        BitConverter.GetBytes(40).CopyTo(data, 0);   // BITMAPINFOHEADER.biSize
        data[10] = (byte)marker;
        return data;
    }

    /// <summary>
    /// Executável PE32+ mínimo, só com a seção .rsrc: raiz → (RT_ICON, RT_GROUP_ICON) → id → idioma → folha de dados.
    /// </summary>
    private static byte[] BuildPe(IReadOnlyList<Icon> icons, bool withResources = true, uint sectionRva = 0x1000, bool pe32 = false)
    {
        var n = icons.Count;
        const int fileAlign = 0x200;

        // ---- árvore de recursos (offsets relativos ao início da seção) ----
        var dirAOff = 32;                       // tipo RT_ICON (a raiz ocupa os 32 primeiros bytes)
        var dirBOff = dirAOff + 16 + (8 * n);   // tipo RT_GROUP_ICON
        var langA0 = dirBOff + 24;
        var langBOff = langA0 + (24 * n);
        var dataA0 = langBOff + 24;
        var dataBOff = dataA0 + (16 * n);
        var payload = dataBOff + 16;
        var groupLen = 6 + (14 * n);
        var iconOffsets = new List<int>();
        var cursor = payload + groupLen;
        foreach (var icon in icons)
        {
            iconOffsets.Add(cursor);
            cursor += icon.Data.Length;
        }

        var rsrc = new MemoryStream();
        var w = new BinaryWriter(rsrc);

        void Header(int idEntries)
        {
            w.Write(0); w.Write(0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)0); w.Write((ushort)idEntries);
        }

        void Entry(uint id, int target, bool isDirectory)
        {
            w.Write(id);
            w.Write(isDirectory ? 0x80000000u | (uint)target : (uint)target);
        }

        Header(2);
        Entry(3, dirAOff, true);
        Entry(14, dirBOff, true);
        Header(n);
        for (var i = 0; i < n; i++)
        {
            Entry((uint)(i + 1), langA0 + (24 * i), true);
        }

        Header(1);
        Entry(1, langBOff, true);
        for (var i = 0; i < n; i++)
        {
            Header(1);
            Entry(0x409, dataA0 + (16 * i), false);
        }

        Header(1);
        Entry(0x409, dataBOff, false);
        for (var i = 0; i < n; i++)
        {
            w.Write(sectionRva + (uint)iconOffsets[i]); w.Write((uint)icons[i].Data.Length); w.Write(0u); w.Write(0u);
        }

        w.Write(sectionRva + (uint)payload); w.Write((uint)groupLen); w.Write(0u); w.Write(0u);

        // dados: grupo (GRPICONDIR) + imagens
        w.Write((ushort)0); w.Write((ushort)1); w.Write((ushort)n);
        for (var i = 0; i < n; i++)
        {
            var icon = icons[i];
            w.Write((byte)(icon.Width >= 256 ? 0 : icon.Width));
            w.Write((byte)(icon.Height >= 256 ? 0 : icon.Height));
            w.Write((byte)0); w.Write((byte)0);
            w.Write((ushort)1); w.Write((ushort)icon.Bits);
            w.Write((uint)icon.Data.Length);
            w.Write((ushort)(i + 1));
        }

        foreach (var icon in icons)
        {
            w.Write(icon.Data);
        }

        var rsrcBytes = rsrc.ToArray();
        if (!withResources)
        {
            rsrcBytes = new byte[16];
        }

        // ---- cabeçalhos PE ----
        var optionalSize = pe32 ? 224 : 240;
        var pe = new MemoryStream();
        var p = new BinaryWriter(pe);
        var dos = new byte[64];
        dos[0] = (byte)'M';
        dos[1] = (byte)'Z';
        BitConverter.GetBytes(0x40).CopyTo(dos, 60);
        p.Write(dos);
        p.Write(new byte[] { (byte)'P', (byte)'E', 0, 0 });
        p.Write((ushort)(pe32 ? 0x14C : 0x8664)); p.Write((ushort)1); p.Write(0); p.Write(0); p.Write(0);
        p.Write((ushort)optionalSize); p.Write((ushort)0x22);

        var optional = new byte[optionalSize];
        BitConverter.GetBytes((ushort)(pe32 ? 0x10B : 0x20B)).CopyTo(optional, 0);
        var directoriesAt = pe32 ? 96 : 112;
        BitConverter.GetBytes(withResources ? sectionRva : 0u).CopyTo(optional, directoriesAt + 16);
        BitConverter.GetBytes((uint)rsrcBytes.Length).CopyTo(optional, directoriesAt + 20);
        p.Write(optional);

        var rawPointer = (uint)(((int)pe.Length + 40 + fileAlign - 1) / fileAlign * fileAlign);
        p.Write(System.Text.Encoding.ASCII.GetBytes(".rsrc\0\0\0"));
        p.Write((uint)rsrcBytes.Length); p.Write(sectionRva); p.Write((uint)rsrcBytes.Length); p.Write(rawPointer);
        p.Write(0u); p.Write(0u); p.Write((ushort)0); p.Write((ushort)0); p.Write(0x40000040u);
        while (pe.Length < rawPointer)
        {
            p.Write((byte)0);
        }

        p.Write(rsrcBytes);
        return pe.ToArray();
    }

    private static byte[]? Read(byte[] pe) => PeIconReader.Read(new MemoryStream(pe));

    private static readonly Icon[] Three =
    [
        new(16, 16, 32, Png(1)),
        new(32, 32, 32, Png(2, 120)),
        new(256, 256, 32, Dib(3, 300)),
    ];

    // ---- leitura ----

    [Fact]
    public void Read_ReturnsAnIcoWithAllImages_ExactlyAsInTheResources()
    {
        var ico = Read(BuildPe(Three));

        Assert.NotNull(ico);
        var images = PeIconReader.ParseIco(ico);
        Assert.Equal(3, images.Count);
        Assert.Equal([16, 32, 256], images.Select(i => i.Width));
        Assert.Equal([16, 32, 256], images.Select(i => i.Height));
        for (var i = 0; i < 3; i++)
        {
            Assert.Equal(Three[i].Data, images[i].Data);
        }

        Assert.Equal([true, true, false], images.Select(i => i.IsPng));
    }

    [Fact]
    public void Read_WorksForPe32AndPe32Plus()
    {
        Assert.NotNull(Read(BuildPe(Three, pe32: true)));
        Assert.NotNull(Read(BuildPe(Three, pe32: false)));
    }

    [Fact]
    public void Read_HonorsTheSectionRvaAndFileAlignment()
    {
        var ico = Read(BuildPe(Three, sectionRva: 0x7E000));

        Assert.Equal(3, PeIconReader.ParseIco(ico).Count);
    }

    [Fact]
    public void Read_SingleImage()
    {
        var ico = Read(BuildPe([new Icon(48, 48, 32, Png(9))]));

        var only = Assert.Single(PeIconReader.ParseIco(ico));
        Assert.Equal(48, only.Width);
    }

    [Fact]
    public void Read_ProducesAValidIcoHeader()
    {
        var ico = Read(BuildPe(Three))!;

        Assert.Equal(0, BitConverter.ToUInt16(ico, 0));
        Assert.Equal(1, BitConverter.ToUInt16(ico, 2));   // tipo ícone
        Assert.Equal(3, BitConverter.ToUInt16(ico, 4));
        Assert.Equal(6 + (3 * 16), (int)BitConverter.ToUInt32(ico, 6 + 12)); // offset da 1a imagem logo após o diretório
    }

    // ---- sem ícone / arquivo inválido ----

    [Fact]
    public void Read_ExecutableWithoutIconResources_IsNull()
    {
        Assert.Null(Read(BuildPe(Three, withResources: false)));
    }

    [Fact]
    public void Read_NotAPeOrEmpty_IsNull()
    {
        Assert.Null(Read([]));
        Assert.Null(Read([(byte)'M', (byte)'Z']));
        Assert.Null(Read(System.Text.Encoding.ASCII.GetBytes("@echo off\r\necho oi\r\n")));
        var noSignature = BuildPe(Three);
        noSignature[0x40] = (byte)'X';
        Assert.Null(Read(noSignature));
    }

    [Fact]
    public void Read_NeverThrows_ForAnyTruncationOrCorruption()
    {
        var valid = BuildPe(Three);

        for (var length = 0; length < valid.Length; length += 7)
        {
            _ = Read(valid[..length]);   // não pode lançar exceção
        }

        var random = new Random(1234);
        for (var attempt = 0; attempt < 400; attempt++)
        {
            var broken = (byte[])valid.Clone();
            for (var k = 0; k < 6; k++)
            {
                broken[random.Next(broken.Length)] = (byte)random.Next(256);
            }

            _ = Read(broken);
        }
    }

    [Fact]
    public void ReadIcoFile_MissingFile_IsNull() =>
        Assert.Null(PeIconReader.ReadIcoFile(Path.Combine(Path.GetTempPath(), "nao-existe-" + Guid.NewGuid().ToString("N") + ".exe")));

    [Fact]
    public void ReadIcoFile_FromDisk_MatchesInMemory()
    {
        var path = Path.Combine(Path.GetTempPath(), "wpb-ico-" + Guid.NewGuid().ToString("N") + ".exe");
        try
        {
            File.WriteAllBytes(path, BuildPe(Three));

            Assert.Equal(Read(BuildPe(Three)), PeIconReader.ReadIcoFile(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadIcoFile_RealWindowsExecutable_HasUsableImages()
    {
        var cmd = Path.Combine(Environment.SystemDirectory, "cmd.exe");
        if (!File.Exists(cmd))
        {
            return;
        }

        var images = PeIconReader.ParseIco(PeIconReader.ReadIcoFile(cmd));

        Assert.NotEmpty(images);
        Assert.All(images, i => Assert.True(i.Width is >= 8 and <= 256 && i.Data.Length > 0));
    }

    // ---- escolha da melhor imagem ----

    private static List<PeIconReader.IconImage> Images(params (int Width, int Bits)[] items) =>
        [.. items.Select(i => new PeIconReader.IconImage { Width = i.Width, Height = i.Width, BitCount = i.Bits, Data = [1] })];

    [Theory]
    [InlineData(32, 32)]     // exata
    [InlineData(16, 16)]
    [InlineData(48, 48)]
    [InlineData(40, 48)]     // a menor que seja pelo menos 40
    [InlineData(64, 128)]
    [InlineData(200, 256)]
    [InlineData(512, 256)]   // nenhuma é grande o bastante: usa a maior
    [InlineData(1, 16)]
    public void Pick_PrefersTheSmallestImageThatIsBigEnough(int wanted, int expectedWidth)
    {
        var images = Images((256, 32), (16, 32), (48, 32), (32, 32), (128, 32));

        Assert.Equal(expectedWidth, PeIconReader.Pick(images, wanted)!.Width);
    }

    [Fact]
    public void Pick_TieGoesToTheHighestBitDepth_AndEmptyListIsNull()
    {
        var images = Images((32, 4), (32, 32), (32, 8));

        Assert.Equal(32, PeIconReader.Pick(images, 32)!.BitCount);
        Assert.Null(PeIconReader.Pick(Images(), 32));
        Assert.Empty(PeIconReader.ParseIco(null));
        Assert.Empty(PeIconReader.ParseIco([1, 2, 3]));
    }

    [Fact]
    public void ParseIco_SkipsEntriesPointingOutsideTheFile()
    {
        var ico = Read(BuildPe(Three))!;
        BitConverter.GetBytes(uint.MaxValue - 4).CopyTo(ico, 6 + 12);   // offset absurdo na 1a entrada

        var images = PeIconReader.ParseIco(ico);

        Assert.Equal(2, images.Count);
    }

    // ---- paridade com o arquivo de ícone do projeto, quando disponível ----

    [Fact]
    public void RealProjectIcon_RoundTripsThroughTheReader()
    {
        // o app.ico do próprio WinPE Builder é embutido no WinPEBuilder.exe (ApplicationIcon): lemos de volta e comparamos
        var exe = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WinPEBuilder.App", "bin", "Debug", "net10.0-windows", "WinPEBuilder.exe");
        var ico = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "WinPEBuilder.App", "app.ico");
        if (!File.Exists(exe) || !File.Exists(ico))
        {
            return;
        }

        var fromExe = PeIconReader.ParseIco(PeIconReader.ReadIcoFile(exe));
        var fromFile = PeIconReader.ParseIco(File.ReadAllBytes(ico));

        Assert.Equal(fromFile.Select(i => i.Width).OrderBy(x => x), fromExe.Select(i => i.Width).OrderBy(x => x));
    }
}
