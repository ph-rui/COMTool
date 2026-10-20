namespace COMTool.TestGate.Tests;

public sealed class MatrixTableTests
{
    [Fact]
    public void MD01_ParsesHeaderAndRows()
    {
        var path = TempFile("| Feature | Status |\n|---|---|\n| UI-05 | 完成 |\n");
        var rows = MarkdownTable.Read(path);
        Assert.Single(rows);
        Assert.Equal("UI-05", rows[0]["Feature"]);
        Assert.Equal("完成", rows[0]["Status"]);
    }

    [Fact]
    public void MD02_IgnoresSeparatorAndBlankLines()
    {
        var path = TempFile("| Feature | Status |\n| --- | --- |\n\n| UI-05 | 完成 |\n");
        Assert.Single(MarkdownTable.Read(path));
    }

    [Fact]
    public void MD03_StripsWhitespaceAroundCells()
    {
        var path = TempFile("|Feature|Status|\n|---|---|\n|  UI-05  |  完成  |\n");
        var row = MarkdownTable.Read(path).Single();
        Assert.Equal("UI-05", row["Feature"]);
        Assert.Equal("完成", row["Status"]);
    }

    [Fact]
    public void MD04_ThrowsOnColumnCountMismatch()
    {
        var path = TempFile("| Feature | Status |\n|---|---|\n| UI-05 |\n");
        var exception = Assert.Throws<InvalidDataException>(() => MarkdownTable.Read(path));
        Assert.Contains(path, exception.Message);
        Assert.Contains("line 3", exception.Message);
    }

    [Fact]
    public void FM01_ParsesCommaSeparatedIdsAndDash()
    {
        var path = TempFile(
            "| Feature | Status | Automated | Phase | Platform | Command | Manual | Evidence |\n" +
            "|---|---|---|---|---|---|---|---|\n" +
            "| UI-05 | 完成 | UI05_A, UI05_B | verify | all | dotnet test | - | - |\n");
        var row = FeatureMatrix.Load(path).Single();
        Assert.Equal(new[] { "UI05_A", "UI05_B" }, row.Automated);
        Assert.Empty(row.Manual);
    }

    [Fact]
    public void PM01_ParsesParityStatusRows()
    {
        var path = TempFile(
            "| ID | 功能 | 优先级 | 状态 | 验收要点 |\n" +
            "|---|---|---|---|---|\n" +
            "| UI-05 | 标签页 | P0 | 完成 | x |\n" +
            "| UI-15 | 窗口尺寸 | P1 | 完成（Avalonia 增强） | x |\n" +
            "| UI-14 | 高 DPI | P1 | 部分（Windows 待测） | x |\n");
        var statuses = ParityMatrix.Load(path);
        Assert.Equal("完成", statuses["UI-05"]);
        Assert.Equal("完成", statuses["UI-15"]);
        Assert.Equal("部分", statuses["UI-14"]);
        Assert.False(statuses.ContainsKey("ID"));
    }

    private static string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"comtool-testgate-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, content);
        return path;
    }
}
