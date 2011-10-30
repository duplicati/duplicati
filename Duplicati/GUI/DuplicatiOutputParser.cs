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
using Duplicati.Datamodel;

namespace Duplicati.GUI
{
    public class DuplicatiOutputParser
    {
        public const string ErrorStatus = "Error";
        public const string OKStatus = "OK";
        public const string WarningStatus = "Warning";
        public const string PartialStatus = "Partial";
        public const string InterruptedStatus = "InProgress";
        public const string NoChangedFiles = "Empty";

        private const string OK_INDICATOR = "Duration";
        private const string WARNING_INDICATOR = "NumberOfErrors";

        private const string UPLOAD_SIZE_INDICATOR = "BytesUploaded";
        private const string DOWNLOAD_SIZE_INDICATOR = "BytesDownloaded";
        private const string PARTIAL_INDICATOR = "Unprocessed";

        private const string METHOD_INDICATOR = "OperationName";

        public static void ParseData(Log log)
        {
            string text = log.Blob.StringData;

            log.ParsedStatus = ErrorStatus;

            if (text.IndexOf(OK_INDICATOR) >= 0)
                log.ParsedStatus = OKStatus;

            long c = ExtractNumber(text, WARNING_INDICATOR, -1);
            if (c > 0)
                log.ParsedStatus = WarningStatus;

            bool isBackup = ExtractValue(text, METHOD_INDICATOR, "").StartsWith("backup", StringComparison.InvariantCultureIgnoreCase);

            if (isBackup)
                c = ExtractNumber(text, UPLOAD_SIZE_INDICATOR, -1);
            else
                c = ExtractNumber(text, DOWNLOAD_SIZE_INDICATOR, -1);

            if (c == 0)
            {
                if (isBackup)
                    log.ParsedStatus = NoChangedFiles;
                else
                    log.ParsedStatus = WarningStatus;
            }

            log.Transfersize = c;

            if (log.ParsedStatus == WarningStatus)
            {
                long missing = ExtractNumber(text, PARTIAL_INDICATOR, 0);
                if (missing > 0)
                    log.ParsedStatus = PartialStatus;
            }
        }

        private static long ExtractNumber(string output, string key, long @default)
        {
            long count;
            if (!long.TryParse(ExtractValue(output, key, @default.ToString()), out count))
                count = @default;
            
            return count;
        }

        private static string ExtractValue(string output, string key, string @default)
        {
            string result = @default;
            if (output.IndexOf(key) >= 0)
            {
                int pos = output.IndexOf(key) + key.Length;
                pos = output.IndexOf(':', pos) + 1;
                int p2 = output.IndexOfAny(new char[] { '\r', '\n' }, pos);
                result = output.Substring(pos, p2 - pos).Trim();
            }

            return result;
        }
    }
}
