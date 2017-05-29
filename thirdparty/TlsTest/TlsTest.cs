//
// TlsTest.cs: TLS/SSL Test Program
//
// Author:
//	Sebastien Pouliot  <sebastien@xamarin.com>
//
// (C) 2004 Novell (http://www.novell.com)
// Copyright 2014 Xamarin Inc. (http://www.xamarin.com)
//

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reflection;
using System.Security.Cryptography.X509Certificates;
using System.Text;

using Mono.Security.Protocol.Tls;

public class TlsTest
{

	public static void Usage(string message)
	{
		Console.WriteLine("{0}tlstest - Copyright (c) 2004 Novell", Environment.NewLine);
		if (message != null)
		{
			Console.WriteLine("{0}{1}{0}", Environment.NewLine, message);
		}
		Console.WriteLine("Usage:");
		Console.WriteLine("tlstest [protocol] [class] [credentials] [--x:x509 [--x:x509]] [--time] [--show] url [...]");
		Console.WriteLine("{0}protocol (only applicable when using stream)", Environment.NewLine);
		Console.WriteLine("\t--any   \tNegotiate protocol [default]");
		Console.WriteLine("\t--ssl   \tUse SSLv3");
		Console.WriteLine("\t--ssl2  \tUse SSLv2 - unsupported on Mono");
		Console.WriteLine("\t--ssl3  \tUse SSLv3");
		Console.WriteLine("\t--tls   \tUse TLSv1");
		Console.WriteLine("\t--tls1  \tUse TLSv1");
		Console.WriteLine("\t--tls11 \tUse TLSv1.1");
		Console.WriteLine("\t--tls12 \tUse TLSv1.2");
		Console.WriteLine("{0}class", Environment.NewLine);
		Console.WriteLine("\t--stream\tDirectly use the SslClientStream [default]");
		Console.WriteLine("\t--web   \tUse the WebRequest/WebResponse classes");
		Console.WriteLine("{0}credentials", Environment.NewLine);
		Console.WriteLine("\t--basic:username:password:domain\tBasic Authentication");
		Console.WriteLine("\t--digest:username:password:domain\tDigest Authentication");
		Console.WriteLine("{0}options", Environment.NewLine);
		Console.WriteLine("\t--x:x509\tX.509 client certificate (multiple entries allowed");
		Console.WriteLine("\t--time  \tShow the time required for each page load");
		Console.WriteLine("\t--show  \tShow the web page content on screen");
		Console.WriteLine("{0}\turl [...]\tOne, or more, URL to download{0}", Environment.NewLine);
	}

	private static bool show;
	private static bool time;
	private static bool web;
	private static Mono.Security.Protocol.Tls.SecurityProtocolType? protocol;
	private static X509CertificateCollection certificates = new X509CertificateCollection();
	private static NetworkCredential basicCred;
	private static NetworkCredential digestCred;

	public static void Main(string[] args)
	{
		if (args.Length == 0)
		{
			Usage("Missing arguments");
			return;
		}

		ArrayList urls = new ArrayList();
		foreach (string arg in args)
		{
			switch (arg)
			{
			// protocol
			case "--any":
				protocol = Mono.Security.Protocol.Tls.SecurityProtocolType.Default;
				break;
			case "--ssl":
			case "--ssl3":
				protocol = Mono.Security.Protocol.Tls.SecurityProtocolType.Ssl3;
				break;
			case "--ssl2":
				protocol = Mono.Security.Protocol.Tls.SecurityProtocolType.Ssl2;
				// note: will only works with Fx 1.2
				// but the tool doesn't link with it
				Usage("Not supported");
				return;
			case "--tls":
			case "--tls1":
				protocol = Mono.Security.Protocol.Tls.SecurityProtocolType.Tls;
				break;
			case "--tls11":
				protocol = Mono.Security.Protocol.Tls.SecurityProtocolType.Tls11;
				break;
			case "--tls12":
				protocol = Mono.Security.Protocol.Tls.SecurityProtocolType.Tls12;
				break;
			// class
			case "--stream":
				web = false;
				break;
			case "--web":
				web = true;
				break;
			// options
			case "--time":
				time = true;
				break;
			case "--show":
				show = true;
				break;
			case "--help":
				Usage(null);
				return;
			// credentials, certificates, urls or bad options
			default:
				if (arg.StartsWith("--digest:"))
				{
					digestCred = GetCredentials(arg.Substring(9));
					continue;
				}
				else if (arg.StartsWith("--basic:"))
				{
					basicCred = GetCredentials(arg.Substring(8));
					continue;
				}
				else if (arg.StartsWith("--x:"))
				{
					string filename = arg.Substring(4);
					X509Certificate x509 = X509Certificate.CreateFromCertFile(filename);
					certificates.Add(x509);
					continue;
				}
				else if (arg.StartsWith("--"))
				{
					Usage("Invalid option " + arg);
					return;
				}
				urls.Add(arg);
				break;
			}
		}

		if (urls.Count == 0)
		{
			Usage("no URL were specified");
			return;
		}

		foreach (string url in urls)
		{
			Console.WriteLine("{0}{1}", Environment.NewLine, url);
			string content = null;
			DateTime start = DateTime.Now;

			try
			{
				if (web)
				{
					content = GetWebPage(url);
				}
				else
				{
					content = GetStreamPage(url);
				}
			}
			catch (Exception e)
			{
				// HResult is now public (was protected before 4.5)
				Console.WriteLine("FAILED: #{0}", e.HResult);
				Console.WriteLine(e.ToString());
			}

			TimeSpan ts = (DateTime.Now - start);
			if ((show) && (content != null))
			{
				Console.WriteLine("{0}{1}{0}", Environment.NewLine, content);
			}
			if (time)
			{
				Console.WriteLine("Time: " + ts.ToString());
			}
		}
	}

	public static string GetWebPage(string url)
	{
		ServicePointManager.CertificatePolicy = new TestCertificatePolicy();
		if (protocol != null)
			ServicePointManager.SecurityProtocol = (System.Net.SecurityProtocolType)(int)protocol.Value;

		Uri uri = new Uri(url);
		HttpWebRequest req = (HttpWebRequest)WebRequest.Create(uri);

		if ((digestCred != null) || (basicCred != null))
		{
			CredentialCache cache = new CredentialCache();
			if (digestCred != null)
				cache.Add(uri, "Digest", digestCred);
			if (basicCred != null)
				cache.Add(uri, "Basic", basicCred);
			req.Credentials = cache;
		}

		if (certificates.Count > 0)
			req.ClientCertificates.AddRange(certificates);

		WebResponse resp = req.GetResponse();
		Stream stream = resp.GetResponseStream();
		StreamReader sr = new StreamReader(stream, Encoding.UTF8);
		return sr.ReadToEnd();
	}

	public static string GetStreamPage(string url)
	{
		Uri uri = new Uri(url);
		if (uri.Scheme != Uri.UriSchemeHttps)
			throw new NotSupportedException("Stream only works with HTTPS protocol");

		IPHostEntry host = Dns.Resolve(uri.Host);
		IPAddress ip = host.AddressList[0];
		Socket socket = new Socket(ip.AddressFamily, SocketType.Stream, ProtocolType.Tcp);
		socket.Connect(new IPEndPoint(ip, uri.Port));
		NetworkStream ns = new NetworkStream(socket, false);
		SslClientStream ssl = new SslClientStream(ns, uri.Host, false, protocol ?? Mono.Security.Protocol.Tls.SecurityProtocolType.Default, certificates);
		ssl.ServerCertValidationDelegate += new CertificateValidationCallback(CertificateValidation);

		StreamWriter sw = new StreamWriter(ssl);
		sw.WriteLine("GET {0} HTTP/1.0{1}", uri.AbsolutePath, Environment.NewLine);
		sw.Flush();

		StreamReader sr = new StreamReader(ssl, Encoding.UTF8);
		return sr.ReadToEnd();
	}

	private static NetworkCredential GetCredentials(string credentials)
	{
		string[] creds = credentials.Split(':');
		NetworkCredential nc = new NetworkCredential();
		nc.UserName = ((creds.Length > 0) ? creds[0] : String.Empty);
		nc.Password = ((creds.Length > 1) ? creds[1] : String.Empty);
		nc.Domain = ((creds.Length > 2) ? creds[2] : String.Empty);
		return nc;
	}

	private static void ShowCertificateError(int error)
	{
		string message = null;
		switch (error)
		{
		case -2146762490:
			message = "CERT_E_PURPOSE 0x800B0106";
			break;
		case -2146762481:
			message = "CERT_E_CN_NO_MATCH 0x800B010F";
			break;
		case -2146869223:
			message = "TRUST_E_BASIC_CONSTRAINTS 0x80096019";
			break;
		case -2146869232:
			message = "TRUST_E_BAD_DIGEST 0x80096010";
			break;
		case -2146762494:
			message = "CERT_E_VALIDITYPERIODNESTING 0x800B0102";
			break;
		case -2146762495:
			message = "CERT_E_EXPIRED 0x800B0101";
			break;
		case -2146762486:
			message = "CERT_E_CHAINING 0x800B010A";
			break;
		case -2146762487:
			message = "CERT_E_UNTRUSTEDROOT 0x800B0109";
			break;
		default:
			message = "unknown (try WinError.h)";
			break;
		}
		Console.WriteLine("Error #{0}: {1}", error, message);
	}

	private static bool CertificateValidation(X509Certificate certificate, int[] certificateErrors)
	{
		if (certificateErrors.Length > 0)
		{
			Console.WriteLine(certificate.ToString(true));
			// X509Certificate.ToString(true) doesn't show dates :-(
			Console.WriteLine("\tValid From:  {0}", certificate.GetEffectiveDateString());
			Console.WriteLine("\tValid Until: {0}{1}", certificate.GetExpirationDateString(), Environment.NewLine);
			// multiple errors are possible using SslClientStream
			foreach (int error in certificateErrors)
			{
				ShowCertificateError(error);
			}
		}
		// whatever the reason we do not stop the SSL connection
		return true;
	}

	public class TestCertificatePolicy : ICertificatePolicy
	{

		public bool CheckValidationResult(ServicePoint sp, X509Certificate certificate, WebRequest request, int error)
		{
			if (error != 0)
			{
				Console.WriteLine(certificate.ToString(true));
				// X509Certificate.ToString(true) doesn't show dates :-(
				Console.WriteLine("\tValid From:  {0}", certificate.GetEffectiveDateString());
				Console.WriteLine("\tValid Until: {0}{1}", certificate.GetExpirationDateString(), Environment.NewLine);

				ShowCertificateError(error);
			}
			// whatever the reason we do not stop the SSL connection
			return true;
		}
	}
}
