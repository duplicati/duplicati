#nullable enable

using System;
using System.IO;
using System.Linq;
using Duplicati.Server;
using NUnit.Framework;

namespace Duplicati.UnitTest;

[TestFixture]
public class QnapSharedFoldersTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp()
    {
        _root = Path.Combine(Path.GetTempPath(), "qnap-shares-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    [TearDown]
    public void TearDown()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    [Test]
    public void Enumerate_resolves_share_symlinks_to_volume_folders()
    {
        if (OperatingSystem.IsWindows())
            Assert.Ignore("The QTS share root is a Unix symlink layout");

        // Two data volumes, as on a two-volume NAS, with a placeholder
        // folder and a quota file like the ones QTS keeps next to them
        var vol1 = Directory.CreateDirectory(Path.Combine(_root, "CACHEDEV1_DATA")).FullName;
        var vol2 = Directory.CreateDirectory(Path.Combine(_root, "CACHEDEV2_DATA")).FullName;
        Directory.CreateDirectory(Path.Combine(vol1, "Public"));
        Directory.CreateDirectory(Path.Combine(vol1, "homes"));
        Directory.CreateDirectory(Path.Combine(vol2, "Veeambackup"));
        Directory.CreateDirectory(Path.Combine(_root, "HDA_DATA"));
        File.WriteAllText(Path.Combine(vol1, "aquota.user"), "");

        // QTS links shares with volume-relative targets; absolute ones must work too
        File.CreateSymbolicLink(Path.Combine(_root, "Public"), "CACHEDEV1_DATA/Public");
        File.CreateSymbolicLink(Path.Combine(_root, "homes"), "CACHEDEV1_DATA/homes");
        File.CreateSymbolicLink(Path.Combine(_root, "Veeambackup"), Path.Combine(vol2, "Veeambackup"));

        // Not shares: a dangling link, a hidden link, a link to a file
        File.CreateSymbolicLink(Path.Combine(_root, "Gone"), "CACHEDEV1_DATA/Gone");
        File.CreateSymbolicLink(Path.Combine(_root, ".hidden"), "CACHEDEV1_DATA/Public");
        File.CreateSymbolicLink(Path.Combine(_root, "quota"), "CACHEDEV1_DATA/aquota.user");

        var shares = QnapSharedFolders.Enumerate(_root).ToList();

        Assert.That(shares.Select(x => x.Name), Is.EqualTo(new[] { "homes", "Public", "Veeambackup" }));
        Assert.That(shares.Single(x => x.Name == "Public").Path, Is.EqualTo(Path.Combine(vol1, "Public")));
        Assert.That(shares.Single(x => x.Name == "homes").Path, Is.EqualTo(Path.Combine(vol1, "homes")));
        Assert.That(shares.Single(x => x.Name == "Veeambackup").Path, Is.EqualTo(Path.Combine(vol2, "Veeambackup")));
    }

    [Test]
    public void Enumerate_of_missing_root_is_empty()
    {
        Assert.That(QnapSharedFolders.Enumerate(Path.Combine(_root, "missing")), Is.Empty);
    }

    [Test]
    public void Token_wraps_the_share_name()
    {
        Assert.That(QnapSharedFolders.TokenFor("Public"), Is.EqualTo("%QNAP_SHARE_Public%"));
        Assert.That(QnapSharedFolders.TokenFor("My Share"), Is.EqualTo("%QNAP_SHARE_My Share%"));
    }
}
