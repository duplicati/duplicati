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
using System.Collections.Generic;
using System.Security.Cryptography;
using Duplicati.Library.Interface;
using Duplicati.Library.Utility;

namespace Duplicati.Library.Backend
{
    public class KeyGenerator : IWebModule
    {
        private const string GENERATE_COMMAND = "generate-keys";
        private const string KEY_TYPE_NAME = "key-type";
        private const string KEY_USERNAME = "key-username";
        private const string KEY_KEYLEN = "key-bits";
        
        private const string KEY_TEMPLATE_DSA = "-----BEGIN DSA PRIVATE KEY-----\n{0}\n-----END DSA PRIVATE KEY----- \n";
        private const string KEY_TEMPLATE_RSA = "-----BEGIN RSA PRIVATE KEY-----\n{0}\n-----END RSA PRIVATE KEY----- \n";
        private const string PUB_KEY_FORMAT_DSA = "ssh-dss";
        private const string PUB_KEY_FORMAT_RSA = "ssh-rsa";
        
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
                    EncodeDERLength(ms, (uint)b.Length);
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
                    EncodePEMLength(ms, (uint)n.Length);
                    ms.Write(n, 0, n.Length);
                }
                return ms.ToArray();
            }
        }
        
        #region IWebModule implementation
        public IDictionary<string, string> Execute(string command, IDictionary<string, string> options)
        {
            if (!GENERATE_COMMAND.Equals(command, StringComparison.InvariantCultureIgnoreCase))
                throw new Exception(string.Format("Invalid command: {0}", command));
            
            string keytype;
            if (!options.TryGetValue(KEY_TYPE_NAME, out keytype))
                keytype = "dsa";

            string username;
            if (!options.TryGetValue(KEY_USERNAME, out username))
                username = "someone@example.com";

            string keylen_s;
            if (!options.TryGetValue(KEY_KEYLEN, out keylen_s))
                keylen_s = "0";

            int keylen;
            if (!int.TryParse(keylen_s, out keylen))
                keylen = 0;

            if ("rsa".Equals(keytype, StringComparison.InvariantCultureIgnoreCase))
            {
                var rsa = RSACryptoServiceProvider.Create();
                if (keylen > 0)
                    rsa.KeySize = keylen;
                else
                    rsa.KeySize = 1024;
                
                var key = rsa.ExportParameters(true);
                
                var privateEntries = new byte[][] { new byte[]{ 0x0 }, key.Modulus, key.Exponent, key.D, key.P, key.Q, key.DP, key.DQ, key.InverseQ };
                var publicEntries = new byte[][] {                     
                    System.Text.Encoding.ASCII.GetBytes(PUB_KEY_FORMAT_RSA),
                    key.Exponent, 
                    key.Modulus 
                };
                
                byte[] privkey = EncodeDER(privateEntries);
                byte[] pubkey = EncodePEM(publicEntries);

                var res = new Dictionary<string, string>();
                res["privkey"] = Convert.ToBase64String(privkey);
                res["privkeyfile"] = string.Format(KEY_TEMPLATE_RSA, Convert.ToBase64String(privkey));
                res["privkeyuri"] = SSHv2.KEYFILE_URI + Duplicati.Library.Utility.Uri.UrlEncode(string.Format(KEY_TEMPLATE_RSA, Convert.ToBase64String(privkey)));
                res["pubkey"] = PUB_KEY_FORMAT_RSA + " " + Convert.ToBase64String(pubkey) + " " + username;
                return res;
            }
            else if ("dsa".Equals(keytype, StringComparison.InvariantCultureIgnoreCase))
            {
            
                var dsa = DSACryptoServiceProvider.Create();
                if (keylen > 0)
                    dsa.KeySize = keylen;
                else
                    dsa.KeySize = 1024;
                var key = dsa.ExportParameters(true);

                var privateEntries = new byte[][] { new byte[] { 0x0 }, key.P, key.Q, key.G, key.Y, key.X };
                var publicEntries = new byte[][] {
                    System.Text.Encoding.ASCII.GetBytes(PUB_KEY_FORMAT_DSA),
                    key.P,
                    key.Q,
                    key.G,
                    key.Y
                };
                
                byte[] privkey = EncodeDER(privateEntries);
                byte[] pubkey = EncodePEM(publicEntries);

                var res = new Dictionary<string, string>();
                res["privkey"] = Convert.ToBase64String(privkey);
                res["privkeyfile"] = string.Format(KEY_TEMPLATE_DSA, Convert.ToBase64String(privkey));
                res["privkeyuri"] = SSHv2.KEYFILE_URI + Duplicati.Library.Utility.Uri.UrlEncode(string.Format(KEY_TEMPLATE_DSA, Convert.ToBase64String(privkey)));
                res["pubkey"] = PUB_KEY_FORMAT_DSA + " " + Convert.ToBase64String(pubkey) + " " + username;
                return res;
            }
            else
            {
                throw new Exception(string.Format("Unsupported key type: {0}", keytype));
            }
        }
        public string Key { get { return "ssh-keygen"; } }
        
        public string DisplayName { get { return "SSH Keygenerator"; } }
        public string Description { get { return "Module for generating SSH private/public keys"; } }
        public IList<ICommandLineArgument> SupportedCommands
        {
            get
            {
                return new List<ICommandLineArgument>(new ICommandLineArgument[] {
                    new CommandLineArgument(GENERATE_COMMAND)
                });
            }
        }
        #endregion
    }
}

