// Copyright (C) 2025, The Duplicati Team
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

using System;
using System.IO;
using System.Text;

namespace Duplicati.Library.Encryption;

public static class AESStringEncryption
{
    private enum Direction
    {
        Encryption,
        Decryption
    }

    public static string EncryptToHex(string passphrase, string content)
    {
        return Transform(passphrase, content, Direction.Encryption);
    }

    public static string DecryptFromHex(string passphrase, string content)
    {
        return Transform(passphrase, content, Direction.Decryption);
    }

    private static string Transform(string passphrase, string content, Direction direction)
    {
        using var aesprovider = new AESEncryption(passphrase, minimalheader: true);
        using var inputStream = new MemoryStream(direction == Direction.Encryption
            ? Encoding.UTF8.GetBytes(content)
            : Utility.Utility.HexStringAsByteArray(content));
        using var outputStream = new MemoryStream();

        switch (direction)
        {
            case Direction.Encryption:
                aesprovider.Encrypt(inputStream, outputStream);
                return Utility.Utility.ByteArrayAsHexString(outputStream.ToArray());
            case Direction.Decryption:
                aesprovider.Decrypt(inputStream, outputStream);
                return Encoding.UTF8.GetString(outputStream.ToArray());
            default:
                throw new NotImplementedException();
        }
    }
}
