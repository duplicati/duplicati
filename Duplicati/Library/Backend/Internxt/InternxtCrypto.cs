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
using System.Security.Cryptography;
using System.Text;
using Org.BouncyCastle.Crypto.Digests;

namespace Duplicati.Library.Backend.Internxt;

/// <summary>
/// The cryptographic primitives used by the Internxt clients.
/// Each function mirrors the reference implementation in the official
/// JavaScript SDK and CLI (and Internxt's Go adapter for rclone), so files
/// written here can be read by the other clients and vice versa.
/// </summary>
public static class InternxtCrypto
{
    /// <summary>
    /// The application secret shipped with every Internxt client.
    /// It is not a secret in any useful sense: it exists only so the
    /// password hash and the salt travel in the CryptoJS "Salted__" format
    /// the server expects. It is a public constant in the CLI's .env file
    /// and in the Go adapter.
    /// </summary>
    public const string AppCryptoSecret = "6KYQBP847D4ATSFA";

    /// <summary>
    /// The encryption version tag stored on Drive file entries
    /// </summary>
    public const string EncryptVersion = "03-aes";

    /// <summary>
    /// The size in bytes of the random per-file index
    /// </summary>
    public const int IndexSize = 32;

    /// <summary>
    /// The size in bytes of the AES-CTR IV, taken from the first bytes of the index
    /// </summary>
    public const int IvSize = 16;

    /// <summary>
    /// The size in bytes of the per-file AES key
    /// </summary>
    public const int KeySize = 32;

    /// <summary>
    /// The OpenSSL "Salted__" marker that starts every encrypted text
    /// </summary>
    private static readonly byte[] SaltedPrefix = Encoding.ASCII.GetBytes("Salted__");

    /// <summary>
    /// Derives an AES-256 key and IV from a secret and salt the way OpenSSL's
    /// EVP_BytesToKey does with MD5 and three digest rounds (CryptoJS compatible)
    /// </summary>
    /// <param name="secret">The secret</param>
    /// <param name="salt">The 8-byte salt</param>
    /// <returns>The key and IV</returns>
    private static (byte[] Key, byte[] Iv) GetKeyAndIv(string secret, ReadOnlySpan<byte> salt)
    {
        var password = new byte[secret.Length + salt.Length];
        // The reference encodes the secret as 'binary' (latin1)
        Encoding.Latin1.GetBytes(secret, password);
        salt.CopyTo(password.AsSpan(secret.Length));

        var hashes = new byte[3][];
        var digest = password;
        for (var i = 0; i < hashes.Length; i++)
        {
            hashes[i] = MD5.HashData(digest);
            digest = [.. hashes[i], .. password];
        }

        return ([.. hashes[0], .. hashes[1]], hashes[2]);
    }

    /// <summary>
    /// Encrypts a text in the OpenSSL/CryptoJS "Salted__" AES-256-CBC format and returns it hex encoded
    /// </summary>
    /// <param name="text">The text to encrypt</param>
    /// <param name="secret">The secret to encrypt with</param>
    /// <returns>The hex encoded ciphertext</returns>
    public static string EncryptText(string text, string secret)
        => EncryptText(text, secret, RandomNumberGenerator.GetBytes(8));

    /// <summary>
    /// Encrypts a text in the OpenSSL/CryptoJS "Salted__" AES-256-CBC format with a given salt
    /// </summary>
    /// <param name="text">The text to encrypt</param>
    /// <param name="secret">The secret to encrypt with</param>
    /// <param name="salt">The 8-byte salt</param>
    /// <returns>The hex encoded ciphertext</returns>
    public static string EncryptText(string text, string secret, byte[] salt)
    {
        if (salt.Length != 8)
            throw new ArgumentException("Salt must be 8 bytes", nameof(salt));

        var (key, iv) = GetKeyAndIv(secret, salt);
        using var aes = Aes.Create();
        aes.Key = key;
        var cipher = aes.EncryptCbc(Encoding.UTF8.GetBytes(text), iv, PaddingMode.PKCS7);
        return Convert.ToHexString([.. SaltedPrefix, .. salt, .. cipher]).ToLowerInvariant();
    }

    /// <summary>
    /// Decrypts a hex encoded OpenSSL/CryptoJS "Salted__" AES-256-CBC text
    /// </summary>
    /// <param name="encryptedHex">The hex encoded ciphertext</param>
    /// <param name="secret">The secret to decrypt with</param>
    /// <returns>The decrypted text</returns>
    /// <exception cref="CryptographicException">Thrown when the data is not in the expected format or the secret is wrong</exception>
    public static string DecryptText(string encryptedHex, string secret)
    {
        var data = Convert.FromHexString(encryptedHex.Trim());
        if (data.Length < 16 || !data.AsSpan(0, 8).SequenceEqual(SaltedPrefix))
            throw new CryptographicException("Encrypted text is not in the expected OpenSSL format");

        var (key, iv) = GetKeyAndIv(secret, data.AsSpan(8, 8));
        using var aes = Aes.Create();
        aes.Key = key;
        var plain = aes.DecryptCbc(data.AsSpan(16), iv, PaddingMode.PKCS7);
        return Encoding.UTF8.GetString(plain);
    }

    /// <summary>
    /// Hashes a password with the salt the server supplied
    /// </summary>
    /// <param name="password">The plain-text password</param>
    /// <param name="saltHex">The hex encoded salt</param>
    /// <returns>The hex encoded hash</returns>
    public static string PasswordHash(string password, string saltHex)
    {
        var hash = Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(password), Convert.FromHexString(saltHex), 10000, HashAlgorithmName.SHA1, 32);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>
    /// Produces the password value for the login call: decrypt the server's
    /// salt, hash the password with it, and wrap the hash with the app secret
    /// </summary>
    /// <param name="password">The plain-text password</param>
    /// <param name="encryptedSalt">The sKey from the login call</param>
    /// <returns>The encrypted password hash</returns>
    public static string EncryptPasswordHash(string password, string encryptedSalt)
    {
        var salt = DecryptText(encryptedSalt, AppCryptoSecret);
        return EncryptText(PasswordHash(password, salt), AppCryptoSecret);
    }

    /// <summary>
    /// Checks that a mnemonic looks like a BIP39 phrase (12 to 24 lowercase ASCII words).
    /// The wordlist checksum is not verified; the server has already accepted
    /// the password and a wrong seed shows up as a hash mismatch on download.
    /// </summary>
    /// <param name="mnemonic">The mnemonic to check</param>
    /// <returns>True if the shape is right</returns>
    public static bool LooksLikeMnemonic(string mnemonic)
    {
        var words = mnemonic.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length < 12 || words.Length > 24 || words.Length % 3 != 0)
            return false;

        return words.All(w => w.All(c => c >= 'a' && c <= 'z'));
    }

    /// <summary>
    /// Computes the BIP39 seed of a mnemonic with an empty passphrase
    /// </summary>
    /// <param name="mnemonic">The mnemonic</param>
    /// <returns>The 64-byte seed</returns>
    public static byte[] Bip39Seed(string mnemonic)
        => Rfc2898DeriveBytes.Pbkdf2(
            Encoding.UTF8.GetBytes(mnemonic.Normalize(NormalizationForm.FormKD)),
            Encoding.UTF8.GetBytes("mnemonic"),
            2048,
            HashAlgorithmName.SHA512,
            64);

    /// <summary>
    /// Derives the per-file AES key and IV from the account seed, the bucket and the file index
    /// </summary>
    /// <param name="seed">The BIP39 seed</param>
    /// <param name="bucketIdHex">The hex encoded bucket id</param>
    /// <param name="index">The 32-byte file index</param>
    /// <returns>The key and IV</returns>
    public static (byte[] Key, byte[] Iv) DeriveFileKey(byte[] seed, string bucketIdHex, ReadOnlySpan<byte> index)
    {
        if (index.Length != IndexSize)
            throw new ArgumentException($"Index must be {IndexSize} bytes", nameof(index));

        var bucketKey = SHA512.HashData([.. seed, .. Convert.FromHexString(bucketIdHex)]);
        var fileKey = SHA512.HashData([.. bucketKey.AsSpan(0, 32), .. index]);
        return (fileKey[..KeySize], index[..IvSize].ToArray());
    }

    /// <summary>
    /// Computes the shard hash the network stores: RIPEMD-160 over the SHA-256 of the ciphertext
    /// </summary>
    /// <param name="sha256Digest">The SHA-256 digest of the ciphertext</param>
    /// <returns>The hex encoded shard hash</returns>
    public static string ShardHash(ReadOnlySpan<byte> sha256Digest)
    {
        var digest = new RipeMD160Digest();
        digest.BlockUpdate(sha256Digest);
        var result = new byte[digest.GetDigestSize()];
        digest.DoFinal(result, 0);
        return Convert.ToHexString(result).ToLowerInvariant();
    }

    /// <summary>
    /// Computes the Basic authentication header value for the network API
    /// </summary>
    /// <param name="bridgeUser">The bridge user name</param>
    /// <param name="userId">The user id</param>
    /// <returns>The header value including the "Basic " prefix</returns>
    public static string NetworkBasicAuth(string bridgeUser, string userId)
    {
        var password = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(userId))).ToLowerInvariant();
        return "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes($"{bridgeUser}:{password}"));
    }
}

/// <summary>
/// A read-only stream that applies AES-256-CTR to the bytes read from an inner stream.
/// CTR mode is symmetric, so the same stream encrypts and decrypts.
/// </summary>
public sealed class AesCtrStream : Stream
{
    /// <summary>
    /// The inner stream
    /// </summary>
    private readonly Stream _inner;
    /// <summary>
    /// Whether the inner stream is disposed with this one
    /// </summary>
    private readonly bool _leaveOpen;
    /// <summary>
    /// The AES instance in ECB mode, used to produce the keystream
    /// </summary>
    private readonly Aes _aes;
    /// <summary>
    /// The current counter block
    /// </summary>
    private readonly byte[] _counter;
    /// <summary>
    /// The keystream for the current block
    /// </summary>
    private readonly byte[] _keystream = new byte[16];
    /// <summary>
    /// The number of keystream bytes consumed in the current block
    /// </summary>
    private int _keystreamPos = 16;
    /// <summary>
    /// The number of bytes read so far
    /// </summary>
    private long _position;

    /// <summary>
    /// Creates a new CTR stream starting at the first block
    /// </summary>
    /// <param name="inner">The inner stream</param>
    /// <param name="key">The 32-byte key</param>
    /// <param name="iv">The 16-byte IV</param>
    /// <param name="leaveOpen">Whether to leave the inner stream open on dispose</param>
    public AesCtrStream(Stream inner, byte[] key, byte[] iv, bool leaveOpen)
        : this(inner, key, iv, 0, leaveOpen)
    {
    }

    /// <summary>
    /// Creates a new CTR stream starting at a byte offset into the plain text.
    /// Used for multipart uploads where each part continues the counter.
    /// </summary>
    /// <param name="inner">The inner stream</param>
    /// <param name="key">The 32-byte key</param>
    /// <param name="iv">The 16-byte IV</param>
    /// <param name="startOffset">The byte offset the inner stream starts at</param>
    /// <param name="leaveOpen">Whether to leave the inner stream open on dispose</param>
    public AesCtrStream(Stream inner, byte[] key, byte[] iv, long startOffset, bool leaveOpen)
    {
        if (iv.Length != 16)
            throw new ArgumentException("IV must be 16 bytes", nameof(iv));
        if (startOffset < 0)
            throw new ArgumentOutOfRangeException(nameof(startOffset));

        _inner = inner;
        _leaveOpen = leaveOpen;
        _aes = Aes.Create();
        _aes.Key = key;
        _counter = (byte[])iv.Clone();

        AddToCounter(startOffset / 16);
        var skip = (int)(startOffset % 16);
        if (skip > 0)
        {
            NextBlock();
            _keystreamPos = skip;
        }
    }

    /// <summary>
    /// Adds a block count to the big-endian counter
    /// </summary>
    /// <param name="blocks">The number of blocks to add</param>
    private void AddToCounter(long blocks)
    {
        var carry = (ulong)blocks;
        for (var i = 15; i >= 0 && carry != 0; i--)
        {
            var sum = _counter[i] + carry;
            _counter[i] = (byte)sum;
            carry = sum >> 8;
        }
    }

    /// <summary>
    /// Produces the keystream for the current counter and advances it
    /// </summary>
    private void NextBlock()
    {
        _aes.EncryptEcb(_counter, _keystream, PaddingMode.None);
        AddToCounter(1);
        _keystreamPos = 0;
    }

    /// <summary>
    /// Applies the keystream to a buffer in place
    /// </summary>
    /// <param name="buffer">The buffer</param>
    private void Transform(Span<byte> buffer)
    {
        for (var i = 0; i < buffer.Length; i++)
        {
            if (_keystreamPos == 16)
                NextBlock();
            buffer[i] ^= _keystream[_keystreamPos++];
        }
    }

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        Transform(buffer[..read]);
        _position += read;
        return read;
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        Transform(buffer.Span[..read]);
        _position += read;
        return read;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => _inner.Length;
    /// <inheritdoc/>
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    /// <inheritdoc/>
    public override void Flush() { }
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _aes.Dispose();
            if (!_leaveOpen)
                _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// A read-only stream that computes the SHA-256 of all bytes read from an inner stream
/// </summary>
public sealed class Sha256ReadStream : Stream
{
    /// <summary>
    /// The inner stream
    /// </summary>
    private readonly Stream _inner;
    /// <summary>
    /// Whether the inner stream is disposed with this one
    /// </summary>
    private readonly bool _leaveOpen;
    /// <summary>
    /// The running hash
    /// </summary>
    private readonly IncrementalHash _hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
    /// <summary>
    /// The number of bytes read so far
    /// </summary>
    private long _position;

    /// <summary>
    /// Creates a new hashing stream
    /// </summary>
    /// <param name="inner">The inner stream</param>
    /// <param name="leaveOpen">Whether to leave the inner stream open on dispose</param>
    public Sha256ReadStream(Stream inner, bool leaveOpen)
    {
        _inner = inner;
        _leaveOpen = leaveOpen;
    }

    /// <summary>
    /// Finalizes the hash and returns the digest. Can be called once.
    /// </summary>
    /// <returns>The SHA-256 digest</returns>
    public byte[] GetHash() => _hash.GetHashAndReset();

    /// <inheritdoc/>
    public override int Read(byte[] buffer, int offset, int count)
        => Read(buffer.AsSpan(offset, count));

    /// <inheritdoc/>
    public override int Read(Span<byte> buffer)
    {
        var read = _inner.Read(buffer);
        _hash.AppendData(buffer[..read]);
        _position += read;
        return read;
    }

    /// <inheritdoc/>
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        var read = await _inner.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        _hash.AppendData(buffer.Span[..read]);
        _position += read;
        return read;
    }

    /// <inheritdoc/>
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => ReadAsync(buffer.AsMemory(offset, count), cancellationToken).AsTask();

    /// <inheritdoc/>
    public override bool CanRead => true;
    /// <inheritdoc/>
    public override bool CanSeek => false;
    /// <inheritdoc/>
    public override bool CanWrite => false;
    /// <inheritdoc/>
    public override long Length => _inner.Length;
    /// <inheritdoc/>
    public override long Position { get => _position; set => throw new NotSupportedException(); }
    /// <inheritdoc/>
    public override void Flush() { }
    /// <inheritdoc/>
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void SetLength(long value) => throw new NotSupportedException();
    /// <inheritdoc/>
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc/>
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _hash.Dispose();
            if (!_leaveOpen)
                _inner.Dispose();
        }
        base.Dispose(disposing);
    }
}

/// <summary>
/// Time-based one-time passwords (RFC 6238) for accounts with two-factor
/// authentication, so a scheduled backup can log in unattended from the
/// enrolled secret. Internxt uses the common authenticator profile:
/// HMAC-SHA1, 30 second steps, 6 digits.
/// </summary>
public static class InternxtTotp
{
    /// <summary>
    /// The step length in seconds
    /// </summary>
    public const int StepSeconds = 30;
    /// <summary>
    /// The number of digits in a code
    /// </summary>
    public const int Digits = 6;
    /// <summary>
    /// The base32 alphabet (RFC 4648)
    /// </summary>
    private const string Base32Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ234567";

    /// <summary>
    /// Decodes a base32 secret as authenticator apps present it: case
    /// insensitive, with spaces, dashes and padding ignored
    /// </summary>
    /// <param name="secret">The secret</param>
    /// <returns>The raw key</returns>
    /// <exception cref="FormatException">Thrown when the secret contains characters outside the alphabet</exception>
    public static byte[] DecodeBase32(string secret)
    {
        var cleaned = new string(secret.Where(c => c != ' ' && c != '-' && c != '=').ToArray()).ToUpperInvariant();
        if (cleaned.Length == 0)
            throw new FormatException("The two-factor secret is empty");

        var output = new List<byte>(cleaned.Length * 5 / 8);
        var buffer = 0;
        var bits = 0;
        foreach (var c in cleaned)
        {
            var value = Base32Alphabet.IndexOf(c);
            if (value < 0)
                throw new FormatException($"The two-factor secret contains an invalid character '{c}'");
            buffer = (buffer << 5) | value;
            bits += 5;
            if (bits >= 8)
            {
                bits -= 8;
                output.Add((byte)((buffer >> bits) & 0xff));
            }
        }

        return output.ToArray();
    }

    /// <summary>
    /// Computes the code for a point in time
    /// </summary>
    /// <param name="secret">The base32 secret</param>
    /// <param name="time">The time to compute the code for</param>
    /// <returns>The code, zero padded</returns>
    public static string Compute(string secret, DateTimeOffset time)
    {
        var key = DecodeBase32(secret);
        var counter = time.ToUnixTimeSeconds() / StepSeconds;
        var message = new byte[8];
        System.Buffers.Binary.BinaryPrimitives.WriteInt64BigEndian(message, counter);

        var hash = HMACSHA1.HashData(key, message);
        var offset = hash[^1] & 0x0f;
        var binary = ((hash[offset] & 0x7f) << 24) | (hash[offset + 1] << 16) | (hash[offset + 2] << 8) | hash[offset + 3];
        return (binary % 1_000_000).ToString("D6");
    }

    /// <summary>
    /// Computes the code for now
    /// </summary>
    /// <param name="secret">The base32 secret</param>
    /// <returns>The current code</returns>
    public static string ComputeNow(string secret)
        => Compute(secret, DateTimeOffset.UtcNow);
}
