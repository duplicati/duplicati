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

namespace Duplicati.SharpRSync
{
    /// <summary>
    /// This class contains various values and methods used for reading and writing
    /// RDiff compatible binary files
    /// </summary>
    public class RDiffBinary
    {
        /// <summary>
        /// A small helper to determine the endianness of the machine
        /// </summary>
        private static readonly byte[] ENDIAN = BitConverter.GetBytes((short)1);
        /// <summary>
        /// The magic header value in an RDiff signature file
        /// </summary>
        public static readonly byte[] SIGNATURE_MAGIC = { (byte)'r', (byte)'s', 0x1, (byte)'6' };
        /// <summary>
        /// The magic header value in an RDiff delta file
        /// </summary>
        public static readonly byte[] DELTA_MAGIC = { (byte)'r', (byte)'s', 0x1, (byte)'6' };

        /// <summary>
        /// Denotes the end of a delta stream.
        /// </summary>
        public const byte EndCommand = 0x0;
        /// <summary>
        /// This is the max number of literal bytes that can be encoded without having the size present.
        /// The delta files created from this library does not contain these, but they can be read.
        /// </summary>
        public static byte LiteralLimit = 0x40;

        /// <summary>
        /// This is a lookup table to find a copy command.
        /// The first index is size of the offset in the original file,
        /// the second index is the size of the length of the block to copy.
        /// </summary>
        public static readonly CopyDeltaCommand[][] CopyCommand =
        { 
            new CopyDeltaCommand[] {CopyDeltaCommand.Copy_Byte_Byte,  CopyDeltaCommand.Copy_Short_Byte,  CopyDeltaCommand.Copy_Int_Byte,  CopyDeltaCommand.Copy_Long_Byte },
            new CopyDeltaCommand[] {CopyDeltaCommand.Copy_Byte_Short, CopyDeltaCommand.Copy_Short_Short, CopyDeltaCommand.Copy_Int_Short, CopyDeltaCommand.Copy_Long_Short},
            new CopyDeltaCommand[] {CopyDeltaCommand.Copy_Byte_Int,   CopyDeltaCommand.Copy_Short_Int,   CopyDeltaCommand.Copy_Int_Int,   CopyDeltaCommand.Copy_Long_Int  },
            new CopyDeltaCommand[] {CopyDeltaCommand.Copy_Byte_Long,  CopyDeltaCommand.Copy_Short_Long,  CopyDeltaCommand.Copy_Int_Long,  CopyDeltaCommand.Copy_Long_Long },
        };

        /// <summary>
        /// Determines the number of bytes required to encode the given size
        /// </summary>
        /// <param name="size">The length to encode</param>
        /// <returns>The number of bytes required</returns>
        public static int FindLength(long size)
        {
            //These limits should be unsigned, but they are read as signed in RDiff?
            int count = 1;
            if (size > byte.MaxValue)
                count++;
            if (size > short.MaxValue)
                count += 2;
            if (size > int.MaxValue)
                count += 4;
            if (size > long.MaxValue)
                throw new Exception("Value must be less than " + long.MaxValue);
            return count;
        }

        /// <summary>
        /// Decodes the size parameter.
        /// </summary>
        /// <param name="data">The data to decode</param>
        /// <returns>The decoded size value</returns>
        public static long DecodeLength(byte[] data)
        {
            if (data.Length == 1)
                return (long)data[0];
            else if (data.Length == 2)
                return (long)BitConverter.ToInt16(FixEndian(data), 0);
            else if (data.Length == 4)
                return (long)BitConverter.ToInt32(FixEndian(data), 0);
            else if (data.Length == 8)
                return BitConverter.ToInt64(FixEndian(data), 0);
            else
                throw new Exception("Invalid data length");
        }

        /// <summary>
        /// Returns an encoded array with the given size value, 
        /// using the least possible number of bytes.
        /// The value is fixed with regards to endianness.
        /// </summary>
        /// <param name="size">The value to write</param>
        /// <returns>The written bytes</returns>
        public static byte[] EncodeLength(int size)
        {
            int len = FindLength(size);
            if (len == 1)
                return FixEndian(BitConverter.GetBytes((byte)size));
            else if (len == 2)
                return FixEndian(BitConverter.GetBytes((short)size));
            else if (len == 4)
                return FixEndian(BitConverter.GetBytes((int)size));
            else if (len == 8)
                return FixEndian(BitConverter.GetBytes((long)size));
            else
                throw new Exception("Encoded value had unexpected length");
        }

        /// <summary>
        /// Returns the correct delta copy command, given the offset and size.
        /// </summary>
        /// <param name="offset">The offset in the file where the copy begins</param>
        /// <param name="size">The size of the copy</param>
        /// <returns>The delta copy command</returns>
        public static CopyDeltaCommand FindCopyDeltaCommand(int offset, int size)
        {
            int i1 = FindLength(offset);
            int i2 = FindLength(size);
            return CopyCommand[i1][i2];
        }

        /// <summary>
        /// Returns the literal delta command for the given size
        /// </summary>
        /// <param name="size">The number that is to be encoded</param>
        /// <returns>The literal delta command</returns>
        public static LiteralDeltaCommand FindLiteralDeltaCommand(int size)
        {
            switch (FindLength(size))
            {
                case 1:
                    return LiteralDeltaCommand.LiteralSizeByte;
                case 2:
                    return LiteralDeltaCommand.LiteralSizeShort;
                case 4:
                    return LiteralDeltaCommand.LiteralSizeInt;
                case 8:
                    return LiteralDeltaCommand.LiteralSizeLong;
                default:
                    throw new Exception("Invalid size for encoded value!");
            }
        }

        /// <summary>
        /// Returns the number of bytes the copy commands offset argument occupies.
        /// </summary>
        /// <param name="command">The copy command to evaluate</param>
        /// <returns>The number of bytes the copy commands offset argument occupies.</returns>
        public static int GetCopyOffsetSize(CopyDeltaCommand command)
        {
            switch (command)
            {
                case CopyDeltaCommand.Copy_Byte_Byte:
                case CopyDeltaCommand.Copy_Byte_Short:
                case CopyDeltaCommand.Copy_Byte_Int:
                case CopyDeltaCommand.Copy_Byte_Long:
                    return 1;
                case CopyDeltaCommand.Copy_Short_Byte:
                case CopyDeltaCommand.Copy_Short_Short:
                case CopyDeltaCommand.Copy_Short_Int:
                case CopyDeltaCommand.Copy_Short_Long:
                    return 2;
                case CopyDeltaCommand.Copy_Int_Byte:
                case CopyDeltaCommand.Copy_Int_Short:
                case CopyDeltaCommand.Copy_Int_Int:
                case CopyDeltaCommand.Copy_Int_Long:
                    return 4;
                case CopyDeltaCommand.Copy_Long_Byte:
                case CopyDeltaCommand.Copy_Long_Short:
                case CopyDeltaCommand.Copy_Long_Int:
                case CopyDeltaCommand.Copy_Long_Long:
                    return 8;
                default: 
                    throw new Exception("Invalid delta copy command");
            }
        }

        /// <summary>
        /// Returns the number of bytes the copy commands length argument occupies.
        /// </summary>
        /// <param name="command">The copy command to evaluate</param>
        /// <returns>The number of bytes the copy commands length argument occupies.</returns>
        public static int GetCopyLengthSize(CopyDeltaCommand command)
        {
            switch (command)
            {
                case CopyDeltaCommand.Copy_Byte_Byte:
                case CopyDeltaCommand.Copy_Short_Byte:
                case CopyDeltaCommand.Copy_Int_Byte:
                case CopyDeltaCommand.Copy_Long_Byte:
                    return 1;

                case CopyDeltaCommand.Copy_Byte_Short:
                case CopyDeltaCommand.Copy_Short_Short:
                case CopyDeltaCommand.Copy_Int_Short:
                case CopyDeltaCommand.Copy_Long_Short:
                    return 2;
                case CopyDeltaCommand.Copy_Byte_Int:
                case CopyDeltaCommand.Copy_Short_Int:
                case CopyDeltaCommand.Copy_Int_Int:
                case CopyDeltaCommand.Copy_Long_Int:
                    return 4;
                case CopyDeltaCommand.Copy_Byte_Long:
                case CopyDeltaCommand.Copy_Short_Long:
                case CopyDeltaCommand.Copy_Int_Long:
                case CopyDeltaCommand.Copy_Long_Long:
                    return 8;
                default:
                    throw new Exception("Invalid delta copy command");
            }
        }

        /// <summary>
        /// Returns the number of bytes the literal command's size argument fills
        /// </summary>
        /// <param name="command">The command to find the size for</param>
        /// <returns>The number of bytes the literal command's size argument fills</returns>
        public static int GetLiteralLength(LiteralDeltaCommand command)
        {
            switch (command)
            {
                case LiteralDeltaCommand.LiteralSizeByte:
                    return 1;
                case LiteralDeltaCommand.LiteralSizeShort:
                    return 2;
                case LiteralDeltaCommand.LiteralSizeInt:
                    return 4;
                case LiteralDeltaCommand.LiteralSizeLong:
                    return 8;
                default:
                    throw new Exception("Invalid literal delta command");
            }
        }

        /// <summary>
        /// Reverses endian order, if required
        /// </summary>
        /// <param name="data">The data to reverse</param>
        /// <returns>The reversed data</returns>
        public static byte[] FixEndian(byte[] data)
        {
            if (ENDIAN[0] != 0)
                Array.Reverse(data);
            return data;
        }

        /// <summary>
        /// This table contains all the delta commands for literal data
        /// </summary>
        public enum LiteralDeltaCommand : byte
        {
            /// <summary>
            /// The next byte contains the number of bytes with literal data that follows
            /// </summary>
            LiteralSizeByte = 0x41,
            /// <summary>
            /// The next short contains the number of bytes with literal data that follows
            /// </summary>
            LiteralSizeShort = 0x42,
            /// <summary>
            /// The next integer contains the number of bytes with literal data that follows
            /// </summary>
            LiteralSizeInt = 0x43,
            /// <summary>
            /// The next long contains the number of bytes with literal data that follows
            /// </summary>
            LiteralSizeLong = 0x44,

        }

        /// <summary>
        /// These are the possible commands to specify a copy.
        /// The first item is the offset in the original file,
        /// the second item is the length of the block to copy.
        /// </summary>
        public enum CopyDeltaCommand : byte
        {
            Copy_Byte_Byte = 0x45,
            Copy_Byte_Short = 0x46,
            Copy_Byte_Int = 0x47,
            Copy_Byte_Long = 0x48,

            Copy_Short_Byte = 0x49,
            Copy_Short_Short = 0x4a,
            Copy_Short_Int = 0x4b,
            Copy_Short_Long = 0x4c,

            Copy_Int_Byte = 0x4d,
            Copy_Int_Short = 0x4e,
            Copy_Int_Int = 0x4f,
            Copy_Int_Long = 0x50,

            Copy_Long_Byte = 0x51,
            Copy_Long_Short = 0x52,
            Copy_Long_Int = 0x53,
            Copy_Long_Long = 0x54,
        }


    }
}
