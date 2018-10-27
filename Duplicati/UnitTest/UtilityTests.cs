//  Copyright (C) 2017, The Duplicati Team
//  http://www.duplicati.com, info@duplicati.com
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using Duplicati.Library.Utility;
using Duplicati.Library.IO;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.UnitTest
{
    public class UtilityTests
    {
        [Test]
        [Category("Utility")]
        public static void AppendDirSeparator()
        {
            const string noTrailingSlash = @"/a\b/c";
            string hasTrailingSlash = noTrailingSlash + Util.DirectorySeparatorString;

            string alternateSeparator = null;
            if (String.Equals(Util.DirectorySeparatorString, "/", StringComparison.Ordinal))
            {
                alternateSeparator = @"\";
            }
            if (String.Equals(Util.DirectorySeparatorString, @"\", StringComparison.Ordinal))
            {
                alternateSeparator = "/";
            }

            Assert.AreEqual(hasTrailingSlash, Util.AppendDirSeparator(noTrailingSlash));
            Assert.AreEqual(hasTrailingSlash, Util.AppendDirSeparator(hasTrailingSlash));
            Assert.AreEqual(hasTrailingSlash, Util.AppendDirSeparator(noTrailingSlash), Util.DirectorySeparatorString);
            Assert.AreEqual(hasTrailingSlash, Util.AppendDirSeparator(hasTrailingSlash), Util.DirectorySeparatorString);

            Assert.AreEqual(noTrailingSlash + alternateSeparator, Util.AppendDirSeparator(noTrailingSlash, alternateSeparator));
            Assert.AreEqual(noTrailingSlash + alternateSeparator, Util.AppendDirSeparator(noTrailingSlash + alternateSeparator, alternateSeparator));
            Assert.AreEqual(hasTrailingSlash + alternateSeparator, Util.AppendDirSeparator(hasTrailingSlash, alternateSeparator));
        }

        [Test]
        [Category("Utility")]
        [TestCase("da-DK")]
        [TestCase("en-US")]
        [TestCase("hu-HU")]
        [TestCase("tr-TR")]
        public static void FilenameStringComparison(string cultureName)
        {
            Action<string, string> checkStringComparison = (x, y) => Assert.IsFalse(String.Equals(x, y, Utility.ClientFilenameStringComparison));
            Action<string, string> checkStringComparer = (x, y) => Assert.IsFalse(new HashSet<string>(new[] { x }).Contains(y, Utility.ClientFilenameStringComparer));

            System.Globalization.CultureInfo originalCulture = System.Globalization.CultureInfo.CurrentCulture;
            try
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = new System.Globalization.CultureInfo(cultureName, false);

                // These are equivalent with respect to hu-HU, but different with respect to en-US.
                string ddzs = "ddzs";
                string dzsdzs = "dzsdzs";
                checkStringComparison(ddzs, dzsdzs);
                checkStringComparer(ddzs, dzsdzs);

                // Many cultures treat the following as equivalent.
                string eAcuteOneCharacter = System.Text.Encoding.GetEncoding("iso-8859-1").GetString(new byte[] { 233 }); // 'é' as one character (ALT+0233).
                string eAcuteTwoCharacters = "\u0065\u0301"; // 'e', combined with an acute accent (U+301).
                checkStringComparison(eAcuteOneCharacter, eAcuteTwoCharacters);
                checkStringComparer(eAcuteOneCharacter, eAcuteTwoCharacters);

                // These are equivalent with respect to en-US, but different with respect to da-DK.
                string aDiaeresisOneCharacter = "\u00C4"; // 'A' with a diaeresis.
                string aDiaeresisTwoCharacters = "\u0041\u0308"; // 'A', combined with a diaeresis.
                checkStringComparison(aDiaeresisOneCharacter, aDiaeresisTwoCharacters);
                checkStringComparer(aDiaeresisOneCharacter, aDiaeresisTwoCharacters);
            }
            finally
            {
                System.Threading.Thread.CurrentThread.CurrentCulture = originalCulture;
            }
        }

        [Test]
        [Category("Utility")]
        public static void ForceStreamRead()
        {
            byte[] source = { 0x10, 0x20, 0x30, 0x40, 0x50 };

            // Ensure that ReadOneByteStream returns one byte at a time.
            byte[] buffer = new byte[source.Length];
            ReadOneByteStream stream = new ReadOneByteStream(source);
            Assert.AreEqual(1, stream.Read(buffer, 0, buffer.Length));
            Assert.AreEqual(source.First(), buffer.First());
            foreach (byte value in buffer.Skip(1))
            {
                Assert.AreEqual(default(byte), value);
            }

            // Buffer is larger than the length of the stream.
            buffer = new byte[source.Length + 1];
            int bytesRead = Utility.ForceStreamRead(new ReadOneByteStream(source), buffer, source.Length);
            Assert.AreEqual(source.Length, bytesRead);
            CollectionAssert.AreEqual(source, buffer.Take(source.Length));
            Assert.AreEqual(default(byte), buffer.Last());

            // Maximum number of bytes is larger than the length of the stream.
            buffer = new byte[source.Length + 1];
            bytesRead = Utility.ForceStreamRead(new ReadOneByteStream(source), buffer, source.Length + 1);
            Assert.AreEqual(source.Length, bytesRead);
            CollectionAssert.AreEqual(source, buffer.Take(bytesRead));
            Assert.AreEqual(default(byte), buffer.Last());

            // Maximum number of bytes is smaller than the length of the stream.
            buffer = new byte[source.Length];
            bytesRead = Utility.ForceStreamRead(new ReadOneByteStream(source), buffer, source.Length - 1);
            Assert.AreEqual(source.Length - 1, bytesRead);
            CollectionAssert.AreEqual(source.Take(bytesRead), buffer.Take(bytesRead));
            Assert.AreEqual(default(byte), buffer.Last());

            // Buffer is smaller than the length of the stream.
            Assert.Throws<ArgumentException>(() => Utility.ForceStreamRead(new ReadOneByteStream(source), new byte[source.Length - 1], source.Length));
        }

        [Test]
        [Category("Utility")]
        public void GetUniqueItems()
        {
            string[] collection = { "A", "a", "A", "b", "c", "c" };
            string[] uniqueItems = { "A", "a", "b", "c" };
            string[] duplicateItems = { "A", "c" };

            // Test with default comparer.
            ISet<string> actualDuplicateItems;
            ISet<string> actualUniqueItems = Utility.GetUniqueItems(collection, out actualDuplicateItems);

            CollectionAssert.AreEquivalent(uniqueItems, actualUniqueItems);
            CollectionAssert.AreEquivalent(duplicateItems, actualDuplicateItems);

            // Test with custom comparer.
            IEqualityComparer<string> comparer = StringComparer.OrdinalIgnoreCase;
            uniqueItems = new string[] { "a", "b", "c" };
            duplicateItems = new string[] { "a", "c" };

            actualDuplicateItems = null;
            actualUniqueItems = Utility.GetUniqueItems(collection, comparer, out actualDuplicateItems);

            Assert.That(actualUniqueItems, Is.EquivalentTo(uniqueItems).Using(comparer));
            Assert.That(actualDuplicateItems, Is.EquivalentTo(duplicateItems).Using(comparer));

            // Test with empty collection.
            actualDuplicateItems = null;
            actualUniqueItems = Utility.GetUniqueItems(new string[0], out actualDuplicateItems);

            Assert.IsNotNull(actualUniqueItems);
            Assert.IsNotNull(actualDuplicateItems);
        }

        [Test]
        [Category("Utility")]
        public void ParseBool()
        {
            string[] expectTrue = { "1", "on", "true", "yes" };
            string[] expectFalse = { "0", "off", "false", "no" };
            string[] expectDefault = { null, "", "maybe" };
            Func<bool> returnsTrue = () => true;
            Func<bool> returnsFalse = () => false;

            foreach (string value in expectTrue)
            {
                string message = $"{value} should be parsed to true.";

                Assert.IsTrue(Utility.ParseBool(value, false), message);
                Assert.IsTrue(Utility.ParseBool(value.ToUpper(), false), message);
                Assert.IsTrue(Utility.ParseBool($" {value} ", false), message);
            }

            foreach (string value in expectFalse)
            {
                string message = $"{value} should be parsed to false.";

                Assert.IsFalse(Utility.ParseBool(value, true), message);
                Assert.IsFalse(Utility.ParseBool(value.ToUpper(), true), message);
                Assert.IsFalse(Utility.ParseBool($" {value} ", true), message);
            }

            foreach (string value in expectDefault)
            {
                Assert.IsTrue(Utility.ParseBool(value, true));
                Assert.IsTrue(Utility.ParseBool(value, returnsTrue));
                Assert.IsFalse(Utility.ParseBool(value, false));
                Assert.IsFalse(Utility.ParseBool(value, returnsFalse));
            }
        }
    }

    /// <summary>
    /// Mimic a Stream that can only read one byte at a time.
    /// </summary>
    class ReadOneByteStream : System.IO.MemoryStream
    {
        private readonly byte[] source;

        public ReadOneByteStream(byte[] source)
        {
            this.source = source;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (Object.ReferenceEquals(buffer, null))
            {
                throw new ArgumentNullException(nameof(buffer));
            }
            if (offset < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }
            if (offset + count > buffer.Length)
            {
                throw new ArgumentException("The sum of offset and count must not be larger than the buffer size.");
            }

            if (offset < this.source.Length)
            {
                const int bytesRead = 1;
                Array.Copy(this.source, offset, buffer, offset, bytesRead);
                return bytesRead;
            }

            return 0;
        }
    }
}
