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
using Duplicati.Library.Interface;
using Duplicati.Library.Utility.Options;
using NUnit.Framework;

namespace Duplicati.UnitTest;

/// <summary>
/// Tests for <see cref="AuthOptionsHelper"/>, which resolves the credentials every
/// backend uses from the backend URL and the option dictionary. The precedence rules
/// are not obvious, so they are pinned here.
/// </summary>
[TestFixture]
public class AuthOptionsHelperTests
{
    private const string UsernameOption = AuthOptionsHelper.AuthUsernameOption;
    private const string PasswordOption = AuthOptionsHelper.AuthPasswordOption;
    private const string AliasUsernameOption = "api-id";
    private const string AliasPasswordOption = "api-key";
    private const string Prefix = "custom-";

    private static Dictionary<string, string?> Opts(params (string Key, string? Value)[] entries)
    {
        var result = new Dictionary<string, string?>();
        foreach (var (key, value) in entries)
            result[key] = value;
        return result;
    }

    [Test]
    [Category("Utility")]
    public void UrlCredentialsWinOverOptions()
    {
        var result = AuthOptionsHelper.Parse(
            Opts((UsernameOption, "option-user"), (PasswordOption, "option-pass")),
            "url-user", "url-pass");

        Assert.That(result.Username, Is.EqualTo("url-user"));
        Assert.That(result.Password, Is.EqualTo("url-pass"));
    }

    [Test]
    [Category("Utility")]
    public void UrlUsernameIsCombinedWithTheOptionPassword()
    {
        var result = AuthOptionsHelper.Parse(
            Opts((UsernameOption, "option-user"), (PasswordOption, "option-pass")),
            "url-user", null);

        Assert.That(result.Username, Is.EqualTo("url-user"), "The URL username wins");
        Assert.That(result.Password, Is.EqualTo("option-pass"), "The password falls back to the option");
    }

    [Test]
    [Category("Utility")]
    public void UrlUsernameWithoutAnyPasswordLeavesThePasswordUnset()
    {
        var result = AuthOptionsHelper.Parse(Opts(), "url-user", null);

        Assert.That(result.Username, Is.EqualTo("url-user"));
        Assert.That(result.Password, Is.Null);
    }

    [Test]
    [Category("Utility")]
    public void OptionsAreUsedWhenTheUrlHasNoUsername()
    {
        var result = AuthOptionsHelper.Parse(
            Opts((UsernameOption, "option-user"), (PasswordOption, "option-pass")),
            null, null);

        Assert.That(result.Username, Is.EqualTo("option-user"));
        Assert.That(result.Password, Is.EqualTo("option-pass"));
    }

    [Test]
    [Category("Utility")]
    public void OptionPasswordIsIgnoredWithoutAUsername()
    {
        var result = AuthOptionsHelper.Parse(Opts((PasswordOption, "option-pass")), null, null);

        Assert.That(result.Username, Is.Null);
        Assert.That(result.Password, Is.Null, "A password without a username is not returned");
    }

    [Test]
    [Category("Utility")]
    public void OptionUsernameWithoutAPasswordLeavesThePasswordUnset()
    {
        var result = AuthOptionsHelper.Parse(Opts((UsernameOption, "option-user")), null, null);

        Assert.That(result.Username, Is.EqualTo("option-user"));
        Assert.That(result.Password, Is.Null);
    }

    [Test]
    [Category("Utility")]
    public void NothingSetGivesNoCredentials()
    {
        var result = AuthOptionsHelper.Parse(Opts(), null, null);

        Assert.That(result.Username, Is.Null);
        Assert.That(result.Password, Is.Null);
        Assert.That(result.HasUsername, Is.False);
        Assert.That(result.HasPassword, Is.False);
    }

    [Test]
    [Category("Utility")]
    public void ThePrefixSelectsTheOptionNames()
    {
        var options = Opts(
            (UsernameOption, "unprefixed-user"),
            (PasswordOption, "unprefixed-pass"),
            ($"{Prefix}{UsernameOption}", "prefixed-user"),
            ($"{Prefix}{PasswordOption}", "prefixed-pass"));

        var result = AuthOptionsHelper.Parse(options, null, null, Prefix);

        Assert.That(result.Username, Is.EqualTo("prefixed-user"));
        Assert.That(result.Password, Is.EqualTo("prefixed-pass"));
    }

    [Test]
    [Category("Utility")]
    public void ThePrefixedLookupIgnoresTheUnprefixedOptions()
    {
        var result = AuthOptionsHelper.Parse(
            Opts((UsernameOption, "unprefixed-user"), (PasswordOption, "unprefixed-pass")),
            null, null,
            Prefix);

        Assert.That(result.Username, Is.Null);
        Assert.That(result.Password, Is.Null);
    }

    [Test]
    [Category("Utility")]
    public void AliasOptionsWinOverTheUrl()
    {
        var result = AuthOptionsHelper.ParseWithAlias(
            Opts((AliasUsernameOption, "alias-user"), (AliasPasswordOption, "alias-pass")),
            "url-user", "url-pass",
            AliasUsernameOption,
            AliasPasswordOption);

        Assert.That(result.Username, Is.EqualTo("alias-user"), "The backend specific option name takes precedence");
        Assert.That(result.Password, Is.EqualTo("alias-pass"));
    }

    [Test]
    [Category("Utility")]
    public void WithoutAliasOptionsTheUrlIsUsed()
    {
        var result = AuthOptionsHelper.ParseWithAlias(
            Opts(),
            "url-user", "url-pass",
            AliasUsernameOption,
            AliasPasswordOption);

        Assert.That(result.Username, Is.EqualTo("url-user"));
        Assert.That(result.Password, Is.EqualTo("url-pass"));
    }

    [Test]
    [Category("Utility")]
    public void WithoutAliasOptionsTheGenericOptionsAreUsed()
    {
        var result = AuthOptionsHelper.ParseWithAlias(
            Opts((UsernameOption, "option-user"), (PasswordOption, "option-pass")),
            null, null,
            AliasUsernameOption,
            AliasPasswordOption);

        Assert.That(result.Username, Is.EqualTo("option-user"));
        Assert.That(result.Password, Is.EqualTo("option-pass"));
    }

    [Test]
    [Category("Utility")]
    public void TheAliasUsernameCombinesWithTheUrlPassword()
    {
        var result = AuthOptionsHelper.ParseWithAlias(
            Opts((AliasUsernameOption, "alias-user")),
            "url-user", "url-pass",
            AliasUsernameOption,
            AliasPasswordOption);

        Assert.That(result.Username, Is.EqualTo("alias-user"));
        Assert.That(result.Password, Is.EqualTo("url-pass"));
    }

    [Test]
    [Category("Utility")]
    public void GetOptionsUsesThePrefix()
    {
        var arguments = AuthOptionsHelper.GetOptions(Prefix);

        Assert.That(arguments.Length, Is.EqualTo(2));
        Assert.That(arguments[0].Name, Is.EqualTo($"{Prefix}{UsernameOption}"));
        Assert.That(arguments[1].Name, Is.EqualTo($"{Prefix}{PasswordOption}"));
        Assert.That(arguments[1].Type, Is.EqualTo(CommandLineArgument.ArgumentType.Password));
    }

    [TestCase("user", "pass", true)]
    [TestCase("user", null, false)]
    [TestCase(null, "pass", false)]
    [TestCase(null, null, false)]
    [TestCase(" ", "pass", false)]
    [TestCase("user", " ", false)]
    [Category("Utility")]
    public void IsValidRequiresBothValues(string? username, string? password, bool expected)
    {
        Assert.That(new AuthOptionsHelper.AuthOptions(username, password).IsValid(), Is.EqualTo(expected));
    }

    [Test]
    [Category("Utility")]
    public void RequireCredentialsReturnsTheInstanceWhenValid()
    {
        var auth = new AuthOptionsHelper.AuthOptions("user", "pass");

        Assert.That(auth.RequireCredentials(), Is.SameAs(auth));
    }

    [Test]
    [Category("Utility")]
    public void RequireCredentialsThrowsWhenIncomplete()
    {
        var ex = Assert.Throws<UserInformationException>(
            () => new AuthOptionsHelper.AuthOptions("user", null).RequireCredentials());

        Assert.That(ex!.HelpID, Is.EqualTo("UsernameAndPasswordRequired"));
    }

    [Test]
    [Category("Utility")]
    public void GetCredentialsReturnsBothValues()
    {
        var (username, password) = new AuthOptionsHelper.AuthOptions("user", "pass").GetCredentials();

        Assert.That(username, Is.EqualTo("user"));
        Assert.That(password, Is.EqualTo("pass"));
    }

    [Test]
    [Category("Utility")]
    public void GetCredentialsThrowsWhenIncomplete()
    {
        Assert.Throws<UserInformationException>(
            () => new AuthOptionsHelper.AuthOptions(null, "pass").GetCredentials());
    }
}
