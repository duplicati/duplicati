//  Copyright (C) 2012, Aaron Hamid
//  https://github.com/ahamid, aaron.hamid@gmail.com
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
using System;
using NUnit.Framework;

namespace Duplicati_Test
{
    // Exists because < NUnit 2.5: http://nunit.org/index.php?p=setup&r=2.5
    // "Before NUnit 2.5, you were permitted only one SetUp method. If you wanted to have some SetUp functionality
    // in the base class and add more in the derived class you needed to call the base class method yourself."
    public abstract class InheritableTestCase
    {
        // define SetUp and TearDown method convention
        public abstract void SetUp();
        public abstract void TearDown();
    }
}