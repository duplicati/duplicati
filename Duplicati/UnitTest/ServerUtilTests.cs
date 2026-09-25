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
using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Duplicati.Library.Encryption;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;
using ServerUtilProgram = Duplicati.CommandLine.ServerUtil.Program;

namespace Duplicati.UnitTest
{
    /// <summary>
    /// Tests that the System.CommandLine parsers used by the ServerUtil tool
    /// are wired up correctly. These tests exist because a migration to a new
    /// System.CommandLine release caused the tool to silently do nothing
    /// (no output, exit code 1) for all commands.
    /// </summary>
    [TestFixture]
    [Category("ServerUtil")]
    [NonParallelizable] // Tests redirect Console.Out and use a shared OutputInterceptorBinder singleton
    public class ServerUtilTests
    {
        /// <summary>
        /// Valid argument lists for each command that must parse without errors.
        /// </summary>
        private static readonly string[][] ValidCommandLines =
        {
            ["pause"],
            ["pause", "10m"],
            ["resume"],
            ["delete", "1"],
            ["delete", "my-backup", "--delete-remote-files", "--delete-local-db", "--force", "--quiet"],
            ["list-backups"],
            ["list-backups", "--detailed"],
            ["run", "1"],
            ["run", "my-backup", "--wait", "--skip-queue", "--poll-interval", "10", "--quiet"],
            ["login"],
            ["change-password"],
            ["change-password", "new-secret"],
            ["logout"],
            ["import", "somefile.json"],
            ["import", "somefile.json", "passphrase", "--import-metadata", "--backup-passphrase", "pw", "--backup-url", "file:///tmp"],
            ["export", "all"],
            ["export", "1", "2", "--encryption-passphrase", "pw", "--export-passwords", "--overwrite", "--unencrypted", "--destination", "/tmp"],
            ["health"],
            ["issue-forever-token"],
            ["status"],
        };

        /// <summary>
        /// Global options that must parse on every command.
        /// </summary>
        private static readonly string[] GlobalOptions =
        {
            "--password", "secret",
            "--hosturl", "http://localhost:8200/",
            "--settings-file", "some-settings.json",
            "--insecure",
            "--settings-encryption-key", "encryption-key",
            "--secret-provider-cache", "None",
            "--secret-provider-pattern", "$",
            "--host-cert", "abc123",
            "--ignore-revocation-failure",
            "--json",
        };

        [Test]
        public void AllCommandsParseWithoutErrors()
        {
            var rootCmd = ServerUtilProgram.CreateRootCommand();
            foreach (var args in ValidCommandLines)
            {
                var parseResult = rootCmd.Parse(args);
                Assert.AreEqual(0, parseResult.Errors.Count,
                    $"Parse errors for '{string.Join(" ", args)}': {string.Join("; ", parseResult.Errors.Select(e => e.Message))}");
            }
        }

        [Test]
        public void AllCommandsParseWithoutErrorsWithGlobalOptions()
        {
            var rootCmd = ServerUtilProgram.CreateRootCommand();
            foreach (var args in ValidCommandLines)
            {
                var fullArgs = args.Concat(GlobalOptions).ToArray();
                var parseResult = rootCmd.Parse(fullArgs);
                Assert.AreEqual(0, parseResult.Errors.Count,
                    $"Parse errors for '{string.Join(" ", fullArgs)}': {string.Join("; ", parseResult.Errors.Select(e => e.Message))}");
            }
        }

        [Test]
        public void AllCommandsAreRegistered()
        {
            var rootCmd = ServerUtilProgram.CreateRootCommand();
            var expected = new[]
            {
                "pause", "resume", "delete", "list-backups", "run", "login",
                "change-password", "logout", "import", "export", "health",
                "issue-forever-token", "status"
            };

            var actual = rootCmd.Subcommands.Select(c => c.Name).ToArray();
            foreach (var name in expected)
                Assert.Contains(name, actual, $"Command '{name}' is not registered on the root command");
        }

        [Test]
        public void RequiredArgumentsAreEnforced()
        {
            var rootCmd = ServerUtilProgram.CreateRootCommand();

            // Commands with required arguments must fail parsing when the argument is missing
            string[][] invalidCommandLines =
            {
                ["delete"],
                ["run"],
                ["import"],
                ["export"],
            };

            foreach (var args in invalidCommandLines)
            {
                var parseResult = rootCmd.Parse(args);
                Assert.Greater(parseResult.Errors.Count, 0,
                    $"Expected parse errors for '{string.Join(" ", args)}' but there were none");
            }
        }

        [Test]
        public void UnknownOptionsAreRejected()
        {
            var rootCmd = ServerUtilProgram.CreateRootCommand();
            var parseResult = rootCmd.Parse(["list-backups", "--no-such-option"]);
            Assert.Greater(parseResult.Errors.Count, 0, "Unknown option did not produce a parse error");
        }

        [Test]
        public void UnknownCommandIsRejected()
        {
            var rootCmd = ServerUtilProgram.CreateRootCommand();
            var parseResult = rootCmd.Parse(["no-such-command"]);
            Assert.Greater(parseResult.Errors.Count, 0, "Unknown command did not produce a parse error");
        }

        [Test]
        public void GlobalOptionsBindToSettings()
        {
            var rootCmd = ServerUtilProgram.CreateRootCommand();
            using var settingsFile = new Library.Utility.TempFile();
            File.WriteAllText(settingsFile.Name, "[]");

            var parseResult = rootCmd.Parse([
                "list-backups",
                "--hosturl", "http://example.com:1234/",
                "--password", "secret",
                "--settings-file", settingsFile.Name,
                "--insecure",
            ]);
            Assert.AreEqual(0, parseResult.Errors.Count);

            var settings = CommandLine.ServerUtil.SettingsBinder.GetSettings(parseResult);
            Assert.AreEqual(new Uri("http://example.com:1234/"), settings.HostUrl);
            Assert.AreEqual("secret", settings.Password);
            Assert.IsTrue(settings.Insecure);
        }

        [Test]
        public void GlobalOptionDefaultsBindToSettings()
        {
            var rootCmd = ServerUtilProgram.CreateRootCommand();
            using var settingsFile = new Library.Utility.TempFile();
            File.WriteAllText(settingsFile.Name, "[]");

            var parseResult = rootCmd.Parse(["list-backups", "--settings-file", settingsFile.Name]);
            Assert.AreEqual(0, parseResult.Errors.Count);

            var settings = CommandLine.ServerUtil.SettingsBinder.GetSettings(parseResult);
            Assert.IsNotNull(settings.HostUrl, "HostUrl should have a default value");
            Assert.IsFalse(settings.Insecure);
        }

        /// <summary>
        /// Regression test for the 2.0.100 canary issue where ServerUtil silently
        /// exited with code 1 and no output at all. The trigger is a settings file
        /// that holds an encrypted refresh token while no encryption key is supplied.
        /// The settings load then throws before the command handler runs, and the
        /// failure must be reported to the console instead of being swallowed.
        /// </summary>
        [Test]
        public async Task FailureToLoadSettingsReportsErrorToConsole()
        {
            var encryptionKey = EncryptedFieldHelper.KeyInstance.CreateKey("test-encryption-key");

            using var settingsFile = new Library.Utility.TempFile();
            var persistedSettings = new[]
            {
                new
                {
                    RefreshToken = EncryptedFieldHelper.Encrypt("dummy-refresh-token", encryptionKey),
                    RefreshNonce = (string)null,
                    HostUrl = new Uri("http://127.0.0.1:8200/"),
                    ServerDatafolder = (string)null
                }
            };
            File.WriteAllText(settingsFile.Name, JsonSerializer.Serialize(persistedSettings));

            // Make sure no ambient encryption key can satisfy the settings file
            var previousKey = Environment.GetEnvironmentVariable(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME);
            Environment.SetEnvironmentVariable(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, null);

            var originalOut = Console.Out;
            try
            {
                // Plain console output
                var output = new StringWriter();
                int exitCode;
                try
                {
                    Console.SetOut(output);
                    exitCode = await ServerUtilProgram.MainAsync(["--settings-file", settingsFile.Name, "list-backups"]);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }

                Assert.AreNotEqual(0, exitCode, "Expected a non-zero exit code when settings cannot be loaded");
                var consoleOutput = output.ToString();
                Assert.IsFalse(string.IsNullOrWhiteSpace(consoleOutput),
                    "The tool produced no console output; failures must be visible to the user");
                Assert.IsTrue(consoleOutput.Contains("key", StringComparison.OrdinalIgnoreCase),
                    $"The console output should mention the encryption key problem, but was: {consoleOutput}");

                // JSON output mode was silent as well
                output = new StringWriter();
                try
                {
                    Console.SetOut(output);
                    exitCode = await ServerUtilProgram.MainAsync(["--settings-file", settingsFile.Name, "--json", "list-backups"]);
                }
                finally
                {
                    Console.SetOut(originalOut);
                }

                Assert.AreNotEqual(0, exitCode, "Expected a non-zero exit code when settings cannot be loaded");
                var jsonOutput = output.ToString();
                Assert.IsFalse(string.IsNullOrWhiteSpace(jsonOutput),
                    "The tool produced no JSON output; failures must be visible to the user");
                Assert.IsTrue(jsonOutput.Contains("key", StringComparison.OrdinalIgnoreCase),
                    $"The JSON output should mention the encryption key problem, but was: {jsonOutput}");
            }
            finally
            {
                Environment.SetEnvironmentVariable(EncryptedFieldHelper.ENVIROMENT_VARIABLE_NAME, previousKey);
            }
        }

        /// <summary>
        /// Invoking with an unknown command must produce a non-zero exit code
        /// and print an error message, not exit silently.
        /// </summary>
        [Test]
        public async Task InvalidCommandProducesVisibleError()
        {
            var originalOut = Console.Out;
            var originalError = Console.Error;
            var output = new StringWriter();
            var error = new StringWriter();
            int exitCode;
            try
            {
                Console.SetOut(output);
                Console.SetError(error);
                exitCode = await ServerUtilProgram.MainAsync(["no-such-command"]);
            }
            finally
            {
                Console.SetOut(originalOut);
                Console.SetError(originalError);
            }

            Assert.AreNotEqual(0, exitCode, "Invalid command should return a non-zero exit code");
            var combined = output.ToString() + error.ToString();
            Assert.IsFalse(string.IsNullOrWhiteSpace(combined),
                "An invalid command produced no visible error message");
        }
    }
}
