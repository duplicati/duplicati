using System;

namespace Duplicati.Library.Backend.Extensions
{
    public static class SystemExtensions
    {
        private static readonly DateTime m_firstDate = new DateTime(1970, 1, 1);
        
        public static long GetEpochSeconds(this DateTime dateTime)
        {
            return (long)dateTime.Subtract(m_firstDate).TotalSeconds;
        }
    }
}