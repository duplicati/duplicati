#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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
using Duplicati.Datamodel;

namespace Duplicati.GUI
{
    public class DuplicatiOutputParser
    {
        private const string OK_INDICATOR = "Duration";
        private const string WARNING_INDICATOR = "NumberOfErrors";

        private const string SIZE_INDICATOR = "BytesUploaded";

        public static void ParseData(Log log)
        {
            string text = log.Blob.StringData;

            log.ParsedStatus = "Error";

            if (text.IndexOf(OK_INDICATOR) >= 0)
                log.ParsedStatus = "OK";

            long c = ExtractNumber(text, WARNING_INDICATOR, -1);
            if (c > 0)
                log.ParsedStatus = "Warning";

            c = ExtractNumber(text, SIZE_INDICATOR, 0);

            if (c <= 0)
                log.ParsedStatus = "Warning";

            log.Transfersize = c;
        }

        private static long ExtractNumber(string output, string key, long @default)
        {
            long count = @default;
            if (output.IndexOf(key) >= 0)
            {
                int pos = output.IndexOf(key) + key.Length;
                pos = output.IndexOf(':', pos) + 1;
                int p2 = output.IndexOfAny(new char[] { '\r', '\n' }, pos);
                string number = output.Substring(pos, p2 - pos).Trim();
                if (!long.TryParse(number, out count))
                    count = @default;
            }

            return count;
        }
    }
}
