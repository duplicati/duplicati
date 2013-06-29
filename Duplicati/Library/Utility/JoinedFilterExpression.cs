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

namespace Duplicati.Library.Utility
{
    public class JoinedFilterExpression : IFilter
    {
        private IFilter m_first;
        private IFilter m_second;
        
        public JoinedFilterExpression(IFilter first, IFilter second)
        {
            m_first = first ?? new FilterExpression();
            m_second = second ?? new FilterExpression();
        }

        public bool Matches(string entry, out bool result)
        {
            return m_first.Matches(entry, out result) || m_second.Matches(entry, out result);
        }

        public bool Empty { get { return m_first.Empty && m_second.Empty; } }
        
        public static IFilter Join(IFilter first, IFilter second)
        {
            if (first == null || first.Empty)
                return second;
            else if (second == null || second.Empty)
                return first;
            else
            {
                if (first is FilterExpression && second is FilterExpression && ((FilterExpression)first).Result == ((FilterExpression)second).Result)
                    return FilterExpression.Combine((FilterExpression)first, (FilterExpression)second);
                
                return new JoinedFilterExpression(first, second);
            }
        }
    }
}

