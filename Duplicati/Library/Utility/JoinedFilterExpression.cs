// Copyright (C) 2025, The Duplicati Team
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

        /// <summary>
        /// Returns MD5 hash of filter expression
        /// </summary>
        /// <returns>MD5 hash of filter expression</returns>
        public string GetFilterHash()
        {
            var hash = MD5HashHelper.GetHash(new[] {First.GetFilterHash(), Second.GetFilterHash()});
			return Utility.ByteArrayAsHexString(hash);
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
                if (first is FilterExpression expression && second is FilterExpression filterExpression && expression.Result == filterExpression.Result)
                    return FilterExpression.Combine(expression, filterExpression);
                
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
                return "(" + this.First + ") || (" + this.Second + ")";
        }
    }
}

