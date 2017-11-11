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
using NUnit.Framework;
using System;
using System.Collections.Generic;

namespace Duplicati.UnitTest
{
    [TestFixture]
    public class UtilityTests
    {
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
            uniqueItems = new string[] {"a", "b", "c"};
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
    }
}
