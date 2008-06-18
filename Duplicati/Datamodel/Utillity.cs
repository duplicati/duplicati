using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Datamodel
{
    public static class Utillity
    {
        public static string FormatSizeString(long size)
        {
            if (size > 1024 * 1024 * 1024)
                return string.Format("{0:N} GB", (double)size / (1024 * 1024 * 1024));
            else if (size > 1024 * 1024)
                return string.Format("{0:N} MB", (double)size / (1024 * 1024));
            else if (size > 1024)
                return string.Format("{0:N} KB", (double)size / 1024);
            else
                return string.Format("{0} bytes", size);
        }
    }
}
