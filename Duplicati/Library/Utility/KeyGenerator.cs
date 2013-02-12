#region Disclaimer / License
// Copyright (C) 2011, Kenneth Skovhede
// http://www.hexad.dk, opensource@hexad.dk
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Utility
{
    public static class KeyGenerator
    {
        public static readonly char[] ALPHA_LOWER = new char[] { 'a', 'b', 'c', 'd', 'e', 'f', 'g', 'h', 'i', 'j', 'k', 'l', 'm', 'n', 'o', 'p', 'q', 'r', 's', 't', 'u', 'v', 'w', 'x', 'y', 'z' };
        public static readonly char[] ALPHA_UPPER = new char[] { 'A', 'B', 'C', 'D', 'E', 'F', 'G', 'H', 'I', 'K', 'K', 'L', 'M', 'N', 'O', 'P', 'Q', 'R', 'S', 'T', 'U', 'V', 'W', 'X', 'Y', 'Z' };
        public static readonly char[] DIGITS = new char[] { '0', '2', '3', '4', '5', '6', '7', '8', '9' };
        public static readonly char[] PUNCTUATION = new char[] { '.', ',', ':', ';', '!', '?' };
        public static readonly char[] MISC = new char[] { '*', '@', '#', '$', '%', '&', '/', '(', ')', '=', '+', '>', '<', '-', '_' };

        public static readonly char[] HEX_CHARS = new char[] { '0', '1', '2', '3', '4', '5', '6', '7', '8', '9', 'A', 'B', 'C', 'D', 'E', 'F' };

        private static char[] _allowed = null;
        private static Random _rnd = null;

        public static char[] AllowedChars
        {
            get
            {
                if (_allowed == null)
                {
                    List<char> l = new List<char>();
                    l.AddRange(ALPHA_LOWER);
                    l.AddRange(ALPHA_UPPER);
                    l.AddRange(DIGITS);
                    l.AddRange(PUNCTUATION);
                    l.AddRange(MISC);
                    _allowed = l.ToArray();
                }

                return _allowed;
            }
        }

        public static Random Rnd
        {
            get
            {
                if (_rnd == null)
                    _rnd = new Random();
                return _rnd;
            }
        }

        public static string GenerateKey(int minChars, int maxChars)
        {
            char[] tmp = new char[Rnd.Next(minChars, maxChars + 1)];
            for (int i = 0; i < tmp.Length; i++)
                tmp[i] = AllowedChars[Rnd.Next(0, AllowedChars.Length)];

            return new string(tmp);
        }

        public static string GenerateSignKey()
        {
            byte[] tmp = new byte[4];
            Rnd.NextBytes(tmp);

            char[] res = new char[8];
            for (int i = 0; i < tmp.Length; i++)
            {
                res[i * 2] = HEX_CHARS[tmp[i] & 0xF];
                res[i * 2 + 1] = HEX_CHARS[(tmp[i] >> 4) & 0xF];
            }

            return new string(res);
        }
    }
}
