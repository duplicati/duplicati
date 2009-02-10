using System;
using System.Collections.Generic;
using System.Text;
using Duplicati.Library.Encryption;

namespace Duplicati.CommandLine.Decrypter
{
    class Program
    {
        static void Main(string[] args)
        {
            if (args.Length == 3)
            {
                Console.WriteLine("Usage: Duplicati.CommandLine.Decrypter.exe <passphrase> <inputfile> <outputfile>");
                return;
            }

            AESEncryption c = new AESEncryption(args[0]);
            c.Decrypt(args[1], args[2]);
        }
    }
}
