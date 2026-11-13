// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.

#nullable enable

using System.Collections.Generic;
using System.Linq;
using Duplicati.Proprietary.GoogleWorkspace;
using Duplicati.Proprietary.GoogleWorkspace.SourceItems;
using Google.Apis.Admin.Directory.directory_v1.Data;
using NUnit.Framework;

namespace Duplicati.UnitTest.GoogleWorkspace;

/// <summary>
/// The placement of user folders by organizational unit
/// </summary>
public class GoogleOrgUnitTests
{
    private static OrgUnit Unit(string id, string path, string? parent)
        => new() { OrgUnitId = "id:" + id, OrgUnitPath = path, Name = path.Split('/').Last(), ParentOrgUnitPath = parent };

    private static User UserIn(string email, string? orgUnitPath)
        => new() { Id = "id-" + email, PrimaryEmail = email, OrgUnitPath = orgUnitPath };

    [Test]
    [Category("GoogleWorkspace")]
    public void BuildNestsUnitsAndPlacesUsersInTheNearestListedUnit()
    {
        // Listed child before parent, to show the order of the list does not matter
        var units = new[] { Unit("east", "/Sales/East", "/Sales"), Unit("sales", "/Sales", "/") };
        var root = OrgUnitNode.Build(units,
        [
            UserIn("root@x.test", "/"),
            UserIn("none@x.test", null),
            UserIn("east@x.test", "/sales/east"),
            UserIn("new@x.test", "/Sales/East/New"),
            UserIn("gone@x.test", "/Gone"),
        ], []);

        Assert.That(root.Children.Select(x => x.Id), Is.EqualTo(new[] { "sales" }), "The id prefix is removed from the folder name");
        var sales = root.Children.Single();
        Assert.That(sales.Name, Is.EqualTo("Sales"));
        var east = sales.Children.Single();
        Assert.That(east.Id, Is.EqualTo("east"));

        Assert.That(root.Users.Select(x => x.PrimaryEmail), Is.EquivalentTo(new[] { "root@x.test", "none@x.test", "gone@x.test" }), "Users of the root or of an unknown top-level unit are placed in the root");
        Assert.That(east.Users.Select(x => x.PrimaryEmail), Is.EquivalentTo(new[] { "east@x.test", "new@x.test" }), "Paths match without regard to case, and an unknown unit falls back to its nearest listed ancestor");
        Assert.That(sales.Users, Is.Empty);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public void BuildPlacesSharedDrivesByUnitId()
    {
        var units = new[] { Unit("sales", "/Sales", "/"), Unit("east", "/Sales/East", "/Sales") };
        var root = OrgUnitNode.Build(units, [],
        [
            new Google.Apis.Drive.v3.Data.Drive { Id = "d-east", OrgUnitId = "east" },
            new Google.Apis.Drive.v3.Data.Drive { Id = "d-prefixed", OrgUnitId = "id:sales" },
            new Google.Apis.Drive.v3.Data.Drive { Id = "d-none" },
            new Google.Apis.Drive.v3.Data.Drive { Id = "d-unknown", OrgUnitId = "gone" },
        ]);

        var sales = root.Children.Single();
        Assert.That(sales.Children.Single().SharedDrives.Select(x => x.Id), Is.EqualTo(new[] { "d-east" }));
        Assert.That(sales.SharedDrives.Select(x => x.Id), Is.EqualTo(new[] { "d-prefixed" }), "An id is matched with or without the prefix");
        Assert.That(root.SharedDrives.Select(x => x.Id), Is.EquivalentTo(new[] { "d-none", "d-unknown" }), "A drive without a known unit is placed in the root");
        Assert.That(root.Users, Is.Empty);
    }

    [Test]
    [Category("GoogleWorkspace")]
    public void ChangeTrackingScopeDependsOnTheUnit()
    {
        var root = OrgUnitNode.Build([Unit("a", "/A", "/"), Unit("b", "/B", "/")], [], []);
        var scopes = new[] { root }.Concat(root.Children).Select(x => x.ChangeTrackingUserScope("u@x.test")).ToList();
        Assert.That(scopes.Distinct().Count(), Is.EqualTo(3));
        Assert.That(scopes, Has.None.EqualTo("u@x.test"), "The scope never equals the one used without organizational units");

        var driveScopes = new[] { root }.Concat(root.Children).Select(x => x.ChangeTrackingSharedDriveScope()).ToList();
        Assert.That(driveScopes.Distinct().Count(), Is.EqualTo(3));
        Assert.That(driveScopes, Has.None.Null.And.None.Empty, "The scope never equals the one used without organizational units");
    }

    [Test]
    [Category("GoogleWorkspace")]
    public void OrgUnitLayoutForcesUserIds()
    {
        var options = OptionsHelper.ParseOptions(new Dictionary<string, string?>
        {
            [OptionsHelper.GOOGLE_USE_OU_PATHS_OPTION] = "true",
            [OptionsHelper.GOOGLE_USE_USER_IDS_IN_PATHS_OPTION] = "false",
            ["machine-id"] = "test-machine",
        });
        Assert.That(options.UseOuPaths, Is.True);
        Assert.That(options.UseUserIdsInPaths, Is.True);

        var plain = OptionsHelper.ParseOptions(new Dictionary<string, string?> { ["machine-id"] = "test-machine" });
        Assert.That(plain.UseOuPaths, Is.False);
        Assert.That(plain.UseUserIdsInPaths, Is.False);
    }
}
