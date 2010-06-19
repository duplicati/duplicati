#region Disclaimer / License
// Copyright (C) 2010, Kenneth Skovhede
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
using System.Xml;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// A class that handles manifest serialization and deserialization
    /// </summary>
    public class Manifestfile
    {
        /// <summary>
        /// The list of signature hashes
        /// </summary>
        public List<string> SignatureHashes { get; set; }

        /// <summary>
        /// The list of content hashes
        /// </summary>
        public List<string> ContentHashes { get; set; }

        /// <summary>
        /// The list of source dirs, where the backups are created from
        /// </summary>
        public string[] SourceDirs { get; set; }

        /// <summary>
        /// Gets the manifest file version
        /// </summary>
        public int Version { get; set; }

        /// <summary>
        /// The largest supported version
        /// </summary>
        public const int MaxSupportedVersion = 2;

        /// <summary>
        /// Constructs a blank manifest file
        /// </summary>
        public Manifestfile()
        {
            SignatureHashes = new List<string>();
            ContentHashes = new List<string>();
            Version = MaxSupportedVersion;
        }

        /// <summary>
        /// Reads the supplied file and initializes the manifest file class
        /// </summary>
        /// <param name="filename">The file to read the manifest from</param>
        public Manifestfile(string filename)
            : this()
        {
            Read(filename);
        }

        /// <summary>
        /// Reads the supplied stream and initializes the manifest file class
        /// </summary>
        /// <param name="source">The stream to read the manifest from</param>
        public Manifestfile(System.IO.Stream source)
            : this()
        {
            Read(source);
        }

        /// <summary>
        /// Reads the manifest document
        /// </summary>
        /// <param name="s">The stream to read the manifest from</param>
        public void Read(string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                Read(fs);
        }

        /// <summary>
        /// Reads the manifest document
        /// </summary>
        /// <param name="s">The stream to read the manifest from</param>
        public void Read(System.IO.Stream s)
        {
            SignatureHashes = new List<string>();
            ContentHashes = new List<string>();

            XmlDocument doc = new XmlDocument();
            doc.Load(s);

            XmlNode root = doc["Manifest"] == null ? doc["ManifestRoot"] : doc["Manifest"];
            if (root == null || root.Attributes["version"] == null)
                throw new Exception(string.Format(Strings.Manifestfile.InvalidManifestError, doc.OuterXml));

            int v;
            if (!int.TryParse(root.Attributes["version"].Value, out v))
                throw new Exception(string.Format(Strings.Manifestfile.InvalidManifestError, doc.OuterXml));

            Version = v;
            if (Version > MaxSupportedVersion)
                throw new Exception(string.Format(Strings.Manifestfile.UnsupportedVersionError, Version, MaxSupportedVersion));

            List<string> paths = new List<string>();
            foreach (XmlNode n in root.SelectNodes("ContentFiles/Hash"))
                ContentHashes.Add(n.InnerText);
            foreach (XmlNode n in root.SelectNodes("SignatureFiles/Hash"))
                SignatureHashes.Add(n.InnerText);
            foreach (XmlNode n in root.SelectNodes("SourcePaths/Path"))
                paths.Add(n.InnerText);

            if (SignatureHashes.Count == 0 || SignatureHashes.Count != ContentHashes.Count)
                throw new Exception(string.Format(Strings.Manifestfile.WrongCountError, SignatureHashes.Count, ContentHashes.Count));
            if (paths.Count == 0)
            {
                if (Version == 1)
                    this.SourceDirs = null;
                else
                    throw new Exception(Strings.Manifestfile.InvalidSourcePathError);
            }
            else
                this.SourceDirs = paths.ToArray();
        }

        /// <summary>
        /// Adds a content and signature hash to the manifest
        /// </summary>
        /// <param name="contenthash">The content hash</param>
        /// <param name="signaturehash">The signature hash</param>
        public void AddEntries(string contenthash, string signaturehash)
        {
            SignatureHashes.Add(signaturehash);
            ContentHashes.Add(contenthash);
        }

        /// <summary>
        /// Saves the current manifest into the given file
        /// </summary>
        /// <param name="filename">The name of the file to write to</param>
        public void Save(string filename)
        {
            using (System.IO.FileStream fs = System.IO.File.Create(filename))
                Save(fs);
        }

        /// <summary>
        /// Saves the current manifest into the given stream
        /// </summary>
        /// <param name="stream">The stream to write the manifest to</param>
        public void Save(System.IO.Stream stream)
        {
            if (Version > MaxSupportedVersion)
                throw new Exception(string.Format(Strings.Manifestfile.UnsupportedVersionError, Version, MaxSupportedVersion));
            if (SignatureHashes.Count == 0 || SignatureHashes.Count != ContentHashes.Count)
                throw new Exception(string.Format(Strings.Manifestfile.WrongCountError, SignatureHashes.Count, ContentHashes.Count));

            XmlDocument doc = new XmlDocument();
            XmlNode root;
            if (Version == 1)
                root = doc.AppendChild(doc.CreateElement("Manifest"));
            else
                root = doc.AppendChild(doc.CreateElement("ManifestRoot"));

            root.Attributes.Append(doc.CreateAttribute("version")).Value = Version.ToString();
            if (Version == 1)
                root.AppendChild(doc.CreateElement("VolumeCount")).InnerText = ContentHashes.Count.ToString();;

            XmlNode contentRoot = root.AppendChild(doc.CreateElement("ContentFiles"));
            XmlNode signatureRoot = root.AppendChild(doc.CreateElement("SignatureFiles"));
            XmlNode sourcePathRoot = root.AppendChild(doc.CreateElement("SourcePaths"));

            foreach (string s in ContentHashes)
                contentRoot.AppendChild(doc.CreateElement("Hash")).InnerText = s;
            foreach (string s in SignatureHashes)
                signatureRoot.AppendChild(doc.CreateElement("Hash")).InnerText = s;
            foreach (string s in SourceDirs)
                sourcePathRoot.AppendChild(doc.CreateElement("Path")).InnerText = s;

            doc.Save(stream);
        }
    }
}
