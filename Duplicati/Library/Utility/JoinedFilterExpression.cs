//  Copyright (C) 2015, The Duplicati Team

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
using System;

namespace Duplicati.Library.Utility
{
    public class JoinedFilterExpression : IFilter
    {
        public readonly IFilter First;
        public readonly IFilter Second;
        
        public JoinedFilterExpression(IFilter first, IFilter second)
        {
            this.First = first ?? new FilterExpression();
            this.Second = second ?? new FilterExpression();
        }

        public bool Matches(string entry, out bool result, out IFilter match)
        {
            return First.Matches(entry, out result, out match) || Second.Matches(entry, out result, out match);
        }

        public bool Empty { get { return First.Empty && Second.Empty; } }
        
        public static IFilter Join(IFilter first, IFilter second)
        {
            if (first == null && second == null)
                return null;
            else if (first == null)
                return second;
            else if (second == null)
                return first;
            else if (first.Empty)
                return second;
            else if (second.Empty)
                return first;
            else
            {
                if (first is FilterExpression && second is FilterExpression && ((FilterExpression)first).Result == ((FilterExpression)second).Result)
                    return FilterExpression.Combine((FilterExpression)first, (FilterExpression)second);
                
                return new JoinedFilterExpression(first, second);
            }
        }
        
        public override string ToString()
        {
            if (this.First.Empty)
                return this.Second.ToString();
            else if (this.Second.Empty)
                return this.First.ToString();
            else
                return "(" + this.First.ToString() + ") || (" + this.Second.ToString() + ")";
        }
    }
}

