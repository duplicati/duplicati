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

namespace Duplicati.Library.Main
{
    /// <summary>
    /// A class that contains all logic for generating the verification files
    /// </summary>
    public class VerificationFile
    {
        private System.Xml.XmlDocument m_doc;
        private System.Xml.XmlNode m_node;
        private System.Xml.XmlNode m_manifestEntry;

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
                f.Attributes.Append(m_doc.CreateAttribute("size")).Value = mfe.Filesize.ToString();
                f.InnerText = Utility.Utility.ByteArrayAsHexString(Convert.FromBase64String(mfe.RemoteHash));

                for (int i = 0; i < mfe.ParsedManifest.SignatureHashes.Count; i++)
                {
                    string sigfilename = mfe.ParsedManifest.SignatureHashes[i].Name;
                    string contentfilename = mfe.ParsedManifest.ContentHashes[i].Name;
                    bool missing = i >= mfe.Volumes.Count;

                    if (string.IsNullOrEmpty(sigfilename) || string.IsNullOrEmpty(contentfilename))
                    {
                        if (missing)
                        {
                            sigfilename = str.GenerateFilename(new SignatureEntry(mfe.Time, mfe.IsFull, i + 1));
                            contentfilename = str.GenerateFilename(new ContentEntry(mfe.Time, mfe.IsFull, i + 1));

                            //Since these files are missing, we have to guess what their real names were
                            string compressionGuess;
                            if (mfe.Volumes.Count <= 0)
                                compressionGuess = ".zip"; //Default if we have no knowledge
                            else
                                compressionGuess = "." + mfe.Volumes[0].Key.Compression; //Most likely the same as all the others

                            //Encryption will likely be the same as the one the manifest uses
                            string encryptionGuess = string.IsNullOrEmpty(mfe.EncryptionMode) ? "" : "." + mfe.EncryptionMode;

                            sigfilename += compressionGuess + encryptionGuess;
                            contentfilename += compressionGuess + encryptionGuess;
                        }
                        else
                        {
                            sigfilename = mfe.Volumes[i].Key.Filename;
                            contentfilename = mfe.Volumes[i].Value.Filename;
                        }
                    }

                    f = m_node.AppendChild(m_doc.CreateElement("File"));
                    f.Attributes.Append(m_doc.CreateAttribute("type")).Value = "signature";
                    f.Attributes.Append(m_doc.CreateAttribute("name")).Value = sigfilename;
                    f.Attributes.Append(m_doc.CreateAttribute("size")).Value = mfe.ParsedManifest.SignatureHashes[i].Size.ToString();
                    if (missing) f.Attributes.Append(m_doc.CreateAttribute("missing")).Value = "true";
                    f.InnerText = Utility.Utility.ByteArrayAsHexString(Convert.FromBase64String(mfe.ParsedManifest.SignatureHashes[i].Hash));
                    

                    f = m_node.AppendChild(m_doc.CreateElement("File"));
                    f.Attributes.Append(m_doc.CreateAttribute("type")).Value = "content";
                    f.Attributes.Append(m_doc.CreateAttribute("name")).Value = contentfilename;
                    f.Attributes.Append(m_doc.CreateAttribute("size")).Value = mfe.ParsedManifest.ContentHashes[i].Size.ToString();
                    if (missing) f.Attributes.Append(m_doc.CreateAttribute("missing")).Value = "true";
                    f.InnerText = Utility.Utility.ByteArrayAsHexString(Convert.FromBase64String(mfe.ParsedManifest.ContentHashes[i].Hash));
                }
            }
        }

        public void UpdateManifest(ManifestEntry manifest)
        {
            if (m_manifestEntry == null)
            {
                m_manifestEntry = m_node.AppendChild(m_doc.CreateElement("File"));
                m_manifestEntry.Attributes.Append(m_doc.CreateAttribute("type")).Value = "manifest";
                m_manifestEntry.Attributes.Append(m_doc.CreateAttribute("name")).Value = manifest.Filename;
                m_manifestEntry.Attributes.Append(m_doc.CreateAttribute("size")).Value = manifest.Filesize.ToString();
            }
            else
            {
                m_manifestEntry.Attributes["type"].Value = "manifest";
                m_manifestEntry.Attributes["name"].Value = manifest.Filename;
                m_manifestEntry.Attributes["size"].Value = manifest.Filesize.ToString();
            }
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
            f.Attributes.Append(m_doc.CreateAttribute("size")).Value = file.Filesize.ToString();
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
