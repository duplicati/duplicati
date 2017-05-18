//
// mozroots.cs: Import the Mozilla's trusted root certificates into Mono
//
// Authors:
//	Sebastien Pouliot <sebastien@ximian.com>
//
// Copyright (C) 2005 Novell, Inc (http://www.novell.com)
//
// Permission is hereby granted, free of charge, to any person obtaining
// a copy of this software and associated documentation files (the
// "Software"), to deal in the Software without restriction, including
// without limitation the rights to use, copy, modify, merge, publish,
// distribute, sublicense, and/or sell copies of the Software, and to
// permit persons to whom the Software is furnished to do so, subject to
// the following conditions:
// 
// The above copyright notice and this permission notice shall be
// included in all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND,
// EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF
// MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND
// NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE
// LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION
// OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION
// WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
//

using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

using Mono.Security.Authenticode;
using Mono.Security.X509;

[assembly: AssemblyTitle("Mozilla Roots Importer")]
[assembly: AssemblyDescription("Download and import trusted root certificates from Mozilla's MXR.")]

namespace Mono.Tools
{

	class MozRoots
	{

		// this URL is recommended by https://bugzilla.mozilla.org/show_bug.cgi?id=1279952#c8 and is also used as basis for curl's https://curl.haxx.se/ca/cacert.pem bundle
		private const string defaultUrl = "https://hg.mozilla.org/releases/mozilla-release/raw-file/default/security/nss/lib/ckfw/builtins/certdata.txt";

		static string url;
		static string inputFile;
		static string pkcs7filename;
		static bool import;
		static bool machine;
		static bool confirmAddition;
		static bool confirmRemoval;
		static bool quiet;

		static byte[] DecodeOctalString(string s)
		{
			string[] pieces = s.Split('\\');
			byte[] data = new byte[pieces.Length - 1];
			for (int i = 1; i < pieces.Length; i++)
			{
				data[i - 1] = (byte)((pieces[i][0] - '0' << 6) + (pieces[i][1] - '0' << 3) + (pieces[i][2] - '0'));
			}
			return data;
		}

		static X509Certificate DecodeCertificate(string s)
		{
			byte[] rawdata = DecodeOctalString(s);
			return new X509Certificate(rawdata);
		}

		static Stream GetFile()
		{
			try
			{
				if (inputFile != null)
				{
					return File.OpenRead(inputFile);
				}
				else {
					WriteLine("Downloading from '{0}'...", url);
					HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
					req.Timeout = 10000;
					return req.GetResponse().GetResponseStream();
				}
			}
			catch
			{
				return null;
			}
		}

		static X509CertificateCollection DecodeCollection()
		{
			X509CertificateCollection roots = new X509CertificateCollection();
			StringBuilder sb = new StringBuilder();
			bool processing = false;

			using (Stream s = GetFile())
			{
				if (s == null)
				{
					WriteLine("Couldn't retrieve the file using the supplied information.");
					return null;
				}

				StreamReader sr = new StreamReader(s);
				while (true)
				{
					string line = sr.ReadLine();
					if (line == null)
						break;

					if (processing)
					{
						if (line.StartsWith("END"))
						{
							processing = false;
							X509Certificate root = DecodeCertificate(sb.ToString());
							roots.Add(root);

							sb = new StringBuilder();
							continue;
						}
						sb.Append(line);
					}
					else {
						processing = line.StartsWith("CKA_VALUE MULTILINE_OCTAL");
					}
				}
				return roots;
			}
		}

		static int Process()
		{
			ServicePointManager.ServerCertificateValidationCallback = (sender, certificate, chain, sslPolicyErrors) =>
			{
				if (sslPolicyErrors != System.Net.Security.SslPolicyErrors.None)
					Console.WriteLine("WARNING: Downloading the trusted certificate list couldn't be done securely (error: {0}), continuing anyway. If you're using mozroots to bootstrap Mono's trust store on a clean system this might be OK, otherwise it could indicate a network intrusion. Please ensure you're using a trusted network or move to cert-sync.", sslPolicyErrors);

				// this is very bad, but on a clean system without an existing trust store we don't really have a better option
				return true;
			};

			X509CertificateCollection roots = DecodeCollection();
			if (roots == null)
			{
				return 1;
			}
			else if (roots.Count == 0)
			{
				WriteLine("No certificates were found.");
				return 0;
			}

			if (pkcs7filename != null)
			{
				SoftwarePublisherCertificate pkcs7 = new SoftwarePublisherCertificate();
				pkcs7.Certificates.AddRange(roots);

				WriteLine("Saving root certificates into '{0}' file...", pkcs7filename);
				using (FileStream fs = File.OpenWrite(pkcs7filename))
				{
					byte[] data = pkcs7.GetBytes();
					fs.Write(data, 0, data.Length);
					fs.Close();
				}
			}

			if (import)
			{
				WriteLine("Importing certificates into {0} store...",
					machine ? "machine" : "user");

				X509Stores stores = (machine ? X509StoreManager.LocalMachine : X509StoreManager.CurrentUser);
				X509CertificateCollection trusted = stores.TrustedRoot.Certificates;
				int additions = 0;
				foreach (X509Certificate root in roots)
				{
					if (!trusted.Contains(root))
					{
						if (!confirmAddition || AskConfirmation("add", root))
						{
							stores.TrustedRoot.Import(root);
							if (confirmAddition)
								WriteLine("Certificate added.{0}", Environment.NewLine);
							additions++;
						}
					}
				}
				if (additions > 0)
					WriteLine("{0} new root certificates were added to your trust store.", additions);

				X509CertificateCollection removed = new X509CertificateCollection();
				foreach (X509Certificate trust in trusted)
				{
					if (!roots.Contains(trust))
					{
						removed.Add(trust);
					}
				}
				if (removed.Count > 0)
				{
					if (confirmRemoval)
					{
						WriteLine("{0} previously trusted certificates were not part of the update.", removed.Count);
					}
					else {
						WriteLine("{0} previously trusted certificates were removed.", removed.Count);
					}

					foreach (X509Certificate old in removed)
					{
						if (!confirmRemoval || AskConfirmation("remove", old))
						{
							stores.TrustedRoot.Remove(old);
							if (confirmRemoval)
								WriteLine("Certificate removed.{0}", Environment.NewLine);
						}
					}
				}
				WriteLine("Import process completed.{0}", Environment.NewLine);
			}
			return 0;
		}

		static string Thumbprint(string algorithm, X509Certificate certificate)
		{
			HashAlgorithm hash = HashAlgorithm.Create(algorithm);
			byte[] digest = hash.ComputeHash(certificate.RawData);
			return BitConverter.ToString(digest);
		}

		static bool AskConfirmation(string action, X509Certificate certificate)
		{
			// the quiet flag is ignored for confirmations
			Console.WriteLine();
			Console.WriteLine("Issuer: {0}", certificate.IssuerName);
			Console.WriteLine("Serial number: {0}", BitConverter.ToString(certificate.SerialNumber));
			Console.WriteLine("Valid from {0} to {1}", certificate.ValidFrom, certificate.ValidUntil);
			Console.WriteLine("Thumbprint SHA-1: {0}", Thumbprint("SHA1", certificate));
			Console.WriteLine("Thumbprint MD5:   {0}", Thumbprint("MD5", certificate));
			while (true)
			{
				Console.Write("Are you sure you want to {0} this certificate ? ", action);
				string s = Console.ReadLine().ToLower();
				if (s == "yes")
					return true;
				else if (s == "no")
					return false;
			}
		}

		static bool ParseOptions(string[] args)
		{
			if (args.Length < 1)
				return false;

			// set defaults
			url = defaultUrl;
			confirmAddition = true;
			confirmRemoval = true;

			for (int i = 0; i < args.Length; i++)
			{
				switch (args[i])
				{
				case "--url":
					if (i >= args.Length - 1)
						return false;
					url = args[++i];
					break;
				case "--file":
					if (i >= args.Length - 1)
						return false;
					inputFile = args[++i];
					break;
				case "--pkcs7":
					if (i >= args.Length - 1)
						return false;
					pkcs7filename = args[++i];
					break;
				case "--import":
					import = true;
					break;
				case "--machine":
					machine = true;
					break;
				case "--sync":
					confirmAddition = false;
					confirmRemoval = false;
					break;
				case "--ask":
					confirmAddition = true;
					confirmRemoval = true;
					break;
				case "--ask-add":
					confirmAddition = true;
					confirmRemoval = false;
					break;
				case "--ask-remove":
					confirmAddition = false;
					confirmRemoval = true;
					break;
				case "--quiet":
					quiet = true;
					break;
				default:
					WriteLine("Unknown option '{0}'.");
					return false;
				}
			}
			return true;
		}

		static void Header()
		{
			Console.WriteLine(System.Reflection.Assembly.GetExecutingAssembly().ToString());
			Console.WriteLine("WARNING: mozroots is deprecated, please move to cert-sync instead.");
			Console.WriteLine();
		}

		static void Help()
		{
			Console.WriteLine("Usage: mozroots [--import [--machine] [--sync | --ask | --ask-add | --ask-remove]]");
			Console.WriteLine("Where the basic options are:");
			Console.WriteLine(" --import\tImport the certificates into the trust store.");
			Console.WriteLine(" --sync\t\tSynchronize (add/remove) the trust store with the certificates.");
			Console.WriteLine(" --ask\t\tAlways confirm before adding or removing trusted certificates.");
			Console.WriteLine(" --ask-add\tAlways confirm before adding a new trusted certificate.");
			Console.WriteLine(" --ask-remove\tAlways confirm before removing an existing trusted certificate.");
			Console.WriteLine("{0}and the advanced options are", Environment.NewLine);
			Console.WriteLine(" --url url\tSpecify an alternative URL for downloading the trusted");
			Console.WriteLine("\t\tcertificates (MXR source format).");
			Console.WriteLine(" --file name\tDo not download but use the specified file.");
			Console.WriteLine(" --pkcs7 name\tExport the certificates into a PKCS#7 file.");
			Console.WriteLine(" --machine\tImport the certificate in the machine trust store.");
			Console.WriteLine("\t\tThe default is to import into the user store.");
			Console.WriteLine(" --quiet\tLimit console output to errors and confirmations messages.");
		}

		static void WriteLine(string str)
		{
			if (!quiet)
				Console.WriteLine(str);
		}

		static void WriteLine(string format, params object[] args)
		{
			if (!quiet)
				Console.WriteLine(format, args);
		}

		static int Main(string[] args)
		{
			try
			{
				if (!ParseOptions(args))
				{
					Header();
					Help();
					return 1;
				}
				if (!quiet)
				{
					Header();
				}
				return Process();
			}
			catch (Exception e)
			{
				// ignore quiet on exception
				Console.WriteLine("Error: {0}", e);
				return 1;
			}
		}
	}
}