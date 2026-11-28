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

#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Duplicati.Library.Main.Operation.Restore
{
    /// <summary>
    /// A table keyed by block hash that keeps a 32-byte hash as its bytes instead of as a string.
    /// </summary>
    /// <remarks>
    /// A restore without a local database keeps every block it needs in memory (#7538). As a string,
    /// the base64 of a SHA-256 hash takes about 110 bytes and a reference, so the table of 600,000
    /// blocks was mostly strings. A hash of another size, or a string that is not the exact base64
    /// of its bytes, is kept as a string, so a lookup always finds what the same string added.
    /// </remarks>
    /// <typeparam name="TValue">The type of the values.</typeparam>
    internal sealed class BlockHashTable<TValue>
    {
        /// <summary>
        /// The values of the hashes that are kept as bytes.
        /// </summary>
        private readonly Dictionary<HashBytes, TValue> m_bytes = new();
        /// <summary>
        /// The values of the hashes that are kept as strings.
        /// </summary>
        private readonly Dictionary<string, TValue> m_strings = new(StringComparer.Ordinal);

        /// <summary>
        /// The number of hashes in the table.
        /// </summary>
        public int Count => m_bytes.Count + m_strings.Count;

        /// <summary>
        /// The number of hashes kept as strings, for tests.
        /// </summary>
        internal int StringCount => m_strings.Count;

        /// <summary>
        /// Gets the value of a hash.
        /// </summary>
        /// <param name="hash">The hash.</param>
        /// <param name="value">The value, if the hash is in the table.</param>
        /// <returns>True if the hash is in the table.</returns>
        public bool TryGetValue(string hash, out TValue value)
            => HashBytes.TryCreate(hash, out var key)
                ? m_bytes.TryGetValue(key, out value!)
                : m_strings.TryGetValue(hash, out value!);

        /// <summary>
        /// Adds a hash that is not in the table.
        /// </summary>
        /// <param name="hash">The hash.</param>
        /// <param name="value">The value.</param>
        public void Add(string hash, TValue value)
        {
            if (HashBytes.TryCreate(hash, out var key))
                m_bytes.Add(key, value);
            else
                m_strings.Add(hash, value);
        }

        /// <summary>
        /// Gets or sets the value of a hash.
        /// </summary>
        /// <param name="hash">The hash.</param>
        public TValue this[string hash]
        {
            get => TryGetValue(hash, out var value) ? value : throw new KeyNotFoundException(hash);
            set
            {
                if (HashBytes.TryCreate(hash, out var key))
                    m_bytes[key] = value;
                else
                    m_strings[hash] = value;
            }
        }

        /// <summary>
        /// Lists the hashes and their values, with each hash as the string it was added with.
        /// </summary>
        /// <returns>The hashes and their values.</returns>
        public IEnumerable<(string Hash, TValue Value)> Entries()
        {
            foreach (var (key, value) in m_bytes)
                yield return (key.ToBase64(), value);
            foreach (var (hash, value) in m_strings)
                yield return (hash, value);
        }

        /// <summary>
        /// The bytes of a 32-byte hash.
        /// </summary>
        private readonly record struct HashBytes(ulong A, ulong B, ulong C, ulong D)
        {
            /// <summary>
            /// The number of bytes in the hash.
            /// </summary>
            private const int Size = 32;
            /// <summary>
            /// The number of characters in the base64 of the hash.
            /// </summary>
            private const int Base64Length = (Size + 2) / 3 * 4;

            /// <summary>
            /// Reads the bytes of a hash, if the string is the base64 of 32 bytes exactly as
            /// <see cref="Convert.ToBase64String(byte[])"/> writes it.
            /// </summary>
            /// <param name="hash">The hash as base64.</param>
            /// <param name="key">The bytes of the hash.</param>
            /// <returns>True if the hash can be kept as bytes.</returns>
            public static bool TryCreate(string hash, out HashBytes key)
            {
                key = default;
                if (hash.Length != Base64Length)
                    return false;

                Span<byte> bytes = stackalloc byte[Size];
                if (!Convert.TryFromBase64String(hash, bytes, out var written) || written != Size)
                    return false;

                // Base64 that is not written this way, such as one with other padding bits, would
                // be written back differently, so it is kept as a string
                Span<char> chars = stackalloc char[Base64Length];
                if (!Convert.TryToBase64Chars(bytes, chars, out var charsWritten) || !chars[..charsWritten].SequenceEqual(hash))
                    return false;

                var words = MemoryMarshal.Cast<byte, ulong>(bytes);
                key = new HashBytes(words[0], words[1], words[2], words[3]);
                return true;
            }

            /// <summary>
            /// Writes the hash as base64.
            /// </summary>
            /// <returns>The base64 of the hash.</returns>
            public string ToBase64()
            {
                Span<ulong> words = [A, B, C, D];
                return Convert.ToBase64String(MemoryMarshal.AsBytes(words));
            }
        }
    }
}
