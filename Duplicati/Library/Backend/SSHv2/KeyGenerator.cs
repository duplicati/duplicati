//  Copyright (C) 2015, The Duplicati Team

//  http://www.duplicati.com, info@duplicati.com
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
using System.Collections.Generic;
using System.Security.Cryptography;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;
using System.Text;
using System.Linq;

namespace Duplicati.Library.Backend
{
    public class KeyGenerator : IWebModule
    {
        private const string KEY_TYPE_NAME = "key-type";
        private const string KEY_USERNAME = "key-username";
        private const string KEY_KEYLEN = "key-bits";
        
        private const string KEY_TEMPLATE_DSA = "-----BEGIN DSA PRIVATE KEY-----\n{0}\n-----END DSA PRIVATE KEY----- \n";
        private const string KEY_TEMPLATE_RSA = "-----BEGIN RSA PRIVATE KEY-----\n{0}\n-----END RSA PRIVATE KEY----- \n";
        private const string PUB_KEY_FORMAT_DSA = "ssh-dss";
        private const string PUB_KEY_FORMAT_RSA = "ssh-rsa";
        
        private const string KEYTYPE_DSA = "dsa";
        private const string KEYTYPE_RSA = "rsa";
        
        private readonly string DEFAULT_USERNAME = "backup-user@" + System.Environment.MachineName;
        private const string DEFAULT_KEYTYPE = KEYTYPE_DSA;        
        private const int DEFAULT_KEYLEN = 1024;
        
        private static void EncodePEMLength(System.IO.Stream s, uint length)
        {
            s.WriteByte((byte)((length >> 24) & 0xff));
            s.WriteByte((byte)((length >> 16) & 0xff));
            s.WriteByte((byte)((length >> 8) & 0xff));
            s.WriteByte((byte)(length & 0xff));
        }
        
        private static void EncodeDERLength(System.IO.Stream s, uint length)
        {
            if (length < 0x7f)
            {
                s.WriteByte((byte)length);
            }
            else if (length <= 0x7fff)
            {
                s.WriteByte(0x82);
                s.WriteByte((byte)((length >> 8) & 0xff));
                s.WriteByte((byte)(length & 0xff));
            }
            else
            {
                s.WriteByte(0x84);
                s.WriteByte((byte)((length >> 24) & 0xff));
                s.WriteByte((byte)((length >> 16) & 0xff));
                s.WriteByte((byte)((length >> 8) & 0xff));
                s.WriteByte((byte)(length & 0xff));
            }
        }
        
        private static byte[] EncodeDER(byte[][] data)
        {
            byte[] payload;
            using(var ms = new System.IO.MemoryStream())
            {
                foreach(var b in data)
                {
                    ms.WriteByte(0x02);
                    var isNegative = (b[0] & 0x80) != 0;
                    
                    EncodeDERLength(ms, (uint)(b.Length + (isNegative ? 1 : 0)));
                    if (isNegative)
                        ms.WriteByte(0);
                    ms.Write(b, 0, b.Length);                    
                }
            
                payload = ms.ToArray();
            }
        
            using(var ms = new System.IO.MemoryStream())
            {
                ms.WriteByte(0x30);
                EncodeDERLength(ms, (uint)payload.Length);
                ms.Write(payload, 0, payload.Length);
                return ms.ToArray();
            }
        }
        
        private static byte[] EncodePEM(byte[][] data)
        {
            using(var ms = new System.IO.MemoryStream())
            {
                foreach(var n in data)
                {
                    var isNegative = (n[0] & 0x80) != 0;
                    EncodePEMLength(ms, (uint)(n.Length + (isNegative ? 1 : 0)));
                    if (isNegative)
                        ms.WriteByte(0);
                    ms.Write(n, 0, n.Length);
                }
                return ms.ToArray();
            }
        }
        
        private static IDictionary<string, string> OutputKey(byte[] private_key, byte[] public_key, string pem_template, string key_name, string username)
        {
            var res = new Dictionary<string, string>();
            
            var b64_raw = Convert.ToBase64String(private_key);
            var sb = new StringBuilder();
            var lw = 64;
            for(var i = 0; i < b64_raw.Length; i += lw)
            {
                sb.Append(b64_raw.Substring(i, Math.Min(lw, b64_raw.Length - i)));
                sb.Append("\n");
            }
            
            var b64 = sb.ToString().Trim();
            var pem = string.Format(pem_template, b64);
            var uri = SSHv2.KEYFILE_URI + Duplicati.Library.Utility.Uri.UrlEncode(pem);
            var pub = key_name + " " + Convert.ToBase64String(public_key) + " " + username;

            res["privkey"] = b64_raw;
            res["privkeyfile"] = pem;
            res["privkeyuri"] = uri;
            res["pubkey"] = pub;
            return res;
        }
        
        #region IWebModule implementation
        public IDictionary<string, string> Execute(IDictionary<string, string> options)
        {            
            string keytype;
            if (!options.TryGetValue(KEY_TYPE_NAME, out keytype))
                keytype = DEFAULT_KEYTYPE;

            string username;
            if (!options.TryGetValue(KEY_USERNAME, out username))
                username = DEFAULT_USERNAME;

            string keylen_s;
            if (!options.TryGetValue(KEY_KEYLEN, out keylen_s))
                keylen_s = "0";

            int keylen;
            if (!int.TryParse(keylen_s, out keylen))
                keylen = DEFAULT_KEYLEN;

            if (KEYTYPE_RSA.Equals(keytype, StringComparison.InvariantCultureIgnoreCase))
            {
                var rsa = RSACryptoServiceProvider.Create();
                if (keylen > 0)
                    rsa.KeySize = keylen;
                else
                    rsa.KeySize = DEFAULT_KEYLEN;
                
                var key = rsa.ExportParameters(true);
                
                var privateEntries = new byte[][] { new byte[]{ 0x0 }, key.Modulus, key.Exponent, key.D, key.P, key.Q, key.DP, key.DQ, key.InverseQ };
                var publicEntries = new byte[][] {                     
                    System.Text.Encoding.ASCII.GetBytes(PUB_KEY_FORMAT_RSA),
                    key.Exponent, 
                    key.Modulus 
                };
                
                return OutputKey(EncodeDER(privateEntries), EncodePEM(publicEntries), KEY_TEMPLATE_RSA, PUB_KEY_FORMAT_RSA, username);
            }
            else if (KEYTYPE_DSA.Equals(keytype, StringComparison.InvariantCultureIgnoreCase))
            {
            
                var dsa = DSACryptoServiceProvider.Create();
                if (keylen > 0)
                    dsa.KeySize = keylen;
                else
                    dsa.KeySize = DEFAULT_KEYLEN;
                
                var key = dsa.ExportParameters(true);
                
                var privateEntries = new byte[][] { new byte[] { 0x0 }, key.P, key.Q, key.G, key.Y, key.X };
                var publicEntries = new byte[][] {
                    System.Text.Encoding.ASCII.GetBytes(PUB_KEY_FORMAT_DSA),
                    key.P,
                    key.Q,
                    key.G,
                    key.Y
                };
                
                return OutputKey(EncodeDER(privateEntries), EncodePEM(publicEntries), KEY_TEMPLATE_DSA, PUB_KEY_FORMAT_DSA, username);
            }
            else
            {
                throw new UserInformationException(string.Format("Unsupported key type: {0}", keytype));
            }
        }
        public string Key { get { return "ssh-keygen"; } }
        
        public string DisplayName { get { return Strings.KeyGenerator.DisplayName; } }
        public string Description { get { return Strings.KeyGenerator.Description; } }
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(KEY_KEYLEN, CommandLineArgument.ArgumentType.Integer, Strings.KeyGenerator.KeyLenShort, Strings.KeyGenerator.KeyLenLong, DEFAULT_KEYLEN.ToString(), null, new string[] {"1024", "2048"}),
                    new CommandLineArgument(KEY_TYPE_NAME, CommandLineArgument.ArgumentType.Enumeration, Strings.KeyGenerator.KeyTypeShort, Strings.KeyGenerator.KeyTypeLong, DEFAULT_KEYTYPE, null, new string[] {KEYTYPE_DSA, KEYTYPE_RSA}),
                    new CommandLineArgument(KEY_USERNAME, CommandLineArgument.ArgumentType.Integer, Strings.KeyGenerator.KeyUsernameShort, Strings.KeyGenerator.KeyUsernameLong, DEFAULT_USERNAME),
                });
            }
        }
        #endregion
    }
}

