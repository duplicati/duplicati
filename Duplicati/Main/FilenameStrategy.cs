using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Main
{
    /// <summary>
    /// This class is responsible for naming remote files,
    /// and parsing remote filenames
    /// </summary>
    internal class FilenameStrategy
    {
        private bool m_useShortFilenames;
        private string m_timeSeperator;

        public FilenameStrategy(Dictionary<string, string> options)
        {
            //--short-filenames
            //--time-separator
            m_useShortFilenames = options.ContainsKey("short-filenames");
            if (options.ContainsKey("time-seperator"))
                m_timeSeperator = options["time-seperator"];
            else
                m_timeSeperator = ":";
        }

        public string GenerateFilename(string prefix, bool signatures, bool full, DateTime time)
        {
            if (!m_useShortFilenames)
            {
                string datetime = time.ToString().Replace(":", m_timeSeperator);
                return prefix + "-" + (signatures ? "signatures" : "content") + "-" + (full ? "full" : "inc") + "." + datetime;
            }
            else
            {
                //TODO: Finish this
                byte[] tmp = new byte[4 + 2 + 2 + 2 + 2 + 2];
                return prefix + "-" + (signatures ? "S" : "C") + (full ? "F" : "I") + "";
            }
        }

        public BackupEntry DecodeFilename(string prefix, Duplicati.Backend.FileEntry fe)
        {
            //TODO: Use RegExp to parse it
            //Filename looks like: "<prefix>-<content/signatures>-<full/inc>-<basename>.<date>.zip.pgp"
            //or
            //"<prefix>-<C/S><F/I>.<short date>.zip.pgp"
            if (!fe.Name.StartsWith(prefix))
                return null;
            string c = fe.Name.Substring(prefix.Length + 1);

            bool isContent = false;
            bool isFull = false;
            DateTime time;

            if (m_useShortFilenames)
            {
                //TODO: Finish this
                return null;
            }
            else
            {
                if (c.StartsWith("content"))
                    isContent = true;
                else if (!c.StartsWith("signatures"))
                    return null;

                c = c.Substring((isContent ? "content" : "signatures").Length + 1);

                if (c.StartsWith("full"))
                    isFull = true;
                else if (!c.StartsWith("inc"))
                    return null;

                c = c.Substring((isFull ? "full" : "inc").Length + 1);

                try
                {
                    string datestring = c.Substring(0, c.IndexOf(".")).Replace(m_timeSeperator, ":");
                    time = DateTime.Parse(datestring);
                }
                catch
                {
                    return null;
                }
            }
            return new BackupEntry(fe, time, isContent, isFull);
        }

    }
}
