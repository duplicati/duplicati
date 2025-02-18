// Copyright (C) 2025, The Duplicati Team
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
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Duplicati.Server.Database;
using Microsoft.AspNetCore.Connections;
using Duplicati.Library.Utility;

#nullable enable

namespace Duplicati.Server;

/// <summary>
/// Helper class for starting the webserver
/// </summary>
public static class WebServerLoader
{
    /// <summary>
    /// The tag used for logging
    /// </summary>
    private static readonly string LOGTAG = Duplicati.Library.Logging.Log.LogTagFromType(typeof(WebServerLoader));

    /// <summary>
    /// Option for changing the webroot folder
    /// </summary>
    public const string OPTION_WEBROOT = "webservice-webroot";

    /// <summary>
    /// Option for changing the webservice listen port
    /// </summary>
    public const string OPTION_PORT = "webservice-port";

    /// <summary>
    /// Option for changing the webservice listen interface
    /// </summary>
    public const string OPTION_INTERFACE = "webservice-interface";

    /// <summary>
    /// Option for setting the webservice password
    /// </summary>
    public const string OPTION_WEBSERVICE_PASSWORD = "webservice-password";

    /// <summary>
    /// Option for resetting the JWT configuration
    /// </summary>
    public const string OPTION_WEBSERVICE_RESET_JWT_CONFIG = "webservice-reset-jwt-config";

    /// <summary>
    /// Option for enabling the forever token
    /// </summary>
    public const string OPTION_WEBSERVICE_ENABLE_FOREVER_TOKEN = "webservice-enable-forever-token";

    /// <summary>
    /// Option for disabling the visual captcha
    /// </summary>
    public const string OPTION_WEBSERVICE_DISABLE_VISUAL_CAPTCHA = "webservice-disable-visual-captcha";

    /// <summary>
    /// Option for setting the webservice allowed hostnames
    /// </summary>
    public const string OPTION_WEBSERVICE_ALLOWEDHOSTNAMES = "webservice-allowed-hostnames";
    /// <summary>
    /// Option for setting the webservice allowed hostnames, alternative name
    /// </summary>
    public const string OPTION_WEBSERVICE_ALLOWEDHOSTNAMES_ALT = "webservice-allowedhostnames";

    /// <summary>
    /// Option for removing the hosted static files from the server
    /// </summary>
    public const string OPTION_WEBSERVICE_API_ONLY = "webservice-api-only";
    /// <summary>
    /// Option for disabling the use of signin tokens
    /// </summary>
    public const string OPTION_WEBSERVICE_DISABLE_SIGNIN_TOKENS = "webservice-disable-signin-tokens";
    /// <summary>
    /// Option for setting the webservice SPA paths
    /// </summary>
    public const string OPTION_WEBSERVICE_SPAPATHS = "webservice-spa-paths";
    /// <summary>
    /// The CORS origins to allow
    /// </summary>
    public const string OPTION_WEBSERVICE_CORS_ORIGINS = "webservice-cors-origins";
    /// <summary>
    /// Option for setting the webservice timezone
    /// </summary>
    public const string OPTION_WEBSERVICE_TIMEZONE = "webservice-timezone";

    /// <summary>
    /// The default path to the web root
    /// </summary>
    public const string DEFAULT_OPTION_WEBROOT = "webroot";

    /// <summary>
    /// The default paths to serve as SPAs
    /// </summary>
    public const string DEFAULT_OPTION_SPAPATHS = "/ngclient";

    /// <summary>
    /// The default listening port
    /// </summary>
    public const int DEFAULT_OPTION_PORT = 8200;

    /// <summary>
    /// Option for setting if to use HTTPS
    /// </summary>
    public const string OPTION_WEBSERVICE_DISABLEHTTPS = "webservice-disable-https";

    /// <summary>
    /// Option for removing the SSL certificate from the datbase
    /// </summary>
    public const string OPTION_WEBSERVICE_REMOVESSLCERTIFICATE = "webservice-remove-sslcertificate";

    /// <summary>
    /// Option for setting the webservice SSL certificate
    /// </summary>
    public const string OPTION_WEBSERVICE_SSLCERTIFICATEFILE = "webservice-sslcertificatefile";

    /// <summary>
    /// Option for setting the webservice SSL certificate key
    /// </summary>
    public const string OPTION_WEBSERVICE_SSLCERTIFICATEFILEPASSWORD = "webservice-sslcertificatepassword";

    /// <summary>
    /// The default listening interface
    /// </summary>
    public const string DEFAULT_OPTION_INTERFACE = "loopback";

    /// <summary>
    /// The parsed settings for the webserver
    /// </summary>
    /// <param name="WebRoot">The root folder with static files</param>
    /// <param name="Port">The listining port</param>
    /// <param name="Interface">The listening interface</param>
    /// <param name="Certificate">SSL certificate, if any</param>
    /// <param name="Servername">The servername to report</param>
    /// <param name="AllowedHostnames">The allowed hostnames</param>
    /// <param name="DisableStaticFiles">If static files should be disabled</param>
    /// <param name="SPAPaths">The paths to serve as SPAs</param>
    /// <param name="CorsOrigins">The origins to allow for CORS</param>
    public record ParsedWebserverSettings(
        string WebRoot,
        int Port,
        System.Net.IPAddress Interface,
        X509Certificate2Collection? Certificate,
        string Servername,
        IEnumerable<string> AllowedHostnames,
        bool DisableStaticFiles,
        IEnumerable<string> SPAPaths,
        IEnumerable<string> CorsOrigins
    );


    /// <summary>
    /// Sets up the webserver and starts it
    /// </summary>
    /// <param name="options">A set of options</param>
    /// <param name="createServer">The method to start the server</param>
    public static async Task<TServer> TryRunServer<TServer>(IReadOnlyDictionary<string, string?> options, Connection connection, Func<ParsedWebserverSettings, Task<TServer>> createServer)
    {
        var ports = Enumerable.Empty<int>();
        options.TryGetValue(OPTION_PORT, out var portstring);
        if (!string.IsNullOrEmpty(portstring))
            ports =
                from n in portstring.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                where int.TryParse(n, out _)
                select int.Parse(n);

        if (ports == null || !ports.Any())
            ports = [DEFAULT_OPTION_PORT];

        options.TryGetValue(OPTION_INTERFACE, out var interfacestring);

        if (string.IsNullOrWhiteSpace(interfacestring))
            interfacestring = connection.ApplicationSettings.ServerListenInterface;
        if (string.IsNullOrWhiteSpace(interfacestring))
            interfacestring = DEFAULT_OPTION_INTERFACE;

        var listenInterface = System.Net.IPAddress.Loopback;
        interfacestring = interfacestring.Trim();
        if (new[] { "*", "all", "any" }.Any(x => x.Equals(interfacestring, StringComparison.OrdinalIgnoreCase)))
            listenInterface = System.Net.IPAddress.Any;
        else if (interfacestring != "loopback")
            listenInterface = System.Net.IPAddress.Parse(interfacestring);

        var removeCertificate = Library.Utility.Utility.ParseBoolOption(options, OPTION_WEBSERVICE_REMOVESSLCERTIFICATE);
        connection.ApplicationSettings.DisableHTTPS = removeCertificate || Library.Utility.Utility.ParseBoolOption(options, OPTION_WEBSERVICE_DISABLEHTTPS);

        options.TryGetValue(OPTION_WEBSERVICE_SSLCERTIFICATEFILE, out var certificateFile);
        options.TryGetValue(OPTION_WEBSERVICE_SSLCERTIFICATEFILEPASSWORD, out var certificateFilePassword);
        certificateFilePassword = certificateFilePassword?.Trim();

        if (string.IsNullOrEmpty(certificateFile) && !string.IsNullOrEmpty(certificateFilePassword))
            Library.Logging.Log.WriteInformationMessage(LOGTAG, "ServerCertificate", Strings.Server.SSLCertificateFileMissingOption);

        if (!string.IsNullOrEmpty(certificateFile) && !string.IsNullOrEmpty(certificateFilePassword))
            connection.ApplicationSettings.ServerSSLCertificate = Utility.LoadPfxCertificate(certificateFile, certificateFilePassword);
        else if (removeCertificate)
            connection.ApplicationSettings.ServerSSLCertificate = null;

        var webroot = Library.AutoUpdater.UpdaterManager.INSTALLATIONDIR;

#if DEBUG
        //For debug we go "../../../../../.." to get out of "Executables/net8/Duplicati.GUI.TrayIcon/bin/debug/net8.0"
        string tmpwebroot = System.IO.Path.GetFullPath(System.IO.Path.Combine(webroot, "..", "..", "..", "..", "..", ".."));
        tmpwebroot = System.IO.Path.Combine(tmpwebroot, "Duplicati", "Server");
        if (System.IO.Directory.Exists(System.IO.Path.Combine(tmpwebroot, "webroot")))
            webroot = tmpwebroot;
#endif

        webroot = System.IO.Path.Combine(webroot, "webroot");

        if (options.ContainsKey(OPTION_WEBROOT))
        {
            var userroot = options[OPTION_WEBROOT];
#if DEBUG
            //In debug mode we do not care where the path points
#else
            //In release mode we check that the user supplied path is located
            // in the same folders as the running application, to avoid users
            // that inadvertently expose top level folders
            if (!string.IsNullOrWhiteSpace(userroot) && userroot.StartsWith(Duplicati.Library.Common.IO.Util.AppendDirSeparator(Duplicati.Library.Utility.Utility.getEntryAssembly().Location), Library.Utility.Utility.ClientFilenameStringComparison))
                webroot = userroot;
#endif
        }

        options.TryGetValue(OPTION_WEBSERVICE_SPAPATHS, out var spaPathsString);
        if (string.IsNullOrWhiteSpace(spaPathsString))
            spaPathsString = DEFAULT_OPTION_SPAPATHS;

        var settings = new ParsedWebserverSettings(
            webroot,
            -1,
            listenInterface,
            connection.ApplicationSettings.UseHTTPS ? connection.ApplicationSettings.ServerSSLCertificate : null,
            string.Format("{0} v{1}", Library.AutoUpdater.AutoUpdateSettings.AppName, Library.AutoUpdater.UpdaterManager.SelfVersion.Version),
            (connection.ApplicationSettings.AllowedHostnames ?? string.Empty).Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
            Duplicati.Library.Utility.Utility.ParseBoolOption(options, OPTION_WEBSERVICE_API_ONLY),
            spaPathsString.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
            options.GetValueOrDefault(OPTION_WEBSERVICE_CORS_ORIGINS)?.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries) ?? Enumerable.Empty<string>()
        );

        // Materialize the list of ports, and move the last-used port to the front, so we try the last-known port first
        ports = ports.ToList();
        if (ports.Contains(connection.ApplicationSettings.LastWebserverPort))
            ports = ports.Where(x => x != connection.ApplicationSettings.LastWebserverPort).Prepend(connection.ApplicationSettings.LastWebserverPort).ToList();

        // If we are in hosted mode with no specified port, 
        // then try different ports
        foreach (var p in ports)
            try
            {
                settings = settings with { Port = p };

                var server = await createServer(settings);

                // If we get here, the server started successfully, so store the new interface setting
                if (interfacestring != connection.ApplicationSettings.ServerListenInterface)
                    connection.ApplicationSettings.ServerListenInterface = interfacestring;

                Library.Logging.Log.WriteInformationMessage(LOGTAG, "ServerListening", Strings.Server.StartedServer(listenInterface.ToString(), p));

                return server;
            }
            catch (Exception ex) when
                (ex is System.Net.Sockets.SocketException { SocketErrorCode: System.Net.Sockets.SocketError.AddressAlreadyInUse }
                || ex is System.IO.IOException { InnerException: AddressInUseException })
            { }


        throw new Exception(Strings.Server.ServerStartFailure(ports));
    }
}