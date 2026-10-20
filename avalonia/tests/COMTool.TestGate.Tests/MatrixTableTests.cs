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
    public void MD05_ParsesMultipleTablesAndIgnoresProse()
    {
        var path = TempFile(
            "# COMTool Avalonia 功能对齐清单\n" +
            "\n" +
            "Intro prose without a pipe.\n" +
            "\n" +
            "| ID | 状态 |\n" +
            "|---|---|\n" +
            "| UI-05 | 完成 |\n" +
            "\n" +
            "## 2. 终端\n" +
            "More prose.\n" +
            "\n" +
            "| Feature | Phase |\n" +
            "|---|---|\n" +
            "| UI-06 | verify |\n");
        var rows = MarkdownTable.Read(path);
        Assert.Equal(2, rows.Count);
        Assert.Equal("UI-05", rows[0]["ID"]);
        Assert.Equal("完成", rows[0]["状态"]);
        Assert.Equal("UI-06", rows[1]["Feature"]);
        Assert.Equal("verify", rows[1]["Phase"]);
        Assert.DoesNotContain("ID", rows[1].Keys);
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
    public void FM02_HeaderOnlyMatrixThrows()
    {
        var path = TempFile(
            "| Feature | Status | Automated | Phase | Platform | Command | Manual | Evidence |\n" +
            "|---|---|---|---|---|---|---|---|\n");
        var exception = Assert.Throws<InvalidDataException>(() => FeatureMatrix.Load(path));
        Assert.Contains(path, exception.Message);
        Assert.Contains("no data rows", exception.Message);
    }

    [Fact]
    public void FM03_MissingRequiredColumnThrows()
    {
        var path = TempFile(
            "| Feature | Status | Automated | Phase | Platform | Command | Manual |\n" +
            "|---|---|---|---|---|---|---|\n" +
            "| UI-05 | 完成 | UI05 | verify | all | dotnet test | - |\n");
        var exception = Assert.Throws<InvalidDataException>(() => FeatureMatrix.Load(path));
        Assert.Contains("Evidence", exception.Message);
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

    [Fact]
    public void PM02_IgnoresNonFeatureRows()
    {
        var path = TempFile(
            "| ID | 功能 | 优先级 | 状态 | 验收要点 |\n" +
            "|---|---|---|---|---|\n" +
            "| - | 说明 | P0 | 完成 | x |\n" +
            "| UI-05 | 标签页 | P0 | 完成 | x |\n");
        var statuses = ParityMatrix.Load(path);
        Assert.Single(statuses);
        Assert.Equal("完成", statuses["UI-05"]);
    }

    [Fact]
    public void PM03_DuplicateFeatureIdThrows()
    {
        var path = TempFile(
            "| ID | 功能 | 优先级 | 状态 | 验收要点 |\n" +
            "|---|---|---|---|---|\n" +
            "| UI-05 | 标签页 | P0 | 完成 | x |\n" +
            "| UI-05 | 重复 | P0 | 完成 | x |\n");
        var exception = Assert.Throws<InvalidDataException>(() => ParityMatrix.Load(path));
        Assert.Contains("UI-05", exception.Message);
        Assert.Contains("duplicate", exception.Message);
    }

    private static string TempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"comtool-testgate-{Guid.NewGuid():N}.md");
        File.WriteAllText(path, content);
        return path;
    }
}
