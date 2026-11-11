using COMTool.Sftp;

namespace COMTool.Sftp.Tests;

/// <summary>
/// r60: the local cache location of a remote file that is opened through
/// the built-in viewers or the system default program.
/// </summary>
public sealed class SftpPathCacheTests
{
    [Fact]
    public void KeepsThePathRelativeToTheRemoteHome()
    {
        var path = SftpPathCache.BuildCachePath("/cache", "/home/u", "/home/u/sub/a.txt");

        Assert.Equal(System.IO.Path.Combine("/cache", "sub", "a.txt"), path);
    }

    [Fact]
    public void FallsBackToTheFileNameWhenOutsideTheHome()
    {
        var path = SftpPathCache.BuildCachePath("/cache", "/home/u", "/other/deep/a.txt");

        Assert.Equal(System.IO.Path.Combine("/cache", "a.txt"), path);
    }

    [Fact]
    public void KeepsTheFullRelativePathWhenTheHomeIsTheRoot()
    {
        var path = SftpPathCache.BuildCachePath("/cache", "/", "/home/u/a.txt");

        Assert.Equal(System.IO.Path.Combine("/cache", "home", "u", "a.txt"), path);
    }

    [Fact]
    public void SanitizesPathUnsafeCharacters()
    {
        Assert.Equal("we_ird_na_me", SftpPathCache.SanitizeSegment("we:ird*na?me"));

        var path = SftpPathCache.BuildCachePath("/cache", "/home/u", "/home/u/bad:name.txt");

        Assert.Equal(System.IO.Path.Combine("/cache", "bad_name.txt"), path);
    }

    [Fact]
    public void SanitizesWindowsUnsafeCharacters()
    {
        Assert.Equal(
            "a_b_c_d_e_f",
            SftpPathCache.SanitizeSegment("a\\b:c*d?e\"f"));
    }
}
