// Copyright (C) 2026, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
//
// Permission is hereby granted, free of charge, to any person obtaining a
// copy of this software and associated documentation files (the "Software"),
// to deal in the Software without restriction, including without limitation
// the rights to use, copy, modify, merge, publish, distribute, sublicense,
// and/or sell copies of the Software, and to permit persons to whom the
// Software is furnished to do so, subject to the following conditions:
//
// The above copyright notice and this permission notice shall be included in
// all copies or substantial portions of the Software.
//
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER
// DEALINGS IN THE SOFTWARE.
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;

namespace ReleaseBuilder;

/// <summary>
/// Creates Windows security catalog (.cat) files.
///
/// A catalog lists the Authenticode hashes of other files. Once the catalog is
/// signed and registered with Windows, code integrity (Smart App Control and
/// App Control for Business) treats the listed files as signed by the catalog
/// signer, without the files themselves being changed.
///
/// The output is the unsigned catalog that makecat and New-FileCatalog produce
/// (a version 2 catalog with SHA256 hashes): a PKCS#7 SignedData without
/// signers, holding a certificate trust list. It is signed afterwards with the
/// same tool as the executables, as osslsigncode, jsign and signtool can all
/// sign catalog files.
/// </summary>
public static class WindowsCatalog
{
    /// <summary>
    /// The name of the catalog in the packages. Duplicati.WindowsService.exe
    /// looks for the catalog under this name next to itself.
    /// </summary>
    public const string CatalogFileName = "Duplicati.cat";

    /// <summary>
    /// A file to list in the catalog
    /// </summary>
    /// <param name="FileName">The name of the file, recorded for information only</param>
    /// <param name="PeImageHash">The SHA256 Authenticode hash of the file</param>
    public sealed record Member(string FileName, byte[] PeImageHash);

    /// <summary>PKCS#7 SignedData</summary>
    private const string SignedDataOid = "1.2.840.113549.1.7.2";
    /// <summary>szOID_CTL, the certificate trust list content type</summary>
    private const string CtlOid = "1.3.6.1.4.1.311.10.1";
    /// <summary>szOID_CATALOG_LIST, the subject usage of a catalog</summary>
    private const string CatalogListOid = "1.3.6.1.4.1.311.12.1.1";
    /// <summary>szOID_CATALOG_LIST_MEMBER2, the member type of a version 2 catalog</summary>
    private const string CatalogListMember2Oid = "1.3.6.1.4.1.311.12.1.3";
    /// <summary>CAT_NAMEVALUE_OBJID, a named attribute of a member</summary>
    private const string NameValueOid = "1.3.6.1.4.1.311.12.2.1";
    /// <summary>CAT_MEMBERINFO2_OBJID, the member information of a version 2 catalog</summary>
    private const string MemberInfo2Oid = "1.3.6.1.4.1.311.12.2.3";
    /// <summary>SPC_INDIRECT_DATA_OBJID, the hash of the member</summary>
    private const string SpcIndirectDataOid = "1.3.6.1.4.1.311.2.1.4";
    /// <summary>SPC_PE_IMAGE_DATA_OBJID, marks the hash as the hash of a PE image</summary>
    private const string SpcPeImageDataOid = "1.3.6.1.4.1.311.2.1.15";
    /// <summary>SHA256</summary>
    private const string Sha256Oid = "2.16.840.1.101.3.4.2.1";

    /// <summary>
    /// The flags makecat writes for a name-value attribute:
    /// CRYPTCAT_ATTR_AUTHENTICATED | CRYPTCAT_ATTR_NAMEASCII | CRYPTCAT_ATTR_DATAASCII
    /// </summary>
    private const int NameValueFlags = 0x10010001;

    /// <summary>
    /// The location of the fields that are excluded from the Authenticode hash
    /// </summary>
    /// <param name="ChecksumOffset">The offset of the CheckSum field in the optional header</param>
    /// <param name="SecurityDirectoryOffset">The offset of the certificate table entry in the data directories</param>
    /// <param name="CertificateTableOffset">The file offset of the certificate table, zero if there is none</param>
    /// <param name="CertificateTableSize">The size of the certificate table, zero if there is none</param>
    private sealed record PeLayout(int ChecksumOffset, int SecurityDirectoryOffset, int CertificateTableOffset, int CertificateTableSize);

    /// <summary>
    /// Reads the parts of the PE headers that the Authenticode hash depends on
    /// </summary>
    /// <param name="data">The contents of the file</param>
    /// <param name="filename">The name of the file, used in error messages</param>
    /// <returns>The layout of the file</returns>
    private static PeLayout ReadPeLayout(byte[] data, string filename)
    {
        if (data.Length < 0x40 || data[0] != 'M' || data[1] != 'Z')
            throw new InvalidDataException($"The file {filename} is not a PE file");

        var peOffset = BitConverter.ToInt32(data, 0x3C);
        if (peOffset < 0 || peOffset > data.Length - 24 || BitConverter.ToUInt32(data, peOffset) != 0x00004550)
            throw new InvalidDataException($"The file {filename} has no PE signature");

        var optionalHeader = peOffset + 24;
        if (optionalHeader + 2 > data.Length)
            throw new InvalidDataException($"The file {filename} has a truncated optional header");

        var magic = BitConverter.ToUInt16(data, optionalHeader);
        var (rvaCountOffset, directoriesOffset) = magic switch
        {
            0x10b => (optionalHeader + 92, optionalHeader + 96),
            0x20b => (optionalHeader + 108, optionalHeader + 112),
            _ => throw new InvalidDataException($"The file {filename} has an unknown optional header type: 0x{magic:x}")
        };

        // Directory 4 is the certificate table, so there must be at least five
        if (directoriesOffset + 5 * 8 > data.Length || BitConverter.ToUInt32(data, rvaCountOffset) < 5)
            throw new InvalidDataException($"The file {filename} has no certificate table entry");

        var securityDirectoryOffset = directoriesOffset + 4 * 8;
        var certificateTableOffset = BitConverter.ToInt32(data, securityDirectoryOffset);
        var certificateTableSize = BitConverter.ToInt32(data, securityDirectoryOffset + 4);

        if (certificateTableSize != 0 && (certificateTableOffset <= securityDirectoryOffset || certificateTableOffset > data.Length - certificateTableSize))
            throw new InvalidDataException($"The file {filename} has an invalid certificate table");

        return new PeLayout(optionalHeader + 64, securityDirectoryOffset, certificateTableOffset, certificateTableSize);
    }

    /// <summary>
    /// Checks if a PE file has an embedded Authenticode signature
    /// </summary>
    /// <param name="data">The contents of the file</param>
    /// <param name="filename">The name of the file, used in error messages</param>
    /// <returns><c>true</c> if the file is signed</returns>
    public static bool HasEmbeddedSignature(byte[] data, string filename)
        => ReadPeLayout(data, filename).CertificateTableSize != 0;

    /// <summary>
    /// Computes the SHA256 Authenticode hash of a PE file, the hash that Windows
    /// looks up in the catalogs.
    ///
    /// The hash covers the whole file except the CheckSum field, the certificate
    /// table entry and the certificate table. An unsigned file is hashed as if it
    /// was padded with zeros to a multiple of 8 bytes, as signing would do; the
    /// catalogs created by Windows show that it does the same for catalog members.
    /// </summary>
    /// <param name="data">The contents of the file</param>
    /// <param name="filename">The name of the file, used in error messages</param>
    /// <returns>The hash</returns>
    public static byte[] ComputePeImageHash(byte[] data, string filename)
    {
        var layout = ReadPeLayout(data, filename);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);

        var afterChecksum = layout.ChecksumOffset + 4;
        var afterSecurityDirectory = layout.SecurityDirectoryOffset + 8;

        hash.AppendData(data, 0, layout.ChecksumOffset);
        hash.AppendData(data, afterChecksum, layout.SecurityDirectoryOffset - afterChecksum);

        if (layout.CertificateTableSize == 0)
        {
            hash.AppendData(data, afterSecurityDirectory, data.Length - afterSecurityDirectory);
            var padding = (8 - data.Length % 8) % 8;
            if (padding != 0)
                hash.AppendData(new byte[padding]);
        }
        else
        {
            hash.AppendData(data, afterSecurityDirectory, layout.CertificateTableOffset - afterSecurityDirectory);
            var afterCertificateTable = layout.CertificateTableOffset + layout.CertificateTableSize;
            hash.AppendData(data, afterCertificateTable, data.Length - afterCertificateTable);
        }

        return hash.GetHashAndReset();
    }

    /// <summary>
    /// Creates an unsigned catalog that lists the given PE files
    /// </summary>
    /// <param name="members">The files to list; files with the same hash are listed once</param>
    /// <param name="created">The creation time recorded in the catalog</param>
    /// <param name="listIdentifier">The identifier recorded in the catalog</param>
    /// <returns>The DER encoded catalog</returns>
    public static byte[] Create(IEnumerable<Member> members, DateTimeOffset created, Guid listIdentifier)
    {
        // Windows sorts the members by hash. Files with the same contents share a
        // hash, and the hash is the member tag, so each hash is listed once.
        var sortedMembers = members
            .DistinctBy(x => Convert.ToHexString(x.PeImageHash))
            .OrderBy(x => x.PeImageHash, Comparer<byte[]>.Create((a, b) => a.AsSpan().SequenceCompareTo(b)))
            .ToList();

        var writer = new AsnWriter(AsnEncodingRules.DER);

        // ContentInfo
        using (writer.PushSequence())
        {
            writer.WriteObjectIdentifier(SignedDataOid);
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
            // SignedData
            using (writer.PushSequence())
            {
                writer.WriteInteger(1);
                // No digest algorithms until it is signed
                using (writer.PushSetOf()) { }

                // The content is written without an OCTET STRING wrapper, as PKCS#7 does it
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(CtlOid);
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                        WriteCertificateTrustList(writer, sortedMembers, created, listIdentifier);
                }

                // No signers until it is signed
                using (writer.PushSetOf()) { }
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// Writes the certificate trust list, which holds the catalog members
    /// </summary>
    /// <param name="writer">The writer to use</param>
    /// <param name="members">The members, sorted by hash</param>
    /// <param name="created">The creation time recorded in the catalog</param>
    /// <param name="listIdentifier">The identifier recorded in the catalog</param>
    private static void WriteCertificateTrustList(AsnWriter writer, IReadOnlyList<Member> members, DateTimeOffset created, Guid listIdentifier)
    {
        using (writer.PushSequence())
        {
            using (writer.PushSequence())
                writer.WriteObjectIdentifier(CatalogListOid);

            writer.WriteOctetString(listIdentifier.ToByteArray());
            writer.WriteUtcTime(created);

            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(CatalogListMember2Oid);
                writer.WriteNull();
            }

            using (writer.PushSequence())
                foreach (var member in members)
                    WriteMember(writer, member);
        }
    }

    /// <summary>
    /// Writes a single catalog member, laid out as makecat writes a PE file
    /// </summary>
    /// <param name="writer">The writer to use</param>
    /// <param name="member">The member to write</param>
    private static void WriteMember(AsnWriter writer, Member member)
    {
        if (member.PeImageHash.Length != 32)
            throw new ArgumentException($"Expected a SHA256 hash for {member.FileName}", nameof(member));

        using (writer.PushSequence())
        {
            // In a version 2 catalog the member tag is the hash itself
            writer.WriteOctetString(member.PeImageHash);

            using (writer.PushSetOf())
            {
                // Member information: [0] marks a PE file
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(MemberInfo2Oid);
                    using (writer.PushSetOf())
                        writer.WriteNull(new Asn1Tag(TagClass.ContextSpecific, 0));
                }

                // The file name, for information
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(NameValueOid);
                    using (writer.PushSetOf())
                    using (writer.PushSequence())
                    {
                        writer.WriteCharacterString(UniversalTagNumber.BMPString, "File");
                        writer.WriteInteger(NameValueFlags);
                        writer.WriteOctetString(Encoding.Unicode.GetBytes(member.FileName + "\0"));
                    }
                }

                // The hash, in the same SpcIndirectDataContent form as an embedded signature
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(SpcIndirectDataOid);
                    using (writer.PushSetOf())
                        WriteSpcIndirectDataContent(writer, member.PeImageHash);
                }
            }
        }
    }

    /// <summary>
    /// Writes the SpcIndirectDataContent for a PE file hash
    /// </summary>
    /// <param name="writer">The writer to use</param>
    /// <param name="hash">The SHA256 Authenticode hash</param>
    private static void WriteSpcIndirectDataContent(AsnWriter writer, byte[] hash)
    {
        using (writer.PushSequence())
        {
            // SpcAttributeTypeAndOptionalValue
            using (writer.PushSequence())
            {
                writer.WriteObjectIdentifier(SpcPeImageDataOid);
                // SpcPeImageData
                using (writer.PushSequence())
                {
                    // The flags makecat writes: includeResources and includeImportAddressTable
                    writer.WriteBitString([0xA0], 5);
                    // file [0] SpcLink, as the obsolete file name that signtool also writes
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, true)))
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 2, true)))
                        writer.WriteCharacterString(UniversalTagNumber.BMPString, "<<<Obsolete>>>", new Asn1Tag(TagClass.ContextSpecific, 0));
                }
            }

            // DigestInfo
            using (writer.PushSequence())
            {
                using (writer.PushSequence())
                {
                    writer.WriteObjectIdentifier(Sha256Oid);
                    writer.WriteNull();
                }
                writer.WriteOctetString(hash);
            }
        }
    }
}
