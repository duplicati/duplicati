//  Copyright (C) 2011, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
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
using System.Collections.Generic;
using System.Linq;

namespace Duplicati.Library.Utility
{
	/// <summary>
	/// Represents multiple filters, that can each send true or false if they match or not
	/// </summary>
	public class CompositeFilterExpression : IFilter
	{
		private KeyValuePair<bool, IFilter>[] m_filters;
		private bool m_defaultValue;
		
		public bool Empty { get { return m_filters.Length == 0; } }
		
		public IEnumerable<KeyValuePair<bool, IFilter>> Filters { get { return m_filters; } }
		
		public CompositeFilterExpression(IEnumerable<KeyValuePair<bool, IFilter>> filters, bool defaultvalue)
		{
			m_filters = filters == null ? new KeyValuePair<bool, IFilter>[0] : filters.ToArray();
			m_defaultValue = defaultvalue;
		}
		
		public bool Matches(string entry)
		{
			foreach(var e in m_filters)
				if (e.Value.Matches(entry))
					return e.Key;
					
			return m_defaultValue;
		}
	}
}

