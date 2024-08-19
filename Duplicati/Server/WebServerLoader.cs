using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using Duplicati.Server.Database;

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
    /// The default path to the web root
    /// </summary>
    public const string DEFAULT_OPTION_WEBROOT = "webroot";

    /// <summary>
    /// The default listening port
    /// </summary>
    public const int DEFAULT_OPTION_PORT = 8200;

    /// <summary>
    /// Option for setting the webservice SSL certificate
    /// </summary>
    public const string OPTION_SSLCERTIFICATEFILE = "webservice-sslcertificatefile";

    /// <summary>
    /// Option for setting the webservice SSL certificate key
    /// </summary>
    public const string OPTION_SSLCERTIFICATEFILEPASSWORD = "webservice-sslcertificatepassword";

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
    /// <param name="Certificate">The certificate, if any</param>
    /// <param name="Servername">The servername to report</param>
    /// <param name="AllowedHostnames">The allowed hostnames</param>
    public record ParsedWebserverSettings(
        string WebRoot,
        int Port,
        System.Net.IPAddress Interface,
        X509Certificate2? Certificate,
        string Servername,
        IEnumerable<string> AllowedHostnames
    );


    /// <summary>
    /// Sets up the webserver and starts it
    /// </summary>
    /// <param name="options">A set of options</param>
    /// <param name="createServer">The method to start the server</param>
    public static async Task<TServer> TryRunServer<TServer>(IReadOnlyDictionary<string, string> options, Connection connection, Func<ParsedWebserverSettings, Task<TServer>> createServer)
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

        options.TryGetValue(OPTION_SSLCERTIFICATEFILE, out var certificateFile);
        options.TryGetValue(OPTION_SSLCERTIFICATEFILEPASSWORD, out var certificateFilePassword);
        certificateFilePassword = certificateFilePassword?.Trim() ?? "";

        X509Certificate2? cert = null;
        if (certificateFile == null)
        {
            try
            {
                cert = connection.ApplicationSettings.ServerSSLCertificate;
            }
            catch (Exception ex)
            {
                Library.Logging.Log.WriteWarningMessage(LOGTAG, "DefectStoredSSLCert", ex, Strings.Server.DefectSSLCertInDatabase);
            }
        }
        else if (certificateFile.Length == 0)
        {
            connection.ApplicationSettings.ServerSSLCertificate = null;
        }
        else
        {
            try
            {
                cert = new X509Certificate2(certificateFile, certificateFilePassword, X509KeyStorageFlags.Exportable);
            }
            catch (Exception ex)
            {
                throw new Exception(Strings.Server.SSLCertificateFailure(ex.Message), ex);
            }
        }

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
            string userroot = options[OPTION_WEBROOT];
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

        var certValid = cert != null && cert.HasPrivateKey;
        var settings = new ParsedWebserverSettings(
            webroot,
            -1,
            listenInterface,
            cert,
            string.Format("{0} v{1}", Library.AutoUpdater.AutoUpdateSettings.AppName, System.Reflection.Assembly.GetExecutingAssembly().GetName().Version),
            connection.ApplicationSettings.AllowedHostnames.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries)
        );

        // If we are in hosted mode with no specified port, 
        // then try different ports
        foreach (var p in ports)
            try
            {
                settings = settings with { Port = p };

                var server = await createServer(settings);
                if (interfacestring != connection.ApplicationSettings.ServerListenInterface)
                    connection.ApplicationSettings.ServerListenInterface = interfacestring;

                if (certValid && cert != connection.ApplicationSettings.ServerSSLCertificate)
                    connection.ApplicationSettings.ServerSSLCertificate = cert;

                Library.Logging.Log.WriteInformationMessage(LOGTAG, "ServerListening", Strings.Server.StartedServer(listenInterface.ToString(), p));

                return server;
            }
            catch (System.Net.Sockets.SocketException)
            {
            }

        throw new Exception(Strings.Server.ServerStartFailure(ports));
    }
}