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
using System.Text.RegularExpressions;

namespace Duplicati.Library.Main
{
    /// <summary>
    /// This class is responsible for naming remote files,
    /// and parsing remote filenames
    /// </summary>
    internal class FilenameStrategy
    {

        //In Duplicati 1.0 there is only one manifest file, and it is called manifest or M in short mode
        //Issue# 58 introduced a backup manifest. Since 1.0 does not verify the manifest version number,
        // the name manifest/M is not used, which prevents 1.0 clients from reading the manifests and volumes.
        //The new names are manifestA and manifestB (A/B in short mode).
        //Future versions of Duplicati always verify the version number to avoid reading/writing data to a format 
        // that is not supported.
        //
        //These are the strings that are used:

        private const string MANIFEST_OLD = "manifest";
        private const string MANIFEST_A = "manifestA";
        private const string MANIFEST_B = "manifestB";

        private const string MANIFEST_OLD_SHORT = "M";
        private const string MANIFEST_A_SHORT = "A";
        private const string MANIFEST_B_SHORT = "B";

        private const string CONTENT = "content";
        private const string SIGNATURE = "signature";
        private const string FULL = "full";
        private const string INCREMENTAL = "inc";

        private const string CONTENT_SHORT = "C";
        private const string SIGNATURE_SHORT = "S";
        private const string FULL_SHORT = "F";
        private const string INCREMENTAL_SHORT = "I";

        private const string VOLUME = "vol";

        private const string DELETE_TRANSACTION_FILENAME = "delete.transaction";

        //Note: Actually the K should be Z which is more correct as it is forced to be Z, but Z as a format specifier is fairly undocumented
        private const string TIMESTAMP_FORMAT = "yyyyMMdd'T'HHmmssK";

        private bool m_useOldFilenames;
        private bool m_useShortFilenames;
        private string m_timeSeparator;
        private string m_prefix;

        private readonly Regex m_oldFilenameRegExp;
        private readonly Regex m_filenameRegExp;
        private readonly Regex m_shortRegExp;
        private readonly Regex m_verificationRegExp;
        private readonly Regex m_deleteTransactionRegExp;

        /// <summary>
        /// A cache used to ensure that filenames are consistent, 
        /// even if the timezone changes in between filename generation
        /// </summary>
        private Dictionary<DateTime, string> m_timeStringCache;

        public FilenameStrategy(string prefix)
            : this(prefix, null, false, false)
        {
        }

        public FilenameStrategy(string prefix, string timeSeparator, bool useShortNames, bool oldFilenames)
        {
            m_prefix = prefix;
            m_timeSeparator = timeSeparator;
            m_useShortFilenames = useShortNames;
            m_useOldFilenames = oldFilenames;

            m_shortRegExp = new Regex(
                string.Format(@"(?<prefix>{0})\-(?<type>({1}|{2}|{3}|{4}|{5}))(?<inc>({6}|{7}))(?<time>([A-F]|[a-f]|[0-9])+)\.(?<volumegroup>{8}(?<volumenumber>\d+)\.)?(?<extension>.+)",
                    Regex.Escape(m_prefix),
                    CONTENT_SHORT,
                    SIGNATURE_SHORT,
                    MANIFEST_OLD_SHORT,
                    MANIFEST_A_SHORT,
                    MANIFEST_B_SHORT,
                    FULL_SHORT,
                    INCREMENTAL_SHORT,
                    VOLUME
                )
            );

            //As we the --time-separator is now deprecated, we must guess what was used
            string timeSepRegEx;
            if (m_timeSeparator == null)
                timeSepRegEx = "."; //We accept any character
            else
                timeSepRegEx = Regex.Escape(m_timeSeparator);

            
            m_oldFilenameRegExp = new Regex(
                string.Format(@"(?<prefix>{0})\-(?<inc>({1}|{2}))\-(?<type>({3}|{4}|{5}|{6}|{7}))\.(?<time>\d{10}\-\d{11}\-\d{11}.\d{11}(?<timeseparator>{9})\d{11}{9}\d{11}(?<timezone>([\+\-]\d{11}{9}\d{11})|Z)?)\.(?<volumegroup>{8}(?<volumenumber>\d+)\.)?(?<extension>.+)",
                    Regex.Escape(m_prefix),
                    FULL,
                    INCREMENTAL,
                    CONTENT,
                    SIGNATURE,
                    MANIFEST_OLD,
                    MANIFEST_A,
                    MANIFEST_B,
                    VOLUME,
                    timeSepRegEx,
                    "{4}", //We need to insert it because it gets replaced by string.Format
                    "{2}" //Same as above
                )
            );

            m_filenameRegExp = new Regex(
                string.Format(@"(?<prefix>{0})\-(?<inc>({1}|{2}))\-(?<type>({3}|{4}|{5}|{6}|{7}))\.(?<time>{9})\.(?<volumegroup>{8}(?<volumenumber>\d+)\.)?(?<extension>.+)",
                    Regex.Escape(m_prefix),
                    FULL,
                    INCREMENTAL,
                    CONTENT,
                    SIGNATURE,
                    MANIFEST_OLD,
                    MANIFEST_A,
                    MANIFEST_B,
                    VOLUME,
                    @"\d{8}T\d{6}Z" //Timestamp format is YYYYMMDDTHHMMSSZ
                )
            );

            m_verificationRegExp = new Regex(
                string.Format(@"(?<prefix>{0})\.(?<time>{1}).verification",
                    Regex.Escape(m_prefix),
                    @"\d{8}T\d{6}Z" //Timestamp format is YYYYMMDDTHHMMSSZ
                )
            );

            m_deleteTransactionRegExp = new Regex(
                string.Format(@"(?<prefix>{0})-{1}(\.(?<encryption>[^\.]+))?",
                    System.Text.RegularExpressions.Regex.Escape(m_prefix),
                    DELETE_TRANSACTION_FILENAME
                )
            );

            //The short filenames and new filenames are UTC so there is no timezone attached
            if (!m_useShortFilenames && m_useOldFilenames)
                m_timeStringCache = new Dictionary<DateTime, string>();
        }

        public FilenameStrategy(Options options)
            : this(options.BackupPrefix, options.TimeSeparatorChar, options.UseShortFilenames, options.UseOldFilenames)
        {
        }

        public string GenerateFilename(BackupEntryBase type)
        {
            string t;
            if (type is ManifestEntry && ((ManifestEntry)type).IsPrimary)
                t = m_useShortFilenames ? MANIFEST_A_SHORT : MANIFEST_A;
            else if (type is ManifestEntry && !((ManifestEntry)type).IsPrimary)
                t = m_useShortFilenames ? MANIFEST_B_SHORT : MANIFEST_B;
            else if (type is ContentEntry)
                t = m_useShortFilenames ? CONTENT_SHORT : CONTENT;
            else if (type is SignatureEntry)
                t = m_useShortFilenames ? SIGNATURE_SHORT : SIGNATURE;
            else if (type is VerificationEntry)
                return m_prefix + "." + type.Time.ToUniversalTime().ToString(TIMESTAMP_FORMAT) + ".verification";
            else if (type is DeleteTransactionEntry)
                return m_prefix + "-" + DELETE_TRANSACTION_FILENAME;
            else
                throw new Exception(string.Format(Strings.FilenameStrategy.InvalidEntryTypeError, type));

            string filename;
            if (m_useShortFilenames)
            {
                filename = m_prefix + "-" + t + (type.IsFull ? FULL_SHORT : INCREMENTAL_SHORT) + (type.Time.ToUniversalTime().Ticks / TimeSpan.TicksPerSecond).ToString("X");
            }
            else
            {
                string datetime;
                if (m_useOldFilenames)
                {
                    //Make sure the same DateTime is always the same string
                    if (!m_timeStringCache.ContainsKey(type.Time))
                        m_timeStringCache[type.Time] = type.Time.ToString("yyyy-MM-ddTHH:mm:ssK");
                    datetime = m_timeStringCache[type.Time].Replace(":", m_timeSeparator ?? (Utility.Utility.IsClientLinux ? ":" : "'"));
                }
                else
                {
                    datetime = type.Time.ToUniversalTime().ToString(TIMESTAMP_FORMAT);
                }

                filename = m_prefix + "-" + (type.IsFull ? FULL : INCREMENTAL) + "-" + t + "." + datetime;
            }

            if (type is ManifestEntry)
                return filename;
            else
                return filename + "." + VOLUME + ((PayloadEntryBase)type).Volumenumber.ToString();
        }

        public BackupEntryBase ParseFilename(Duplicati.Library.Interface.IFileEntry fe)
        {
            bool oldFilename = false;
            Match m = m_filenameRegExp.Match(fe.Name);
            if (!m.Success)
                m = m_shortRegExp.Match(fe.Name);
            if (!m.Success)
            {
                m = m_oldFilenameRegExp.Match(fe.Name);
                oldFilename = true;
            }
            if (!m.Success)
            {
                m = m_verificationRegExp.Match(fe.Name);
                if (m.Success && m.Value == fe.Name)
                {
                    DateTime verificationtime = DateTime.ParseExact(m.Groups["time"].Value, TIMESTAMP_FORMAT, System.Globalization.CultureInfo.InvariantCulture).ToLocalTime();
                    return new VerificationEntry(fe.Name, fe, verificationtime, m.Groups["time"].Value);
                }
                
                m = m_deleteTransactionRegExp.Match(fe.Name);
                if (m.Success && m.Value == fe.Name)
                {
                    return new DeleteTransactionEntry(fe, m.Groups["encryption"].Value);
                }
            }

            if (!m.Success)
                return null;
            if (m.Value != fe.Name)
                return null; //Accept only full matches

            bool isFull = m.Groups["inc"].Value == FULL || m.Groups["inc"].Value == FULL_SHORT;
            bool isShortName = m.Groups["inc"].Value.Length == 1;
            string timeString = m.Groups["time"].Value;
            DateTime time;
            if (isShortName)
                time = new DateTime(long.Parse(timeString, System.Globalization.NumberStyles.HexNumber) * TimeSpan.TicksPerSecond, DateTimeKind.Utc).ToLocalTime();
            else
            {
                if (oldFilename)
                    time = DateTime.Parse(timeString.Replace(m.Groups["timeseparator"].Value, ":"));
                else
                    time = DateTime.ParseExact(timeString, TIMESTAMP_FORMAT, System.Globalization.CultureInfo.InvariantCulture).ToLocalTime();
            }

            string extension = m.Groups["extension"].Value;

            int volNumber = -1;
            if (m.Groups["volumenumber"].Success)
                volNumber = int.Parse(m.Groups["volumenumber"].Value);
            else if (m.Groups["extension"].Success && m.Groups["extension"].Value.StartsWith(VOLUME))
            {
                //The extension must be present, but the volumenumber is optional, so the greedy regexp takes it
                if (!int.TryParse(m.Groups["extension"].Value.Substring(VOLUME.Length), out volNumber))
                    volNumber = -1;
            }

            string compression = extension;
            string encryption = null;

            int dotIndex = compression.IndexOf(".");
            if (dotIndex > 0)
            {
                encryption = compression.Substring(dotIndex + 1);
                compression = compression.Substring(0, dotIndex);
            }

            if (Array.IndexOf<string>(new string[] { MANIFEST_OLD , MANIFEST_OLD_SHORT ,MANIFEST_A , MANIFEST_A_SHORT, MANIFEST_B, MANIFEST_B_SHORT },  m.Groups["type"].Value) >= 0)
                return new ManifestEntry(fe.Name, fe, time, isFull, timeString, encryption, m.Groups["type"].Value != MANIFEST_B && m.Groups["type"].Value != MANIFEST_B_SHORT);
            else if (m.Groups["type"].Value == SIGNATURE || m.Groups["type"].Value == SIGNATURE_SHORT)
                return new SignatureEntry(fe.Name, fe, time, isFull, timeString, encryption, compression, volNumber);
            else if (m.Groups["type"].Value == CONTENT || m.Groups["type"].Value == CONTENT_SHORT)
                return new ContentEntry(fe.Name, fe, time, isFull, timeString, encryption, compression, volNumber);
            else
                return null;
        }

        public bool UseShortNames { get { return m_useShortFilenames; } }
        public string Prefix { get { return m_prefix; } }

        /// <summary>
        /// Parses a filename as a delete transaction.
        /// </summary>
        /// <param name="fe">The file entry to parse</param>
        /// <returns>A delete transaction or null</returns>
        public DeleteTransactionEntry ParseAsDeleteTransaction(Library.Interface.IFileEntry fe)
        {
            System.Text.RegularExpressions.Match m = m_deleteTransactionRegExp.Match(fe.Name);
            if (m.Success && m.Value == fe.Name)
                return new DeleteTransactionEntry(fe, m.Groups["encryption"].Value);

            return null;
        }
    }
}
