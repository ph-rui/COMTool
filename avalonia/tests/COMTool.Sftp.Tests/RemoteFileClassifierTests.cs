using COMTool.Sftp;

namespace COMTool.Sftp.Tests;

public sealed class RemoteFileClassifierTests
{
    [Fact]
    public void ClassifiesImages()
    {
        string[] names =
        {
            "photo.jpg", "IMG_0001.JPG", "screen_00001.png",
            "top.bmp", "a.GIF", "logo.webp",
        };
        foreach (string name in names)
        {
            Assert.True(RemoteFileClassifier.Classify(name) == RemoteFileKind.Image, $"expected Image for {name}");
        }
    }

    [Fact]
    public void ClassifiesText()
    {
        string[] names =
        {
            "build.sh", "notes.txt", "main.C",
            "config.JSON", "app.log", "script.py",
        };
        foreach (string name in names)
        {
            Assert.True(RemoteFileClassifier.Classify(name) == RemoteFileKind.Text, $"expected Text for {name}");
        }
    }

    [Fact]
    public void ClassifiesExtensionlessTextByWholeName()
    {
        string[] names = { "Makefile", ".gitignore", "README" };
        foreach (string name in names)
        {
            Assert.True(RemoteFileClassifier.Classify(name) == RemoteFileKind.Text, $"expected Text for {name}");
        }
    }

    [Fact]
    public void ClassifiesUnknownAsOther()
    {
        string[] names = { "firmware.bin", "app.tar", "movie.mkv", "noext" };
        foreach (string name in names)
        {
            Assert.True(RemoteFileClassifier.Classify(name) == RemoteFileKind.Other, $"expected Other for {name}");
        }
    }

    [Fact]
    public void ClassificationIsCaseInsensitive()
    {
        Assert.Equal(RemoteFileKind.Image, RemoteFileClassifier.Classify("PHOTO.PNG"));
        Assert.Equal(RemoteFileKind.Text, RemoteFileClassifier.Classify("A.SH"));
    }
}
