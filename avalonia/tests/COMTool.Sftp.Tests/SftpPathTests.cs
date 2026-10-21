using COMTool.Sftp;

namespace COMTool.Sftp.Tests;

public sealed class SftpPathTests
{
    [Fact]
    public void CombineJoinsWithASingleSeparator()
    {
        Assert.Equal("/a/b/c", SftpPath.Combine("/a/b", "c"));
        Assert.Equal("/c", SftpPath.Combine("/", "c"));
        Assert.Equal("/a/b/c", SftpPath.Combine("/a/b/", "c"));
    }

    [Fact]
    public void GetParentWalksUpAndStopsAtRoot()
    {
        Assert.Equal("/a", SftpPath.GetParent("/a/b"));
        Assert.Equal("/", SftpPath.GetParent("/a"));
        Assert.Equal("/", SftpPath.GetParent("/"));
    }

    [Fact]
    public void GetFileNameReturnsTheLastSegment()
    {
        Assert.Equal("b", SftpPath.GetFileName("/a/b"));
        Assert.Equal(string.Empty, SftpPath.GetFileName("/"));
    }

    [Fact]
    public void NormalizeCollapsesAndResolvesDotSegments()
    {
        Assert.Equal("/a/c", SftpPath.Normalize("/a/./b/../c"));
        Assert.Equal("/a/b", SftpPath.Normalize("//a///b"));
        Assert.Equal("/", SftpPath.Normalize("/"));
    }

    [Fact]
    public void NormalizeCannotEscapeTheRoot()
    {
        Assert.Equal("/x", SftpPath.Normalize("/../x"));
        Assert.Equal("/", SftpPath.Normalize("/a/../.."));
    }

    [Fact]
    public void IsRootOnlyMatchesTheRootPath()
    {
        Assert.True(SftpPath.IsRoot("/"));
        Assert.False(SftpPath.IsRoot("/a"));
    }
}
