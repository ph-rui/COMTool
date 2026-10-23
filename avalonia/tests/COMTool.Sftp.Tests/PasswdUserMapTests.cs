using COMTool.Sftp;

namespace COMTool.Sftp.Tests;

public sealed class PasswdUserMapTests
{
    [Fact]
    public void ParseReadsNamesAndSkipsMalformedLines()
    {
        var content =
            "root:x:0:0:root:/root:/bin/bash\r\n"
            + "zlgmcu:x:1000:1000::/home/zlgmcu:/bin/bash\n"
            + "broken-line\n"
            + "nobody:x:65534:65534:nobody:/nonexistent:/usr/sbin/nologin\n"
            + "# a comment:x:1:1::/:/bin/false\n"
            + "duplicate:x:1000:1000::/:/bin/false\n";

        var names = PasswdUserMap.Parse(content);

        Assert.Equal("root", names[0]);
        Assert.Equal("zlgmcu", names[1000]);
        Assert.Equal("nobody", names[65534]);
        Assert.False(names.ContainsKey(1));
        Assert.Equal(3, names.Count);
    }

    [Fact]
    public void ParseOfAnEmptyOrMissingFileIsEmpty()
    {
        Assert.Empty(PasswdUserMap.Parse(null));
        Assert.Empty(PasswdUserMap.Parse(""));
    }
}
