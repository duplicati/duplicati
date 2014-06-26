//  Copyright (C) 2014, Kenneth Skovhede

//  http://www.hexad.dk, opensource@hexad.dk
//
//  This library is free software; you can redistribute it and/or modify
//  it under the terms of the GNU Lesser General Public License as
//  published by the Free Software Foundation; either version 2.1 of the
//  License, or (at your option) any later version.
//
//  This library is distributed in the hope that it will be useful, but
//  WITHOUT ANY WARRANTY; without even the implied warranty of
//  MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. See the GNU
//  Lesser General Public License for more details.
//
//  You should have received a copy of the GNU Lesser General Public
//  License along with this library; if not, write to the Free Software
//  Foundation, Inc., 59 Temple Place, Suite 330, Boston, MA 02111-1307 USA
using System;

namespace Duplicati.Library.AutoUpdater
{
    public class SignatureReadingStream : System.IO.Stream, IDisposable
    {
        /// <summary>
        /// The size of the HMAC key in bytes
        /// </summary>
        private const int HMAC_KEY_SIZE = 64;

        /// <summary>
        /// The size of the SHA256 output hash in bytes
        /// </summary>
        /// 
        private const int SHA256_HASH_SIZE = 32;        

        /// <summary>
        /// The stream to read from
        /// </summary>
        private System.IO.Stream m_stream;

        public SignatureReadingStream(System.IO.Stream stream, System.Security.Cryptography.DSA key)
        {
            if (!VerifySignature(stream, key))
                throw new System.IO.InvalidDataException("Unable to verify signature");
            m_stream = stream;
        }

        private static bool VerifySignature(System.IO.Stream stream, System.Security.Cryptography.DSA key)
        {
            stream.Position = stream.Length - (SHA256_HASH_SIZE + HMAC_KEY_SIZE);
            var signature = new byte[SHA256_HASH_SIZE];
            var hmackey = new byte[HMAC_KEY_SIZE];
            if (stream.Read(signature, 0, signature.Length) != signature.Length)
                throw new System.IO.InvalidDataException("Unexpected end-of-stream while reading signature");
            if (stream.Read(hmackey, 0, hmackey.Length) != hmackey.Length)
                throw new System.IO.InvalidDataException("Unexpected end-of-stream while reading hmac key");
            var hmac = new System.Security.Cryptography.HMACSHA256(hmackey);
            hmac.Initialize();

            var bytes = stream.Length - (signature.Length + hmackey.Length);
            stream.Position = 0;
            var buf = new byte[8 * 1024];
            while (bytes > 0)
            {
                var r = stream.Read(buf, 0, (int)Math.Min(bytes, buf.Length));
                if (r == 0)
                    throw new Exception("Unexpected end-of-stream while reading content");
                bytes -= r;
                hmac.TransformBlock(buf, 0, r, buf, 0);
            }

            var hash = hmac.TransformFinalBlock(buf, 0, 0);
            return key.VerifySignature(hash, signature);
        }

        #region implemented abstract members of Stream

        public override void Flush()
        {
            m_stream.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            count = (int)Math.Min(count, this.Length - m_stream.Position);
            return m_stream.Read(buffer, offset, count);
        }

        public override long Seek(long offset, System.IO.SeekOrigin origin)
        {
            switch (origin)
            {
                case System.IO.SeekOrigin.Current:
                    return Seek(m_stream.Position + offset, System.IO.SeekOrigin.Begin);
                case System.IO.SeekOrigin.End:
                    return m_stream.Seek(offset + (SHA256_HASH_SIZE + HMAC_KEY_SIZE), origin);
                case System.IO.SeekOrigin.Begin:
                default:
                    return m_stream.Seek(Math.Min(offset, m_stream.Length - (SHA256_HASH_SIZE + HMAC_KEY_SIZE)), origin);
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
                return m_stream.Length - (SHA256_HASH_SIZE + HMAC_KEY_SIZE);
            }
        }

        public override long Position
        {
            get
            {
                return m_stream.Position;
            }
            set
            {
                m_stream.Seek(value, System.IO.SeekOrigin.Begin);
            }
        }

        #endregion
    }
}

