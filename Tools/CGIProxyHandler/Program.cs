using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Runtime.InteropServices;

namespace CGIProxyHandler
{
	class MainClass
	{
		private class LogHandler
		{
			private object m_lock = new object();
			private StreamWriter m_stream;
			private bool m_debug;

			public LogHandler(StreamWriter writer, bool debug)
			{
				m_stream = writer;
				m_debug = debug;
			}
				
			public void WriteMessage(string msg)
			{
				if (m_stream != null)
					lock(m_lock)
						m_stream.WriteLine(msg);
			}

			public void WriteDebugMessage(string msg)
			{
				if (m_stream != null && m_debug)
					lock(m_lock)
						m_stream.WriteLine(msg);
			}

		}
			
		/// <summary>
		/// Runs an external command
		/// </summary>
		/// <returns>The stdout data.</returns>
		/// <param name="command">The executable</param>
		/// <param name="args">The executable and the arguments.</param>
		/// <param name="shell">If set to <c>true</c> use the shell context for execution.</param>
		/// <param name="exitcode">Set the value to check for a particular exitcode.</param>
		private static async Task<string> ShellExec(string command, string args = null, bool shell = false, int exitcode = -1, Dictionary<string, string> env = null)
		{
			var psi = new ProcessStartInfo() {
				FileName = command,
				Arguments = shell ? null : args,
				UseShellExecute = false,
				RedirectStandardInput = shell,
				RedirectStandardOutput = true
			};

			if (env != null)
				foreach (var pk in env)
					psi.EnvironmentVariables[pk.Key] = pk.Value;

			using (var p = Process.Start(psi))
			{
				if (shell && args != null)
					await p.StandardInput.WriteLineAsync(args);
				
				var res = await p.StandardOutput.ReadToEndAsync();

				p.WaitForExit((int)TimeSpan.FromSeconds(5).TotalMilliseconds);

				if (p.ExitCode != exitcode && exitcode != -1)
					throw new Exception(string.Format("Exit code was: {0}, stdout: {1}", p.ExitCode, res));
				return res;
			}
		}

		private static string GetEnvArg(string key, string @default = null)
		{
			var res = Environment.GetEnvironmentVariable(key);
			return string.IsNullOrWhiteSpace(res) ? @default : res.Trim();				
		}

		private const string CRLF = "\r\n";
		private static readonly byte[] ERROR_MESSAGE = System.Text.Encoding.ASCII.GetBytes("Status: 500 Server error" + CRLF + CRLF);

		public static void Main(string[] args)
		{
			var debug = GetEnvArg("PROXY_DEBUG", "0") == "1";
			var logfile = GetEnvArg("PROXY_LOGFILE", "/var/log/duplicat-proxy.log");

			using (var logout = new StreamWriter(File.Open(logfile, System.IO.FileMode.Append, FileAccess.Write, FileShare.ReadWrite)))
			using(var stdout = Console.OpenStandardOutput())
			{
				if (logout != null)
					logout.AutoFlush = true;


				var logger = new LogHandler(logout, debug);
				
				try
				{
					//stdout.WriteTimeout = (int)ACTIVITY_TIMEOUT.TotalMilliseconds;
					logger.WriteDebugMessage("Started!");
					logger.WriteDebugMessage(string.Format("Processing request for target url: {0}", GetEnvArg("HTTP_X_PROXY_PATH")));

					logger.WriteDebugMessage(string.Format("Redirects: {0},{1},{2}", Console.IsInputRedirected, Console.IsOutputRedirected, Console.IsErrorRedirected));

					if (args == null)
						args = new string[0];
					
					foreach(var a in args)
						logger.WriteDebugMessage(string.Format("arg: {0}", a));

					Run(args, logger, stdout).Wait();
				}
				catch (Exception ex)
				{
					var rex = ex;
					if (rex is AggregateException && (rex as AggregateException).Flatten().InnerExceptions.Count == 1)
						rex = (rex as AggregateException).Flatten().InnerExceptions.First();

					if (debug)
						logger.WriteMessage(string.Format("Failed: {0}", rex));
					else
						logger.WriteMessage(string.Format("Failed: {0}", rex.Message));
					
					try 
					{
						stdout.Write(ERROR_MESSAGE, 0, ERROR_MESSAGE.Length);
					}
					catch (Exception ex2) 
					{
						logger.WriteDebugMessage(string.Format("Failed to set error status: {0}", ex2));
					}
				}
			}
		}


		private static async Task Run(string[] args, LogHandler logger, Stream stdout)
		{
			var login_cgi = GetEnvArg("SYNO_LOGIN_CGI", "/usr/syno/synoman/webman/login.cgi");
			var auth_cgi = GetEnvArg("SYNO_AUTHENTICATE_CGI", "/usr/syno/synoman/webman/modules/authenticate.cgi");
			var admin_only = !(GetEnvArg("SYNO_ALL_USERS", "0") == "1");
			var auto_xsrf = GetEnvArg("SYNO_AUTO_XSRF", "1") == "1";
			var skip_auth = GetEnvArg("SYNO_SKIP_AUTH", "0") == "1";
			var query_string = GetEnvArg("QUERY_STRING", "");
			var proxy_host = GetEnvArg("PROXY_HOST", "localhost");
			var proxy_port = GetEnvArg("PROXY_PORT", "8200");

			var xsrftoken = GetEnvArg("HTTP_X_SYNO_TOKEN");
			if (string.IsNullOrWhiteSpace(xsrftoken) && !string.IsNullOrWhiteSpace(query_string))
			{
				// Avoid loading a library just for parsing the token
				var tkre = new Regex(@"SynoToken=(<?token>[^&+])");
				var m = tkre.Match(query_string);
				if (m.Success)
					xsrftoken = m.Groups["token"].Value;
			}

			if (!skip_auth)
			{
				if (string.IsNullOrWhiteSpace(xsrftoken) && auto_xsrf)
				{
					var authre = new Regex(@"""SynoToken""\s?\:\s?""(?<token>[^""]+)""");
					var resp = await ShellExec(login_cgi);

					logger.WriteDebugMessage(string.Format("xsrf response is: {0}", resp));
					
					var m = authre.Match(resp);
					if (m.Success)
						xsrftoken = m.Groups["token"].Value;
					else
						throw new Exception("Unable to get XSRF token");
				}

				var tmpenv = new Dictionary<string, string>();
				tmpenv["QUERY_STRING"] = "SynoToken=" + xsrftoken;

				var username = GetEnvArg("SYNO_USERNAME");

				if (string.IsNullOrWhiteSpace(username))
				{
					username = await ShellExec(auth_cgi, shell: false, exitcode: 0, env: tmpenv);
					logger.WriteDebugMessage(string.Format("Username: {0}", username));				
				}

				if (string.IsNullOrWhiteSpace(username))
					throw new Exception("Not logged in");
				
				username = username.Trim();

				if (admin_only)
				{
					var groups = GetEnvArg("SYNO_GROUP_IDS");

					if (string.IsNullOrWhiteSpace(groups))						
						groups = await ShellExec("id", "-G '" + username.Trim().Replace("'", "\\'") + "'", exitcode: 0) ?? "";
					if (!groups.Split(new char[] { ' ' }).Contains("101"))
						throw new Exception(string.Format("User {0} is not an admin", username));

					logger.WriteDebugMessage("User is admin");
				}
			}

			var path = GetEnvArg("HTTP_X_PROXY_PATH");
			if (string.IsNullOrWhiteSpace(path))
			{
				var xpre = new Regex(@"x-proxy-path=(<?url>[^&+])");
				var m = xpre.Match(query_string);
				if (m.Success)
					path = Uri.UnescapeDataString(m.Groups["url"].Value);
			}

			logger.WriteDebugMessage(string.Format("Path is {0} and query string is {1}", path, query_string));

			if (string.IsNullOrWhiteSpace(path) || !path.StartsWith("/"))
				throw new Exception("Invalid path requested");

			if (!string.IsNullOrWhiteSpace(query_string))
				path += (query_string.StartsWith("?") ? "" : "?") + Uri.EscapeUriString(query_string);

			int port;
			if (!int.TryParse(proxy_port, out port))
				port = 8200;

			logger.WriteDebugMessage(string.Format("About to connect to {0}:{1}", proxy_host, port));

			using (var client = new System.Net.Sockets.TcpClient())
			{
				logger.WriteDebugMessage(string.Format("Connecting to {0}:{1}", proxy_host, port));
				client.Connect(proxy_host, port);
				logger.WriteDebugMessage("Connected");
				
				using (var ns = client.GetStream())
				{
					logger.WriteDebugMessage("Opened TCP stream");
					
					using (var sw = new StreamWriter(ns))
					{
						logger.WriteDebugMessage("Created StreamWriter");
						
						//await ForwardRequest(sw, path, logout);
						//await ForwardResponse(ns, stdout, logout);

						await Task.WhenAll(
							ForwardRequest(sw, path, logger),
							ForwardResponse(ns, stdout, logger)
						);

						logger.WriteDebugMessage("Done processing");
					}
				}
			}
		}

		private static readonly byte[] STATUS_PREFIX = System.Text.Encoding.ASCII.GetBytes("Status: ");
		private static readonly int HTTP_HEAD_LEN = "HTTP/1.1 ".Length;


		private static async Task ForwardResponse(Stream source, Stream target, LogHandler logger)
		{
			var buf = new byte[8 * 1024];
			int r = 0;
			int offset = 0;
			var lastmatch = 0;
			var status = false;
			long contentlength = -1;
			var canceltoken = new CancellationTokenSource();

			logger.WriteDebugMessage("Forward response");

			while ((r = await source.ReadAsync(buf, offset, buf.Length - offset, canceltoken.Token)) != 0)
			{
				logger.WriteDebugMessage(string.Format("Read {0} bytes", r));
				
				offset += r;
				var ix = Array.IndexOf(buf, (byte)13, 0, offset);

				while (ix >= 0 && ix < offset - 1)
				{
					if (buf[ix + 1] == 10)
					{
						if (!status)
						{
							status = true;
							logger.WriteDebugMessage("Writing: Status: " + System.Text.Encoding.ASCII.GetString(buf, lastmatch + HTTP_HEAD_LEN, ix - lastmatch - HTTP_HEAD_LEN));

							await target.WriteAsync(STATUS_PREFIX, 0, STATUS_PREFIX.Length, canceltoken.Token);
							await target.WriteAsync(buf, lastmatch + HTTP_HEAD_LEN, (ix - lastmatch - HTTP_HEAD_LEN) + 2, canceltoken.Token);

							logger.WriteDebugMessage("Wrote status line");
						}
						else
						{
							// Blank line and we are done
							if (ix - lastmatch == 0)
							{
								logger.WriteDebugMessage(string.Format("Completed header, writing remaining {0} bytes", offset - lastmatch));

								await target.WriteAsync(buf, lastmatch, offset - lastmatch, canceltoken.Token);

								// Adjust remaining data length
								if (contentlength > 0)
									contentlength -= offset - lastmatch - 2;
							

								logger.WriteDebugMessage(string.Format("Body has remaining {0} bytes", contentlength));

								while(contentlength > 0)
								{
									r = await source.ReadAsync(buf, 0, (int)Math.Min(buf.Length, contentlength), canceltoken.Token);
									if (r == 0)
										break;

									contentlength -= r;


									await target.WriteAsync(buf, 0, r, canceltoken.Token);

									logger.WriteDebugMessage(string.Format("Body has remaining {0} bytes", contentlength));
								}

								await target.FlushAsync(canceltoken.Token);

								//await logout.WriteDebugMessageAsync(string.Format("Last body chunck: {0}", System.Text.Encoding.ASCII.GetString(buf, 0, r)));
								logger.WriteDebugMessage(string.Format("Completed response forward"));

								target.Close();

								return;
							}
							else
							{

								var header = System.Text.Encoding.ASCII.GetString(buf, lastmatch, ix - lastmatch) ?? string.Empty; 
								if (header.StartsWith("Content-Length: ", StringComparison.OrdinalIgnoreCase))
								if (!long.TryParse(header.Substring("Content-Length: ".Length), out contentlength))
									contentlength = -1;
									
								logger.WriteDebugMessage("Writing: " + header);

								await target.WriteAsync(buf, lastmatch, (ix - lastmatch) + 2, canceltoken.Token);
							}
							
						}

						lastmatch = ix + 2;
					}

					//await logger.WriteDebugMessageAsync(string.Format("Buf stats: {0},{1},{2},{3}", buf.Length, ix, offset, lastmatch));
					
					ix = Array.IndexOf(buf, (byte)13, ix + 1, offset - ix - 1);
				}
			}

		}

		private static async Task ForwardRequest(StreamWriter sw, string path, LogHandler logger)
		{
			var canceltoken = new CancellationTokenSource();
			var env = Environment.GetEnvironmentVariables();

			/*foreach (var k in env.Keys)
				logger.WriteDebugMessage(string.Format("{0}: {1}", k, env[k]));*/

			await sw.WriteAsync(string.Format("{0} {1} HTTP/1.1{2}", GetEnvArg("REQUEST_METHOD", "").Trim(), path, CRLF));

			logger.WriteDebugMessage("Wrote request header line");

			foreach (var key in env.Keys.Cast<string>().Where<string>(x => x.StartsWith("HTTP_")))
				await sw.WriteAsync(string.Format("{0}: {1}{2}", key.Substring("HTTP_".Length).Replace("_", "-"), env[key], CRLF));

			if (!string.IsNullOrWhiteSpace(GetEnvArg("CONTENT_TYPE")))
				await sw.WriteAsync(string.Format("{0}: {1}{2}", "Content-Type", GetEnvArg("CONTENT_TYPE"), CRLF));
			if (!string.IsNullOrWhiteSpace(GetEnvArg("CONTENT_LENGTH")))
				await sw.WriteAsync(string.Format("{0}: {1}{2}", "Content-Length", GetEnvArg("CONTENT_LENGTH"), CRLF));

			await sw.WriteAsync(string.Format("{0}: {1}{2}", "Connection", "close", CRLF));

			await sw.WriteAsync(CRLF);
			await sw.FlushAsync();
				
			logger.WriteDebugMessage("Wrote all header lines");

			if (new string[] { "POST", "PUT", "PATCH" }.Contains(GetEnvArg("REQUEST_METHOD", "").Trim().ToUpper()))
			{
				logger.WriteDebugMessage(string.Format("Copying StdIn"));

				using(var stdin = Console.OpenStandardInput())
				{
					logger.WriteDebugMessage("Opened StdIn");

					long reqsize;
					if (!long.TryParse(GetEnvArg("CONTENT_LENGTH"), out reqsize))
						reqsize = long.MaxValue;

					var buf = new byte[4 * 1024 * 1024];
					var r = 0;
					while(reqsize > 0)
					{
						logger.WriteDebugMessage(string.Format("Remaining {0} bytes from stdin", reqsize));
						
						r = await stdin.ReadAsync(buf, 0, buf.Length, canceltoken.Token);
						logger.WriteDebugMessage(string.Format("Got {0} bytes from stdin", r));

						if (r == 0)
							break;
						
						reqsize -= r;
						await sw.BaseStream.WriteAsync(buf, 0, r, canceltoken.Token);
					}
				}

				logger.WriteDebugMessage("Copy stdin done");

			}

			logger.WriteDebugMessage("Completed writing request");

		}

	}
}
