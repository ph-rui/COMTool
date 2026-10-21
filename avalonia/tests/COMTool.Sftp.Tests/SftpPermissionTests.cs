using COMTool.Sftp;

namespace COMTool.Sftp.Tests;

public sealed class SftpPermissionTests
{
    // C# has no octal integer literals, so the modes are written the way
    // they appear on the shell and converted here.
    private static int Mode(string octalDigits) => Convert.ToInt32(octalDigits, 8);

    [Fact]
    public void OctalRoundTrips()
    {
        foreach (var digits in new[] { "000", "644", "755", "600", "777" })
        {
            var mode = Mode(digits);
            Assert.Equal(mode, SftpPermission.FromOctal(mode).ToOctal());
        }
    }

    [Fact]
    public void RwxStringMatchesTheOctalMode()
    {
        Assert.Equal("rw-r--r--", SftpPermission.FromOctal(Mode("644")).ToRwxString());
        Assert.Equal("rwxr-xr-x", SftpPermission.FromOctal(Mode("755")).ToRwxString());
    }

    [Fact]
    public void ExecuteIsIndependentOfRead()
    {
        // 0111 is execute only: the read bit is not implied. The three
        // characters of a group are always read, write, execute.
        Assert.Equal("--x--x--x", SftpPermission.FromOctal(Mode("111")).ToRwxString());
        Assert.Equal(Mode("111"), SftpPermission.FromOctal(Mode("111")).ToOctal());
        Assert.True(SftpPermission.FromOctal(Mode("111")).OwnerExecute);
        Assert.False(SftpPermission.FromOctal(Mode("111")).OwnerRead);
    }

    [Fact]
    public void RwxStringRoundTrips()
    {
        Assert.Equal(Mode("644"), SftpPermission.FromRwxString("rw-r--r--").ToOctal());
        Assert.Equal(Mode("111"), SftpPermission.FromRwxString("--x--x--x").ToOctal());
    }

    [Fact]
    public void OctalTextRoundTrips()
    {
        Assert.Equal("644", SftpPermission.FromOctal(Mode("644")).ToOctalText());
        Assert.Equal("000", SftpPermission.FromOctal(Mode("000")).ToOctalText());
        Assert.Equal(Mode("640"), SftpPermission.FromOctalText("640").ToOctal());
        Assert.Equal("640", SftpPermission.FromOctalText("640").ToOctalText());
    }
}
