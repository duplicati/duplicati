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

namespace Duplicati.Library.SharpRSync
{
    /// <summary>
    /// This class contains operations on a RDiff compatible delta file
    /// </summary>
    public class DeltaFile
    {
        /// <summary>
        /// The size of the internal buffer used to read in data
        /// </summary>
        private const int BUFFER_SIZE = 100 * 1024;

        /// <summary>
        /// The ChecksumFileReader used to perform signature lookups
        /// </summary>
        ChecksumFileReader m_checksum;

        /// <summary>
        /// The possibly modified input data
        /// </summary>
        System.IO.Stream m_inputStream;

        /// <summary>
        /// Constructs a new DeltaFile based on a signature file.
        /// This instance can be used to create a new DeltaFile
        /// </summary>
        /// <param name="checksum">The checksum to use</param>
        public DeltaFile(ChecksumFileReader checksum)
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
            //Validate the file header
            byte[] sig = new byte[4];
            if (Utility.ForceStreamRead(m_inputStream, sig, 4) != 4)
                throw new Exception(Strings.DeltaFile.EndofstreamBeforeSignatureError);
            for (int i = 0; i < sig.Length; i++)
                if (RDiffBinary.DELTA_MAGIC[i] != sig[i])
                    throw new Exception(Strings.DeltaFile.InvalidSignatureError);

            //Keep reading until we hit the end command
            while (true)
            {
                int command = m_inputStream.ReadByte();
                if (command == RDiffBinary.EndCommand)
                    break;
                
                //It is an error to omit the end command
                if (command < 0)
                    throw new Exception(Strings.DeltaFile.EndofstreamWithoutMarkerError);

                if (Enum.IsDefined(typeof(RDiffBinary.LiteralDeltaCommand), (RDiffBinary.LiteralDeltaCommand)command))
                {
                    //Find out how many bytes of literal data there is
                    int len = RDiffBinary.GetLiteralLength((RDiffBinary.LiteralDeltaCommand)command);
                    byte[] tmp = new byte[len];
                    if (Utility.ForceStreamRead(m_inputStream, tmp, tmp.Length) != tmp.Length)
                        throw new Exception(Strings.DeltaFile.UnexpectedEndofstreamError);
                    long size = RDiffBinary.DecodeLength(tmp);
                    if (size < 0)
                        throw new Exception(Strings.DeltaFile.InvalidLitteralSizeError);

                    //Copy the literal data from the patch to the output
                    Utility.StreamCopy(m_inputStream, output, size);
                }
                else if (Enum.IsDefined(typeof(RDiffBinary.CopyDeltaCommand), (RDiffBinary.CopyDeltaCommand)command))
                {
                    //Find the offset of the data in the base file
                    int len = RDiffBinary.GetCopyOffsetSize((RDiffBinary.CopyDeltaCommand)command);
                    byte[] tmp = new byte[len];
                    if (Utility.ForceStreamRead(m_inputStream, tmp, tmp.Length) != tmp.Length)
                        throw new Exception(Strings.DeltaFile.UnexpectedEndofstreamError);
                    long offset = RDiffBinary.DecodeLength(tmp);
                    if (offset < 0)
                        throw new Exception(Strings.DeltaFile.InvalidCopyOffsetError);

                    //Find the length of the data to copy from the basefile
                    len = RDiffBinary.GetCopyLengthSize((RDiffBinary.CopyDeltaCommand)command);
                    tmp = new byte[len];
                    if (Utility.ForceStreamRead(m_inputStream, tmp, tmp.Length) != tmp.Length)
                        throw new Exception(Strings.DeltaFile.UnexpectedEndofstreamError);
                    long length = RDiffBinary.DecodeLength(tmp);
                    if (length < 0)
                        throw new Exception(Strings.DeltaFile.InvalidCopyLengthError);

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
                    throw new Exception(Strings.DeltaFile.UnknownCommandError);
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
            output.Write(RDiffBinary.DELTA_MAGIC, 0, RDiffBinary.DELTA_MAGIC.Length);

            int blocklength = m_checksum.BlockLength;
            int buffersize = BUFFER_SIZE;

            //If we have some insanely large blocks, try to handle them nicely anyway
            if (blocklength > BUFFER_SIZE / 2)
                buffersize = blocklength * 4;

            //The number of matched bytes
            long matched = 0;
            //The first matched byte
            long matched_offset = 0;
            //The index of the next expected block
            long next_match_key = 0;

            //The number of unmatched bytes
            int unmatched = 0;
            //The index of the first unmatched byte
            int unmatched_offset = 0;

            //Keep a local copy of the lookup table
            bool[] weakLookup = m_checksum.WeakLookup;

            //We use statically allocated buffers, and we need two buffers
            // to prevent Array.Copy from allocating a temp buffer
            byte[] working_data = new byte[BUFFER_SIZE];
            byte[] temp_work = new byte[BUFFER_SIZE];
            byte[] md4buf = new byte[blocklength];

            //Read the initial buffer block
            int buffer_len = Utility.ForceStreamRead(input, working_data);
            int buffer_index = 0;
            blocklength = Math.Min(blocklength, buffer_len);

            //Setup the initial checksum
            uint weakChecksum = Adler32Checksum.Calculate(working_data, 0, blocklength);

            long indexMatched = -1;
            bool force_buffer_refill = false;
            bool recalculate_weak_checksum = false;
            bool streamExhausted = false;

            while (blocklength > 0)
            {
                //Check if the block matches somewhere, if we have force-reloaded the buffer, the check has already been made
                if (force_buffer_refill)
                    force_buffer_refill = false;
                else
                    indexMatched = m_checksum.LookupChunck(weakChecksum, working_data, buffer_index, blocklength, next_match_key);
                
                if (indexMatched >= 0)
                {
                    //We have a match, flush unmatched
                    if (unmatched > 0)
                    {
                        WriteLiteral(working_data, unmatched_offset, unmatched, output);
                        unmatched = 0;
                    }

                    //First match
                    if (matched == 0)
                    {
                        matched_offset = indexMatched * blocklength;
                    }
                    else if (indexMatched != next_match_key)
                    {
                        //Subsequent match, but the sequence does not fit
                        WriteCopy(matched_offset, matched, output);

                        //Pretend this was the fist
                        matched = 0;
                        matched_offset = indexMatched * blocklength;
                    }

                    //If the next block matches this signature, we can write larger
                    // copy instructions and thus safe space
                    next_match_key = indexMatched + 1;

                    //Adjust the counters
                    matched += blocklength;
                    buffer_index += blocklength;

                    if (buffer_len - buffer_index < blocklength)
                    {
                        //If this is the last chunck, compare to the last hash
                        if (temp_work == null)
                            blocklength = Math.Min(blocklength, buffer_len - buffer_index);
                        else //We are out of buffer, reload
                            recalculate_weak_checksum = true;
                    }

                    //Reset the checksum to fit the new block, but skip it if we are out of data
                    if (!recalculate_weak_checksum)
                        weakChecksum = Adler32Checksum.Calculate(working_data, buffer_index, blocklength);
                }
                else
                {
                    //At this point we have not advanced the buffer_index, so the weak_checksum matches the data,
                    // even if we arrive here after reloading the buffer

                    //No match, flush accumulated matches, if any
                    if (matched > 0)
                    {
                        //Send the matching bytes as a copy
                        WriteCopy(matched_offset, matched, output);
                        matched = 0;
                        matched_offset = 0;

                        //We do not immediately start tapping the unmatched bytes, 
                        // because the buffer may be nearly empty, and we 
                        // want to gather as many unmatched bytes as possible
                        // to avoid the instruction overhead in the file
                        if (buffer_index != 0)
                            force_buffer_refill = true;
                    }
                    else
                    {
                        int lastPossible = buffer_len - blocklength;
                        if (unmatched == 0)
                            unmatched_offset = buffer_index;

                        //Local speedup for long non-matching regions
                        while (buffer_index < lastPossible)
                        {
                            //Roll the weak checksum buffer by 1 byte
                            weakChecksum = Adler32Checksum.Roll(working_data[buffer_index], working_data[buffer_index + blocklength], weakChecksum, blocklength);
                            buffer_index++;

                            if (weakLookup[weakChecksum >> 16])
                                break;
                        }

                        unmatched = buffer_index - unmatched_offset;

                        //If this is the last block, claim the remaining bytes as unmatched
                        if (temp_work == null)
                        {
                            //There may be a minor optimization possible here, as the last chunk of the original file may still fit 
                            // and be smaller than the block length

                            unmatched += blocklength;
                            blocklength = 0;
                        }
                    }
                }

                //If we are out of buffer, try to load some more
                if (force_buffer_refill || buffer_len - buffer_index <= m_checksum.BlockLength)
                {
                    //The number of unused bytes the the buffer
                    int remaining_bytes = buffer_len - buffer_index;

                    //If we have read the last bytes or the buffer is already full, skip this
                    if (temp_work != null && temp_work.Length - remaining_bytes > 0)
                    {
                        Array.Copy(working_data, buffer_index, temp_work, 0, remaining_bytes);

                        //Prevent reading the stream after it has been exhausted because some streams break on that
                        int tempread =  
                            streamExhausted ? 0 : Utility.ForceStreamRead(input, temp_work, remaining_bytes, temp_work.Length - remaining_bytes);

                        if (tempread > 0)
                        {
                            //We are about to discard some data, if it is unmatched, write it to stream
                            if (unmatched > 0)
                            {
                                WriteLiteral(working_data, unmatched_offset, unmatched, output);
                                unmatched = 0;
                            }

                            //Now swap the arrays
                            byte[] tmp = working_data;
                            working_data = temp_work;
                            temp_work = tmp;

                            buffer_index = 0;
                            buffer_len = remaining_bytes + tempread;
                        }
                        else 
                        {
                            //Prevent reading the stream after it has been exhausted because some streams break on that
                            streamExhausted = true;

                            if (remaining_bytes <= m_checksum.BlockLength)
                            {
                                //Mark as done
                                temp_work = null;

                                //The last round has a smaller block length
                                blocklength = remaining_bytes;
                            }

                        }

                        //If we run out of buffer, we may need to recalculate the checksum
                        if (recalculate_weak_checksum)
                        {
                            weakChecksum = Adler32Checksum.Calculate(working_data, buffer_index, blocklength);
                            recalculate_weak_checksum = false;
                        }

                    }
                }
            }

            //There cannot be both matched and unmatched bytes written
            if (matched > 0 && unmatched > 0)
                throw new Exception(Strings.DeltaFile.InternalBufferError);

            if (matched > 0)
                WriteCopy(matched_offset, matched, output);

            if (unmatched > 0)
                WriteLiteral(working_data, unmatched_offset, unmatched, output);

            output.WriteByte((byte)RDiffBinary.EndCommand);
            output.Flush();
        }

        /// <summary>
        /// Writes a literal command to a delta stream
        /// </summary>
        /// <param name="data">The literal data to write</param>
        /// <param name="output">The output delta stream</param>
        private void WriteLiteral(byte[] data, int offset, int count, System.IO.Stream output)
        {
            output.WriteByte((byte)RDiffBinary.FindLiteralDeltaCommand(count));
            byte[] len = RDiffBinary.EncodeLength(count);
            output.Write(len, 0, len.Length);
            output.Write(data, offset, count);
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
