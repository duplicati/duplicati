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
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Duplicati.Library.Utility;
using NUnit.Framework;
using Assert = NUnit.Framework.Legacy.ClassicAssert;

namespace Duplicati.UnitTest
{
    public class CommandLineWrappingTests
    {
        [Test]
        [Category("Utility")]
        public static void WrapCommandLineElementEscapesWindowsPercentWhenEnvironmentExpansionIsDisabled()
        {
            Assert.AreEqual("\"abc%%TEMP%%def\"", Utility.WrapCommandLineElement("abc%TEMP%def", false, true));
            Assert.AreEqual("\"100%%\"", Utility.WrapCommandLineElement("100%", false, true));
        }

        [Test]
        [Category("Utility")]
        public static void WrapCommandLineElementKeepsWindowsPercentWhenEnvironmentExpansionIsEnabled()
        {
            Assert.AreEqual("\"abc%TEMP%def\"", Utility.WrapCommandLineElement("abc%TEMP%def", true, true));
        }

        /// <summary>
        /// The shapes that were already right, kept so that a change to the
        /// escaping cannot quietly move them
        /// </summary>
        /// <param name="argument">The argument to wrap</param>
        /// <param name="expected">The expected wrapping</param>
        [Test]
        [Category("Utility")]
        [TestCase(@"plain-value_1", @"plain-value_1")]
        [TestCase(@"has space", @"""has space""")]
        [TestCase(@"C:\Temp\file.txt", @"""C:\Temp\file.txt""")]
        [TestCase(@"a\b", @"""a\b""")]
        [TestCase(@"\", @"""\\""")]
        [TestCase(@"C:\Temp\folder\", @"""C:\Temp\folder\\""")]
        public static void WrapCommandLineElementLeavesTheAlreadyCorrectWindowsShapesAlone(string argument, string expected)
        {
            Assert.AreEqual(expected, Utility.WrapCommandLineElement(argument, false, true));
        }

        /// <summary>
        /// A quote is escaped with a backslash, not by doubling it. Doubling is
        /// read back correctly by the C runtime that hands arguments to
        /// Main(string[]), but not by CommandLineToArgvW, so the two parsers
        /// disagree about the same string.
        /// </summary>
        /// <remarks>
        /// The literals are verbatim, so the characters are:
        ///   has"quote          -> "has\"quote"
        ///   "                  -> "\""
        ///   a"b"c              -> "a\"b\"c"
        ///   --passphrase=p"ss  -> "--passphrase=p\"ss"
        /// </remarks>
        /// <param name="argument">The argument to wrap</param>
        /// <param name="expected">The expected wrapping</param>
        [Test]
        [Category("Utility")]
        [TestCase(@"has""quote", @"""has\""quote""")]
        [TestCase(@"""", @"""\""""")]
        [TestCase(@"a""b""c", @"""a\""b\""c""")]
        [TestCase(@"--passphrase=p""ss", @"""--passphrase=p\""ss""")]
        public static void WrapCommandLineElementEscapesAWindowsQuoteWithABackslash(string argument, string expected)
        {
            Assert.AreEqual(expected, Utility.WrapCommandLineElement(argument, false, true));
        }

        /// <summary>
        /// Every backslash in a run that meets a quote has to be doubled, both
        /// for a quote inside the argument and for the closing quote. Leaving an
        /// odd number in front of a quote makes that quote a literal one, and
        /// the argument then never ends.
        /// </summary>
        /// <remarks>
        /// The literals are verbatim, so the characters are:
        ///   C:\dir\"x           -> "C:\dir\\\"x"
        ///   C:\Temp\folder\\    -> "C:\Temp\folder\\\\"
        ///   C:\Temp\folder\\\   -> "C:\Temp\folder\\\\\\"
        /// </remarks>
        /// <param name="argument">The argument to wrap</param>
        /// <param name="expected">The expected wrapping</param>
        [Test]
        [Category("Utility")]
        [TestCase(@"C:\dir\""x", @"""C:\dir\\\""x""")]
        [TestCase(@"C:\Temp\folder\\", @"""C:\Temp\folder\\\\""")]
        [TestCase(@"C:\Temp\folder\\\", @"""C:\Temp\folder\\\\\\""")]
        public static void WrapCommandLineElementDoublesEveryBackslashThatMeetsAQuote(string argument, string expected)
        {
            Assert.AreEqual(expected, Utility.WrapCommandLineElement(argument, false, true));
        }

        /// <summary>
        /// An empty argument has to be written as a pair of quotes, and one that
        /// is only whitespace has to be quoted, or the argument disappears from
        /// the commandline: an empty string adds nothing, and bare whitespace is
        /// read as the separator between arguments.
        /// </summary>
        /// <param name="argument">The argument to wrap</param>
        /// <param name="expected">The expected wrapping</param>
        /// <param name="isWindows">Whether Windows escaping is used</param>
        [Test]
        [Category("Utility")]
        [TestCase("", "\"\"", true)]
        [TestCase(" ", "\" \"", true)]
        [TestCase("   ", "\"   \"", true)]
        [TestCase("\t", "\"\t\"", true)]
        [TestCase("", "\"\"", false)]
        [TestCase(" ", "\" \"", false)]
        [TestCase("\t", "\"\t\"", false)]
        public static void WrapCommandLineElementQuotesAnEmptyOrWhitespaceArgument(string argument, string expected, bool isWindows)
        {
            Assert.AreEqual(expected, Utility.WrapCommandLineElement(argument, false, isWindows));
        }

        [Test]
        [Category("Utility")]
        public static void WrapCommandLineElementLeavesANullArgumentNull()
        {
            Assert.IsNull(Utility.WrapCommandLineElement(null, false, true));
            Assert.IsNull(Utility.WrapCommandLineElement(null, false, false));
        }

        [Test]
        [Category("Utility")]
        public static void WrapCommandLineElementRespectsUnixEnvironmentExpansionFlag()
        {
            Assert.AreEqual("\"$HOME/file\"", Utility.WrapCommandLineElement("$HOME/file", true, false));
            Assert.AreEqual("\"\\$HOME/file\"", Utility.WrapCommandLineElement("$HOME/file", false, false));
        }

        /// <summary>
        /// The point of all of the above: what Windows reads back has to be the
        /// argument that went in, and the argument after it has to survive.
        /// </summary>
        /// <param name="argument">The argument to wrap and read back</param>
        [Test]
        [Category("Utility")]
        [TestCase(@"plain-value_1")]
        [TestCase(@"has space")]
        [TestCase(@"C:\Temp\file.txt")]
        [TestCase(@"C:\Temp\folder\")]
        [TestCase(@"C:\Temp\folder\\")]
        [TestCase(@"C:\Temp\folder\\\")]
        [TestCase(@"C:\Program Files\Duplicati\")]
        [TestCase(@"has""quote")]
        [TestCase(@"""")]
        [TestCase(@"a""b""c")]
        [TestCase(@"C:\dir\""x")]
        [TestCase(@"\")]
        [TestCase(@"a\b")]
        [TestCase(@"--passphrase=p""ss")]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("   ")]
        [TestCase("\t")]
        public static void AWrappedWindowsArgumentIsReadBackAsItself(string argument)
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("The Windows commandline parser is only available on Windows.");
                return;
            }

            var wrapped = Utility.WrapCommandLineElement(argument, true, true);
            var parsed = ParseWindowsCommandLine(@"C:\program.exe " + wrapped + " --next=1");

            // Three, because the program name is the first of them. Anything
            // less means the argument swallowed what came after it.
            Assert.AreEqual(3, parsed.Length, $"<{wrapped}> was read as {parsed.Length} arguments");
            Assert.AreEqual(argument, parsed[1]);
            Assert.AreEqual("--next=1", parsed[2]);
        }

        /// <summary>
        /// The export of a backup as a commandline writes each option as
        /// <c>--name=</c> followed by the wrapped value. The value has to come back
        /// as itself, including one that is empty or only whitespace, and the
        /// option after it has to survive.
        /// </summary>
        /// <param name="value">The option value to wrap and read back</param>
        [Test]
        [Category("Utility")]
        [TestCase("")]
        [TestCase(" ")]
        [TestCase("  two spaces  ")]
        [TestCase(@"C:\Temp\folder\")]
        [TestCase(@"p""ss")]
        public static void AnOptionValueWrappedAfterTheEqualsSignIsReadBackAsItself(string value)
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("The Windows commandline parser is only available on Windows.");
                return;
            }

            // The same composition as Runner.GetCommandLine
            var option = string.Format("--{0}={1}", "passphrase", Utility.WrapCommandLineElement(value, false));
            var parsed = ParseWindowsCommandLine(@"C:\program.exe " + option + " --next=1");

            Assert.AreEqual(3, parsed.Length, $"<{option}> was read as {parsed.Length} arguments");
            Assert.AreEqual("--passphrase=" + value, parsed[1]);
            Assert.AreEqual("--next=1", parsed[2]);
        }

        /// <summary>
        /// The same thing for a whole commandline, which is the shape the
        /// Windows service registers as its image path
        /// </summary>
        [Test]
        [Category("Utility")]
        public static void AWrappedWindowsCommandLineIsReadBackAsItsArguments()
        {
            if (!OperatingSystem.IsWindows())
            {
                Assert.Ignore("The Windows commandline parser is only available on Windows.");
                return;
            }

            var arguments = new[]
            {
                "--portable-mode",
                @"--webservice-sslcertificatefile=C:\Program Files\certs\Default Web Site-all.pfx",
                @"--webservice-sslcertificatepassword=p""ss",
                @"--dbpath=C:\Temp\folder\\",
                "",
                " ",
                "--log-retention=3M"
            };

            var commandline = Utility.WrapAsCommandLine(arguments, true);
            var parsed = ParseWindowsCommandLine(@"C:\program.exe " + commandline);

            Assert.AreEqual(arguments.Length + 1, parsed.Length, $"<{commandline}> was read as {parsed.Length} arguments");
            for (var i = 0; i < arguments.Length; i++)
                Assert.AreEqual(arguments[i], parsed[i + 1]);
        }

        /// <summary>
        /// Splits a commandline the way Windows does when it hands the arguments
        /// to a program
        /// </summary>
        /// <returns>The arguments, the program name first.</returns>
        /// <param name="commandline">The commandline to split.</param>
        [SupportedOSPlatform("windows")]
        private static string[] ParseWindowsCommandLine(string commandline)
        {
            var block = CommandLineToArgvW(commandline, out var count);
            if (block == IntPtr.Zero)
                throw new InvalidOperationException($"CommandLineToArgvW failed with {Marshal.GetLastWin32Error()}");

            try
            {
                var result = new string[count];
                for (var i = 0; i < count; i++)
                    result[i] = Marshal.PtrToStringUni(Marshal.ReadIntPtr(block, i * IntPtr.Size));

                return result;
            }
            finally
            {
                LocalFree(block);
            }
        }

        /// <summary>
        /// Splits a commandline into arguments, as Windows itself does
        /// </summary>
        [SupportedOSPlatform("windows")]
        [DllImport("shell32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string lpCmdLine, out int pNumArgs);

        /// <summary>
        /// Releases the block that CommandLineToArgvW allocated
        /// </summary>
        [SupportedOSPlatform("windows")]
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr LocalFree(IntPtr hMem);
    }
}
