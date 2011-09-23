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
using System.Xml;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// A class that handles manifest serialization and deserialization
    /// </summary>
    public class Manifestfile
    {
        /// <summary>
        /// An entry that describes a hash entry in the manifest file
        /// </summary>
        public class HashEntry
        {
            /// <summary>
            /// The file hash
            /// </summary>
            private string m_hash;
            /// <summary>
            /// The file name
            /// </summary>
            private string m_name;
            /// <summary>
            /// The file size
            /// </summary>
            private long m_size;

            /// <summary>
            /// Gets the file hash
            /// </summary>
            public string Hash { get { return m_hash; } }
            /// <summary>
            /// Gets the file name
            /// </summary>
            public string Name { get { return m_name; } }
            /// <summary>
            /// Gets the file size
            /// </summary>
            public long Size { get { return m_size; } }

            /// <summary>
            /// Constructs a HashEntry from a previously serialized xml node
            /// </summary>
            /// <param name="node">The node to deserialize</param>
            /// <param name="version">The manifest version</param>
            public HashEntry(XmlNode node, int version)
            {
                m_hash = node.InnerText;
                if (version > 2)
                {
                    m_name = node.Attributes["name"].Value;
                    m_size = long.Parse(node.Attributes["size"].Value);
                }
                else
                {
                    m_size = -1;
                    m_name = null;
                }
            }

            /// <summary>
            /// Constructs a HashEntry from a remote file descriptor
            /// </summary>
            /// <param name="item">The remote file to mimic</param>
            public HashEntry(BackupEntryBase item)
            {
                m_hash = item.RemoteHash;
                m_name = item.Filename;
                m_size = item.Filesize;

                if (string.IsNullOrEmpty(m_hash) || m_size < 0 || string.IsNullOrEmpty(m_name))
                    throw new Exception(Strings.Manifestfile.InternalError);
            }

            /// <summary>
            /// Saves the HashEntry to a xml node
            /// </summary>
            /// <param name="node">The node to save to</param>
            public void Save(XmlNode node)
            {
                if (string.IsNullOrEmpty(m_hash) || m_size < 0 || string.IsNullOrEmpty(m_name))
                    throw new Exception(Strings.Manifestfile.InternalError);

                node.InnerText = m_hash;
                node.Attributes.Append(node.OwnerDocument.CreateAttribute("name")).Value = m_name;
                node.Attributes.Append(node.OwnerDocument.CreateAttribute("size")).Value = m_size.ToString();
            }
        }

        /// <summary>
        /// A placeholder for the empty hash value with the same length as a real hash value
        /// </summary>
        private const string EMPTY_HASH_VALUE = "00000000000000000000000000000000000000000000";

		/// <summary>
		///The list of signature hashes 
		/// </summary>
        private List<HashEntry> m_signatureHashes;
		
		/// <summary>
		///The list of content hashes 
		/// </summary>
        private List<HashEntry> m_contentHashes;

        /// <summary>
        /// The manifest hash of the previous manifest file
        /// </summary>
        private string m_previousManifestHash;

        /// <summary>
        /// The filename of the previous manifest file
        /// </summary>
        private string m_previousManifestFilename;

		/// <summary>
		///The list of source dirs 
		/// </summary>
		private string[] m_sourceDirs;
		
		/// <summary>
		///The manifest file version 
		/// </summary>
		private int m_version;

        /// <summary>
        /// The hash algorithm used for content and signature hashes
        /// </summary>
        private string m_hashAlgorithm = Utility.Utility.HashAlgorithm;

        /// <summary>
        /// The self-hash of the manifest
        /// </summary>
        private string m_selfHash = null;

        /// <summary>
        /// The filename of this manifest file
        /// </summary>
        private string m_selfFilename = null;

        /// <summary>
        /// The list of signature hashes
        /// </summary>
        public List<HashEntry> SignatureHashes 
		{ 
			get { return m_signatureHashes; }
			set { m_signatureHashes = value; }
		}

        /// <summary>
        /// The list of content hashes
        /// </summary>
        public List<HashEntry> ContentHashes 
		{ 
			get { return m_contentHashes; }
			set { m_contentHashes = value; }
		}

        /// <summary>
        /// The list of source dirs, where the backups are created from
        /// </summary>
        public string[] SourceDirs 
		{ 
			get { return m_sourceDirs; }
			set { m_sourceDirs = value; }
		}

        /// <summary>
        /// Gets or sets the manifest file version
        /// </summary>
        public int Version 
		{ 
			get { return m_version; }
			set { m_version = value; }
		}

        /// <summary>
        /// Gets or sets the hash algorithm used for verifying the signature and content hashes
        /// </summary>
        public string HashAlgorithm
        {
            get { return m_hashAlgorithm; }
            set { m_hashAlgorithm = value; }
        }

        /// <summary>
        /// Gets the self hash of the file
        /// </summary>
        public string SelfHash
        {
            get { return m_selfHash; }
        }

        /// <summary>
        /// Gets or sets the filename of the current file
        /// </summary>
        public string SelfFilename
        {
            get { return m_selfFilename; }
            set { m_selfFilename = value; }
        }

        /// <summary>
        /// Gets or sets the hash of the previous manifest file
        /// </summary>
        public string PreviousManifestHash
        {
            get { return m_previousManifestHash; }
            set { m_previousManifestHash = value; }
        }

        /// <summary>
        /// Gets or sets the filename of the previous manifest
        /// </summary>
        public string PreviousManifestFilename
        {
            get { return m_previousManifestFilename; }
            set { m_previousManifestFilename = value; }
        }

        /// <summary>
        /// The largest supported version
        /// </summary>
        public const int MaxSupportedVersion = 3;

        /// <summary>
        /// Constructs a blank manifest file
        /// </summary>
        public Manifestfile()
        {
            SignatureHashes = new List<HashEntry>();
            ContentHashes = new List<HashEntry>();
            Version = MaxSupportedVersion;
        }

        /// <summary>
        /// Reads the supplied file and initializes the manifest file class
        /// </summary>
        /// <param name="filename">The file to read the manifest from</param>
        public Manifestfile(string filename, bool skipHashCheck)
            : this()
        {
            Read(filename, skipHashCheck);
        }

        /// <summary>
        /// Reads the supplied stream and initializes the manifest file class
        /// </summary>
        /// <param name="source">The stream to read the manifest from</param>
        public Manifestfile(System.IO.Stream source, bool skipHashCheck)
            : this()
        {
            Read(source, skipHashCheck);
        }

        /// <summary>
        /// Reads the manifest document
        /// </summary>
        /// <param name="s">The stream to read the manifest from</param>
        public void Read(string filename, bool skipHashCheck)
        {
            using (System.IO.FileStream fs = System.IO.File.OpenRead(filename))
                Read(fs, skipHashCheck);
        }

        /// <summary>
        /// Reads the manifest document
        /// </summary>
        /// <param name="s">The stream to read the manifest from</param>
        public void Read(System.IO.Stream s, bool skipHashCheck)
        {
            SignatureHashes = new List<HashEntry>();
            ContentHashes = new List<HashEntry>();

            using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
            {
                Utility.Utility.CopyStream(s, ms);

                ms.Position = 0;
                XmlDocument doc = new XmlDocument();
                doc.Load(ms);
                ms.Position = 0;

                XmlNode root = doc["Manifest"] == null ? doc["ManifestRoot"] : doc["Manifest"];
                if (root == null || root.Attributes["version"] == null)
                    throw new System.IO.InvalidDataException(string.Format(Strings.Manifestfile.InvalidManifestError, doc.OuterXml));

                int v;
                if (!int.TryParse(root.Attributes["version"].Value, out v))
                    throw new System.IO.InvalidDataException(string.Format(Strings.Manifestfile.InvalidManifestError, doc.OuterXml));

                Version = v;
                if (Version > MaxSupportedVersion)
                    throw new System.IO.InvalidDataException(string.Format(Strings.Manifestfile.UnsupportedVersionError, Version, MaxSupportedVersion));

                if (root.Attributes["hash-algorithm"] != null)
                    m_hashAlgorithm = root.Attributes["hash-algorithm"].Value;

                if (m_hashAlgorithm != Utility.Utility.HashAlgorithm)
                    throw new System.IO.InvalidDataException(string.Format(Strings.Manifestfile.UnsupportedHashAlgorithmError, m_hashAlgorithm));

                List<string> paths = new List<string>();
                foreach (XmlNode n in root.SelectNodes("ContentFiles/Hash"))
                    ContentHashes.Add(new HashEntry(n, Version));
                foreach (XmlNode n in root.SelectNodes("SignatureFiles/Hash"))
                    SignatureHashes.Add(new HashEntry(n, Version));
                foreach (XmlNode n in root.SelectNodes("SourcePaths/Path"))
                    paths.Add(n.InnerText);

                if (SignatureHashes.Count == 0 || SignatureHashes.Count != ContentHashes.Count)
                    throw new Exception(string.Format(Strings.Manifestfile.WrongCountError, SignatureHashes.Count, ContentHashes.Count));
                if (paths.Count == 0)
                {
                    if (Version == 1)
                        this.SourceDirs = null;
                    else
                        throw new System.IO.InvalidDataException(Strings.Manifestfile.InvalidSourcePathError);
                }
                else
                    this.SourceDirs = paths.ToArray();

                if (Version > 2)
                {
                    if (root.Attributes["hash"] != null)
                        m_selfHash = root.Attributes["hash"].Value;

                    if (string.IsNullOrEmpty(m_selfHash) || m_selfHash == EMPTY_HASH_VALUE)
                        throw new System.IO.InvalidDataException(Strings.Manifestfile.MainfestIsMissingSelfHash);

                    if (root.Attributes["filename"] != null)
                        m_selfFilename = root.Attributes["filename"].Value;

                    if (string.IsNullOrEmpty(m_selfFilename))
                        throw new System.IO.InvalidDataException(Strings.Manifestfile.MainfestIsMissingSelfFilename);

                    XmlNode manifest = root["PreviousManifest"];
                    if (manifest != null)
                    {
                        if (manifest.Attributes["filename"] != null)
                            m_previousManifestFilename = manifest.Attributes["filename"].Value;
                        if (manifest.Attributes["hash"] != null)
                            m_previousManifestHash = manifest.Attributes["hash"].Value;

                        if (string.IsNullOrEmpty(m_previousManifestFilename))
                            throw new System.IO.InvalidDataException(Strings.Manifestfile.MissingPreviousManifestFilename);
                        if (string.IsNullOrEmpty(m_previousManifestFilename))
                            throw new System.IO.InvalidDataException(Strings.Manifestfile.MissingPreviousManifestHash);
                    }

                    if (!skipHashCheck)
                    {
                        //Validate self-hash
                        ms.Position = 0;
                        SeekToSelfHash(ms);
                        byte[] dummy_value = System.Text.Encoding.UTF8.GetBytes(EMPTY_HASH_VALUE);
                        ms.Write(dummy_value, 0, dummy_value.Length);
                        ms.Position = 0;
                        string selfHash = Utility.Utility.CalculateHash(ms);
                        if (m_selfHash != selfHash)
                            throw new System.IO.InvalidDataException(string.Format(Strings.Manifestfile.SelfhashMismatch, m_selfHash, selfHash));
                    }
                }
                else
                {
                    //For previous versions, we just hash the file
                    m_selfHash = Utility.Utility.CalculateHash(ms);
                }

            }
        }

        /// <summary>
        /// Adds a content and signature hash to the manifest
        /// </summary>
        /// <param name="contenthash">The content hash</param>
        /// <param name="signaturehash">The signature hash</param>
        public void AddEntries(ContentEntry contenthash, SignatureEntry signaturehash)
        {
            SignatureHashes.Add(new HashEntry(signaturehash));
            ContentHashes.Add(new HashEntry(contenthash));
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
                throw new System.IO.InvalidDataException(string.Format(Strings.Manifestfile.UnsupportedVersionError, Version, MaxSupportedVersion));
            if (SignatureHashes.Count == 0 || SignatureHashes.Count != ContentHashes.Count)
                throw new System.IO.InvalidDataException(string.Format(Strings.Manifestfile.WrongCountError, SignatureHashes.Count, ContentHashes.Count));

            XmlDocument doc = new XmlDocument();
            XmlNode root;
            if (Version == 1)
                root = doc.AppendChild(doc.CreateElement("Manifest"));
            else
                root = doc.AppendChild(doc.CreateElement("ManifestRoot"));

            root.Attributes.Append(doc.CreateAttribute("hash-algorithm")).Value = m_hashAlgorithm;
            root.Attributes.Append(doc.CreateAttribute("version")).Value = Version.ToString();
            if (Version == 1)
                root.AppendChild(doc.CreateElement("VolumeCount")).InnerText = ContentHashes.Count.ToString();;

            XmlNode contentRoot = root.AppendChild(doc.CreateElement("ContentFiles"));
            XmlNode signatureRoot = root.AppendChild(doc.CreateElement("SignatureFiles"));
            XmlNode sourcePathRoot = root.AppendChild(doc.CreateElement("SourcePaths"));

            foreach (HashEntry h in ContentHashes)
                h.Save(contentRoot.AppendChild(doc.CreateElement("Hash")));
            foreach (HashEntry h in SignatureHashes)
                h.Save(signatureRoot.AppendChild(doc.CreateElement("Hash")));
            foreach (string s in SourceDirs)
                sourcePathRoot.AppendChild(doc.CreateElement("Path")).InnerText = s;

            if (Version > 2)
            {
                root.Attributes.Append(doc.CreateAttribute("hash")).Value = EMPTY_HASH_VALUE;
                root.Attributes.Append(doc.CreateAttribute("filename")).Value = m_selfFilename;

                if (!string.IsNullOrEmpty(m_previousManifestFilename))
                {
                    XmlNode manifest = root.AppendChild(doc.CreateElement("PreviousManifest"));
                    manifest.Attributes.Append(doc.CreateAttribute("filename")).Value = m_previousManifestFilename;
                    manifest.Attributes.Append(doc.CreateAttribute("hash")).Value = m_previousManifestHash;
                }

                using (System.IO.MemoryStream ms = new System.IO.MemoryStream())
                {
                    doc.Save(ms);
                    ms.Position = 0;
                    string base64hash = Utility.Utility.CalculateHash(ms);
                    byte[] hash = System.Text.Encoding.UTF8.GetBytes(base64hash);
                    ms.Position = 0;
                    SeekToSelfHash(ms);
                    ms.Write(hash, 0, hash.Length);
                    ms.Position = 0;

                    //Verify that we did not break the document!
                    try
                    {
                        XmlDocument testdoc = new XmlDocument();
                        testdoc.Load(ms);
                        if (base64hash != testdoc["ManifestRoot"].Attributes["hash"].Value)
                            throw new Exception(string.Format(Strings.Manifestfile.HashGenerationFailure, testdoc["ManifestRoot"].Attributes["hash"].Value, base64hash));
                    }
                    catch (Exception ex)
                    {
                        throw new System.IO.InvalidDataException(string.Format(Strings.Manifestfile.ManifestValidationError, ex.ToString()), ex);
                    }

                    Utility.Utility.CopyStream(ms, stream, true);
                }
            }
            else
                doc.Save(stream);
        }

        private static readonly byte[] SELF_HASH_SIGNATURE = System.Text.Encoding.UTF8.GetBytes(" hash=\"");
        private static void SeekToSelfHash(System.IO.Stream s)
        {
            int nextIx = 0;
            int v;
            while ((v = s.ReadByte()) != -1)
            {
                if (SELF_HASH_SIGNATURE[nextIx] == (byte)v)
                {
                    nextIx++;
                    if (nextIx >= SELF_HASH_SIGNATURE.Length)
                        return;
                }
                else
                {
                    if ((byte)v == SELF_HASH_SIGNATURE[0])
                        nextIx = 1;
                    else
                        nextIx = 0;
                }
            }

            throw new Exception(Strings.Manifestfile.EofInManifestFile);
        }
    }
}
