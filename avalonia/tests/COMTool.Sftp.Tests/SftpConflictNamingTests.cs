using COMTool.Sftp;

namespace COMTool.Sftp.Tests;

public sealed class SftpConflictNamingTests
{
    [Fact]
    public void SuggestsTheParenthesisedIndexBeforeTheExtension()
    {
        Assert.Equal("a (1).txt", SftpConflictNaming.Suggest("a.txt", 1));
        Assert.Equal("a (2).txt", SftpConflictNaming.Suggest("a.txt", 2));
    }

    [Fact]
    public void SuggestsAParenthesisedNameWhenThereIsNoExtension()
    {
        Assert.Equal("a (1)", SftpConflictNaming.Suggest("a", 1));
    }

    [Fact]
    public void UsesOnlyTheLastExtension()
    {
        Assert.Equal("a.tar (1).gz", SftpConflictNaming.Suggest("a.tar.gz", 1));
    }
}
