using System;
using System.Collections.Generic;
using System.Text;

namespace UpdateVersionNumber
{
    public class DisplayItem
    {
        public DisplayItem(string file, Version version)
        {
            m_file = file;
            m_version = version;
        }

        private string m_file;

        public string File
        {
            get { return m_file; }
            set { m_file = value; }
        }
        private Version m_version;

        public Version Version
        {
            get { return m_version; }
            set { m_version = value; }
        }

        public override string ToString()
        {
            return m_file + " (" + m_version.ToString() + ")";
        }
    }
}
