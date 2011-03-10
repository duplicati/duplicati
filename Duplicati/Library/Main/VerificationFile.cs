using System;
using System.Collections.Generic;
using System.Text;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// A class that contains all logic for generating the verification files
    /// </summary>
    public class VerificationFile
    {
        private ManifestEntry m_currentManifest;
        private System.Xml.XmlDocument m_doc;
        private System.Xml.XmlNode m_node;
        private System.Xml.XmlNode m_manifestEntry;
        private List<System.Xml.XmlNode> m_registedNodes;
        private int m_volumeCount;

        internal VerificationFile(IEnumerable<ManifestEntry> parentChain, FilenameStrategy str)
        {
            m_doc = new System.Xml.XmlDocument();
            System.Xml.XmlNode root = m_doc.AppendChild(m_doc.CreateElement("Verify"));
            root.Attributes.Append(m_doc.CreateAttribute("hash-algorithm")).Value = Utility.Utility.HashAlgorithm;
            root.Attributes.Append(m_doc.CreateAttribute("version")).Value = "1";

            m_node = root.AppendChild(m_doc.CreateElement("Files"));

            foreach (ManifestEntry mfe in parentChain)
            {
                System.Xml.XmlNode f = m_node.AppendChild(m_doc.CreateElement("File"));
                f.Attributes.Append(m_doc.CreateAttribute("type")).Value = "manifest";
                f.Attributes.Append(m_doc.CreateAttribute("name")).Value = mfe.Filename;
                f.InnerText = Utility.Utility.ByteArrayAsHexString(Convert.FromBase64String(mfe.RemoteHash));

                for (int i = 0; i < mfe.ParsedManifest.SignatureHashes.Count; i++)
                {
                    string sigfilename;
                    string contentfilename;
                    bool missing;
                    if (i < mfe.Volumes.Count)
                    {
                        sigfilename = mfe.Volumes[i].Key.Filename;
                        contentfilename = mfe.Volumes[i].Value.Filename;
                        missing = false;
                    }
                    else
                    {
                        //TODO: These are not 100% correct filenames as they do not have the compression and encryption extensions
                        sigfilename = str.GenerateFilename(new SignatureEntry(mfe.Time, mfe.IsFull, i + 1));
                        contentfilename = str.GenerateFilename(new ContentEntry(mfe.Time, mfe.IsFull, i + 1));
                        missing = true;
                    }

                    f = m_node.AppendChild(m_doc.CreateElement("File"));
                    f.Attributes.Append(m_doc.CreateAttribute("type")).Value = "signature";
                    f.Attributes.Append(m_doc.CreateAttribute("name")).Value = sigfilename;
                    if (missing) f.Attributes.Append(m_doc.CreateAttribute("missing")).Value = "true";
                    f.InnerText = Utility.Utility.ByteArrayAsHexString(Convert.FromBase64String(mfe.ParsedManifest.SignatureHashes[i]));
                    

                    f = m_node.AppendChild(m_doc.CreateElement("File"));
                    f.Attributes.Append(m_doc.CreateAttribute("type")).Value = "content";
                    f.Attributes.Append(m_doc.CreateAttribute("name")).Value = contentfilename;
                    if (missing) f.Attributes.Append(m_doc.CreateAttribute("missing")).Value = "true";
                    f.InnerText = Utility.Utility.ByteArrayAsHexString(Convert.FromBase64String(mfe.ParsedManifest.ContentHashes[i]));
                }
            }
        }

        public void UpdateManifest(ManifestEntry manifest)
        {
            if (m_manifestEntry == null)
                m_manifestEntry = m_node.AppendChild(m_doc.CreateElement("File"));
            m_manifestEntry.Attributes.Append(m_doc.CreateAttribute("type")).Value = "manifest";
            m_manifestEntry.Attributes.Append(m_doc.CreateAttribute("name")).Value = manifest.Filename;
            m_manifestEntry.InnerText = Utility.Utility.ByteArrayAsHexString(Convert.FromBase64String(manifest.RemoteHash));
        }

        public void AddFile(BackupEntryBase file)
        {
            System.Xml.XmlNode f = m_node.AppendChild(m_doc.CreateElement("File"));
            if (file is ManifestEntry)
                f.Attributes.Append(m_doc.CreateAttribute("type")).Value = "manifest";
            else if (file is SignatureEntry)
                f.Attributes.Append(m_doc.CreateAttribute("type")).Value = "signature";
            else if (file is ContentEntry)
                f.Attributes.Append(m_doc.CreateAttribute("type")).Value = "content";
            f.Attributes.Append(m_doc.CreateAttribute("name")).Value = file.Filename;
            f.InnerText = Utility.Utility.ByteArrayAsHexString(Convert.FromBase64String(file.RemoteHash));
        }

        public void Save(System.IO.Stream stream)
        {
            m_doc.Save(stream);
        }

        public void Save(string filename)
        {
            m_doc.Save(filename);
        }
    }

}
