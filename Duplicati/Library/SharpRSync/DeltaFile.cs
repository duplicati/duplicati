#region Disclaimer / License
// Copyright (C) 2008, Kenneth Skovhede
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

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// This class contains operations on a RDiff compatible delta file
    /// </summary>
    public class DeltaFile
    {

        ChecksumFile m_checksum;
        System.IO.Stream m_inputStream;

        /// <summary>
        /// Constructs a new DeltaFile based on a signature file.
        /// This instance can be used to create a new DeltaFile
        /// </summary>
        /// <param name="checksum">The checksum to use</param>
        public DeltaFile(ChecksumFile checksum)
        {
            m_checksum = checksum;
        }

        /// <summary>
        /// Constructs a new DeltaFile by reading the stream.
        /// This instance can be used to patch a file.
        /// </summary>
        /// <param name="inputStream">The stream containing the delta information</param>
        public DeltaFile(System.IO.Stream inputStream)
        {
            m_inputStream = inputStream;
        }

        /// <summary>
        /// Creates a new file based on the basefile and the delta information.
        /// The basefile and output stream cannot point to the same resource (ea. file).
        /// The base file MUST be seekable.
        /// </summary>
        /// <param name="basefile">A seekable stream with the baseinformation</param>
        /// <param name="output">The stream to write the patched data to. Must not point to the same resource as the basefile.</param>
        public void PatchFile(System.IO.Stream basefile, System.IO.Stream output)
        {
            byte[] sig = new byte[4];
            if (Utility.ForceStreamRead(m_inputStream, sig, 4) != 4)
                throw new Exception("End of stream occured while reading initial 4 bytes of delta file");
            for (int i = 0; i < sig.Length; i++)
                if (RDiffBinary.DELTA_MAGIC[i] != sig[i])
                    throw new Exception("Delta stream did not have the correct start marker");

            //Keep reading until we hit the end command
            while (true)
            {
                int command = m_inputStream.ReadByte();
                if (command == RDiffBinary.EndCommand)
                    break;
                
                //It is an error to omit the end command
                if (command < 0)
                    throw new Exception("Stream ended but had no end marker");

                if (Enum.IsDefined(typeof(RDiffBinary.LiteralDeltaCommand), (RDiffBinary.LiteralDeltaCommand)command))
                {
                    //Find out how many bytes of literal data there is
                    int len = RDiffBinary.GetLiteralLength((RDiffBinary.LiteralDeltaCommand)command);
                    byte[] tmp = new byte[len];
                    if (Utility.ForceStreamRead(m_inputStream, tmp, tmp.Length) != tmp.Length)
                        throw new Exception("Unexpected end of stream detected");
                    long size = RDiffBinary.DecodeLength(tmp);
                    if (size < 0)
                        throw new Exception("Invalid size for literal data");

                    //Copy the literal data from the patch to the output
                    Utility.StreamCopy(m_inputStream, output, size);
                }
                else if (Enum.IsDefined(typeof(RDiffBinary.CopyDeltaCommand), (RDiffBinary.CopyDeltaCommand)command))
                {
                    //Find the offset of the data in the base file
                    int len = RDiffBinary.GetCopyOffsetSize((RDiffBinary.CopyDeltaCommand)command);
                    byte[] tmp = new byte[len];
                    if (Utility.ForceStreamRead(m_inputStream, tmp, tmp.Length) != tmp.Length)
                        throw new Exception("Unexpected end of stream detected");
                    long offset = RDiffBinary.DecodeLength(tmp);
                    if (offset < 0)
                        throw new Exception("Invalid offset for copy data");

                    //Find the length of the data to copy from the basefile
                    len = RDiffBinary.GetCopyLengthSize((RDiffBinary.CopyDeltaCommand)command);
                    tmp = new byte[len];
                    if (Utility.ForceStreamRead(m_inputStream, tmp, tmp.Length) != tmp.Length)
                        throw new Exception("Unexpected end of stream detected");
                    long length = RDiffBinary.DecodeLength(tmp);
                    if (length < 0)
                        throw new Exception("Invalid length for copy data");

                    //Seek to the begining, and copy
                    basefile.Position = offset;
                    Utility.StreamCopy(basefile, output, length);
                }
                else if (command <= RDiffBinary.LiteralLimit)
                {
                    //Literal data less than 64 bytes are found, copy it
                    Utility.StreamCopy(m_inputStream, output, command);
                }
                else
                    throw new Exception("Unknown command in delta file");
            }
            
            output.Flush();
        }

        /// <summary>
        /// Generates a delta file from input, and writes it to output
        /// </summary>
        /// <param name="input">The stream to generate the delta from</param>
        /// <param name="output">The stream to write the delta to</param>
        public void GenerateDeltaFile(System.IO.Stream input, System.IO.Stream output)
        {
            RollingBuffer buffer = new RollingBuffer(input);
            output.Write(RDiffBinary.DELTA_MAGIC, 0, RDiffBinary.DELTA_MAGIC.Length);
            Adler32Checksum adler = new Adler32Checksum(buffer, m_checksum.BlockLength);
            System.Security.Cryptography.HashAlgorithm md4 = System.Security.Cryptography.MD4.Create("MD4");
            md4.Initialize();

            byte[] md4buffer = new byte[m_checksum.BlockLength];

            long unmatched = 0;
            long matched = 0;
            long matched_offset = 0;
            long next_match_key = 0;

            do
            {
                bool foundMatch = false;
                List<KeyValuePair<int, byte[]>> strong = m_checksum.FindChunk(adler.Checksum);
                
                //No weak matches :(
                if (strong == null || strong.Count == 0)
                    unmatched++;
                else
                {
                    //A weak match, check for MD4 matches
                    byte[] tmp;
                    if (buffer.Count < m_checksum.BlockLength)
                        tmp = buffer.GetHead(buffer.Count);
                    else
                    {
                        buffer.GetHead(md4buffer, 0, md4buffer.Length);
                        tmp = md4buffer;
                    }

                    //If there are two identical blocks, we try the next first as that produces the smallest delta file
                    //NOTE: RDiff does not seem to have this optimization
                    if (matched > 0 && strong[0].Key != next_match_key)
                        foreach (KeyValuePair<int, byte[]> k in strong)
                            if (k.Key == next_match_key)
                            {
                                strong.Remove(k);
                                strong.Insert(0, k);
                                break;
                            }

                    foreach (KeyValuePair<int, byte[]> k in strong)
                    {
                        if (!md4.CanReuseTransform)
                            md4 = System.Security.Cryptography.MD4.Create("MD4");
                        md4.Initialize();

                        tmp = md4.ComputeHash(tmp);
                        bool matches = true;
                        for(int i = 0; i < m_checksum.StrongLength; i++)
                            if (tmp[i] != k.Value[i])
                            {
                                matches = false;
                                break;
                            }

                        if (matches)
                        {
                            //Send leading bytes as a literal block
                            if (unmatched > 0)
                            {
                                WriteLiteral(buffer.GetTail(unmatched), output);
                                buffer.DropTail(unmatched);
                                unmatched = 0;
                            }
                            
                            if (matched == 0)
                                matched_offset = k.Key * m_checksum.BlockLength;
                            else if (k.Key != next_match_key)
                            {
                                WriteCopy(matched_offset, matched, output);
                                matched = 0;
                                matched_offset = k.Key * m_checksum.BlockLength;
                            }

                            next_match_key = k.Key + 1;
                            

                            matched += buffer.Count;
                            foundMatch = true;
                            buffer.DropTail(buffer.Count);
                            break;
                        }
                    }
                }

                //If this byte was not matched, and we have queued up matches,
                // flush them now
                if (!foundMatch && matched > 0)
                {
                    //Send the matching bytes as a copy
                    WriteCopy(matched_offset, matched, output);
                    matched = 0;
                    matched_offset = 0;
                }

                //Avoid keeping too large blocks in memory.
                //This gives an overhead of app. 3 bytes pr. 2Kb data
                if (unmatched >= m_checksum.BlockLength + 1)
                {
                    WriteLiteral(buffer.GetTail(unmatched - 1), output);
                    buffer.DropTail(unmatched - 1);
                    unmatched = 1;
                }

            } while ( buffer.Count == 0 ? adler.Reset() : adler.Rollbuffer());

            //If it is still in the buffer, it was not matched
            unmatched = buffer.Count;

            if (matched > 0 && unmatched > 0)
                throw new Exception("Internal error, had buffered both matched and unmatched blocks!");

            if (matched > 0)
            {
                //Send the matching bytes as a copy
                WriteCopy(matched_offset, matched, output);
            }

            //Any trailing bytes are treated as a literal
            if (unmatched > 0)
            {
                WriteLiteral(buffer.GetTail(unmatched), output);
                buffer.DropTail(unmatched);
            }

            output.WriteByte((byte)RDiffBinary.EndCommand);
            output.Flush();
        }

        /// <summary>
        /// Writes a literal command to a delta stream
        /// </summary>
        /// <param name="data">The literal data to write</param>
        /// <param name="output">The output delta stream</param>
        private void WriteLiteral(byte[] data, System.IO.Stream output)
        {
            output.WriteByte((byte)RDiffBinary.FindLiteralDeltaCommand(data.Length));
            byte[] len = RDiffBinary.EncodeLength(data.Length);
            output.Write(len, 0, len.Length);
            
            output.Write(data, 0, data.Length);
        }

        /// <summary>
        /// Write a copy command to a delta stream
        /// </summary>
        /// <param name="offset">The offset in the basefile where the data is located</param>
        /// <param name="length">The length of the data to copy</param>
        /// <param name="output">The output delta stream</param>
        private void WriteCopy(long offset, long length, System.IO.Stream output)
        {
            output.WriteByte((byte)RDiffBinary.FindCopyDeltaCommand(offset, length));
            byte[] len = RDiffBinary.EncodeLength(offset);
            output.Write(len, 0, len.Length);
            len = RDiffBinary.EncodeLength(length);
            output.Write(len, 0, len.Length);
        }
    }
}
