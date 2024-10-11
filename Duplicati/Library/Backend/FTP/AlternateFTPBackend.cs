using System.Collections.Generic;

namespace Duplicati.Library.Backend;


/// <summary>
/// This classes exists to provide a way to use the FTP backend with the alternate name "AlternateFTPBackend",
/// and supporting all existing configured backups that were created using the "aftp" prefix.
///
/// The functionality is identical to the FTP backend
/// </summary>
public class AlternateFTPBackend: FTP
{
    /* 
        These constants are overriden to provide transparent access to the alternate configuration keys
    */
    protected override string CONFIG_KEY_FTP_ENCRYPTION_MODE => "aftp-encryption-mode";
    protected override string CONFIG_KEY_FTP_DATA_CONNECTION_TYPE => "aftp-data-connection-type";
    protected override string CONFIG_KEY_FTP_SSL_PROTOCOLS => "aftp-ssl-protocols";
    protected override string CONFIG_KEY_FTP_UPLOAD_DELAY => "aftp-upload-delay";
    protected override string CONFIG_KEY_FTP_LOGTOCONSOLE => "aftp-log-to-console";
    protected override string CONFIG_KEY_FTP_LOGPRIVATEINFOTOCONSOLE => "aftp-log-privateinfo-to-console";
    
    public override string Description => Strings.DescriptionAlternate;
    
    public override string DisplayName => Strings.DisplayNameAlternate;

    /*
     * Likewise, this constant is overriden to provide transparent access to the alternate protocol key
    */
    public override string ProtocolKey { get; } = "aftp";

    public AlternateFTPBackend()
    {
        
    }
    public AlternateFTPBackend(string url, Dictionary<string, string> options): base(url, options)
    {
    }
}