using WinPEBuilder.Core;

namespace WinPEBuilder.Tests;

public class WinPeArchitectureTests
{
    [Theory]
    [InlineData(WinPeArchitecture.X64, "amd64")]
    [InlineData(WinPeArchitecture.X86, "x86")]
    public void ToAdkName_ReturnsCopypeName(WinPeArchitecture arch, string expected)
        => Assert.Equal(expected, arch.ToAdkName());

    [Fact]
    public void ToAdkName_UnknownValue_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => ((WinPeArchitecture)99).ToAdkName());
}
