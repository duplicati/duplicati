using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace LocalizationTool
{
    public class CSVReader : IDisposable
    {
        private System.IO.StreamReader m_reader;

        public CSVReader(string filename)
        {
            m_reader = new System.IO.StreamReader(filename, System.Text.Encoding.UTF8, true);
        }

        public List<string> AdvanceLine()
        {
            bool inQuote = false;
            StringBuilder sb = new StringBuilder();
            do
            {
                string line = m_reader.ReadLine();
                if (line == null)
                {
                    if (sb.Length == 0)
                        return null;
                    else
                        throw new Exception("Unexpected EOF, buffer was: " + sb.ToString());
                }

                sb.AppendLine(line);

                foreach (char c in line)
                    if (c == '"')
                        inQuote = !inQuote;
            } while (inQuote);

            string txt = sb.ToString();

            if (txt.EndsWith("\r\n"))
                txt = txt.Substring(0, txt.Length - 2);
            else if (txt.EndsWith("\r\n"))
                txt = txt.Substring(0, txt.Length - 2);
            else if (txt.EndsWith("\n"))
                txt = txt.Substring(0, txt.Length - 1);
            else if (txt.EndsWith("\r"))
                txt = txt.Substring(0, txt.Length - 1);


            inQuote = false;
            int ix = 0;
            List<string> result = new List<string>();
            for(int i = 0; i < txt.Length; i++)
                if (inQuote)
                {
                    if (txt[i] == '"')
                        inQuote = false;
                }
                else 
                {
                    if (txt[i] == '"')
                        inQuote = true;
                    else if (txt[i] == ',')
                    {
                        result.Add(CleanString(txt.Substring(ix, i - ix)));
                        ix = i + 1;
                    }
                }

            if (inQuote)
                throw new Exception("Failed to parse line: " + txt);

            if (ix != txt.Length)
                result.Add(CleanString(txt.Substring(ix)));

            return result;
        }

        private string CleanString(string v)
        {
            //v = v.Trim();

            if (v.StartsWith("\"") && v.EndsWith("\""))
                return v.Substring(1, v.Length - 2).Replace("\"\"", "\"");
            else
                return v;
        }

        #region IDisposable Members

        public void Dispose()
        {
            if (m_reader != null)
            {
                m_reader.Dispose();
                m_reader = null;
            }
        }

        #endregion
    }
}
