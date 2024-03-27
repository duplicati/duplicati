// Copyright (C) 2024, The Duplicati Team
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
using System.Collections.Generic;

namespace Duplicati.Library.Utility
{
    public class SignatureReadingStream : System.IO.Stream, IDisposable
    {
        /// <summary>
        /// The size of the SHA256 output hash in bytes
        /// </summary>
        /// 
        internal const int SIGNED_HASH_SIZE = 128;

        /// <summary>
        /// The stream to read from
        /// </summary>
        private readonly System.IO.Stream m_stream;

        protected SignatureReadingStream()
        {
        }

        /// <summary>
        /// Creates a new stream that reads from the given stream and verifies the signature using any of the given keys
        /// </summary>
        /// <param name="stream">The stream with a signature</param>
        /// <param name="keys">The allowed keys</param>
        public SignatureReadingStream(System.IO.Stream stream, IEnumerable<System.Security.Cryptography.RSACryptoServiceProvider> keys)
        {
            if (!VerifySignature(stream, keys))
                throw new System.IO.InvalidDataException("Unable to verify signature");
            m_stream = stream;
            this.Position = 0;
        }

        /// <summary>
        /// Wraps trying the keys one by one, returning true if any of the keys validate
        /// </summary>
        /// <param name="stream">The stream to verify</param>
        /// <param name="keys">The keys to try</param>
        /// <returns><c>true</c> if the stream is valid; <c>false</c> otherwise</returns>
        private static bool VerifySignature(System.IO.Stream stream, IEnumerable<System.Security.Cryptography.RSACryptoServiceProvider> keys)
        {
            if (keys == null)
                return false;

            foreach (var key in keys)
                try
                {
                    if (VerifySignature(stream, key))
                        return true;
                }
                catch
                {
                }

            return false;
        }

        /// <summary>
        /// Verifies the signature of the stream using the given key
        /// </summary>
        /// <param name="stream">The stream to verify</param>
        /// <param name="key">The key to validate with</param>
        /// <returns><c>true</c> if the stream signature matches the key; <c>false</c> otherwise</returns>
        private static bool VerifySignature(System.IO.Stream stream, System.Security.Cryptography.RSACryptoServiceProvider key)
        {
            stream.Position = 0;
            var signature = new byte[SIGNED_HASH_SIZE];
            if (Duplicati.Library.Utility.Utility.ForceStreamRead(stream, signature, signature.Length) != signature.Length)
                throw new System.IO.InvalidDataException("Unexpected end-of-stream while reading signature");
            var sha256 = System.Security.Cryptography.SHA256.Create();
            sha256.Initialize();

            var bytes = stream.Length - (signature.Length);
            var buf = new byte[8 * 1024];
            while (bytes > 0)
            {
                var r = stream.Read(buf, 0, (int)Math.Min(bytes, buf.Length));
                if (r == 0)
                    throw new Exception("Unexpected end-of-stream while reading content");
                bytes -= r;
                sha256.TransformBlock(buf, 0, r, buf, 0);
            }

            sha256.TransformFinalBlock(buf, 0, 0);
            var hash = sha256.Hash;
            var OID = System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256");
            return key.VerifyHash(hash, OID, signature);
        }

        /// <summary>
        /// Creates a signed stream from the given data stream and writes the signature to the signed stream
        /// </summary>
        /// <param name="datastream">The stream to sign</param>
        /// <param name="signedstream">The stream with the signature</param>
        /// <param name="key">The key used to sign it</param>
        public static void CreateSignedStream(System.IO.Stream datastream, System.IO.Stream signedstream, System.Security.Cryptography.RSACryptoServiceProvider key)
        {
            var sha256 = System.Security.Cryptography.SHA256.Create();

            datastream.Position = 0;
            signedstream.Position = SIGNED_HASH_SIZE;

            var buf = new byte[8 * 1024];
            var bytes = datastream.Length;
            while (bytes > 0)
            {
                var r = datastream.Read(buf, 0, (int)Math.Min(bytes, buf.Length));
                if (r == 0)
                    throw new Exception("Unexpected end-of-stream while reading content");

                signedstream.Write(buf, 0, r);

                bytes -= r;
                sha256.TransformBlock(buf, 0, r, buf, 0);
            }

            sha256.TransformFinalBlock(buf, 0, 0);
            var hash = sha256.Hash;

            var OID = System.Security.Cryptography.CryptoConfig.MapNameToOID("SHA256");
            var signature = key.SignHash(hash, OID);

            signedstream.Position = 0;
            signedstream.Write(signature, 0, signature.Length);

            signedstream.Position = 0;
            if (!VerifySignature(signedstream, key))
                throw new System.IO.InvalidDataException("Unable to verify signature");
        }

        #region implemented abstract members of Stream

        public override void Flush()
        {
            try { m_stream.Flush(); }
            catch { }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return m_stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            switch (origin)
            {
                case System.IO.SeekOrigin.Current:
                    return Seek(offset + this.Position, System.IO.SeekOrigin.Begin);
                case System.IO.SeekOrigin.End:
                    return Seek(this.Length - offset, System.IO.SeekOrigin.Begin);
                case System.IO.SeekOrigin.Begin:
                default:
                    return this.Position = offset;
            }
        }

        public override void SetLength(long value)
        {
            throw new InvalidOperationException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new InvalidOperationException();
        }

        public override bool CanRead
        {
            get
            {
                return true;
            }
        }

        public override bool CanSeek
        {
            get
            {
                return true;
            }
        }

        public override bool CanWrite
        {
            get
            {
                return false;
            }
        }

        public override long Length
        {
            get
            {
                return m_stream.Length - SIGNED_HASH_SIZE;
            }
        }

        // Since the constructor sets the Position, we seal the implementation here to prevent subclasses
        // from potentially referencing uninitialized members.
        public sealed override long Position
        {
            get
            {
                return m_stream.Position - SIGNED_HASH_SIZE;
            }
            set
            {
                m_stream.Position = value + SIGNED_HASH_SIZE;
            }
        }

        #endregion
    }
}

