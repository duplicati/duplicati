using Duplicati.Library.Localization.Short;
using System;


namespace Duplicati.Library.Modules.Builtin.Strings {
    internal static class ConsolePasswordInput {
        public static string ConfirmPassphrasePrompt { get { return LC.L(@"Confirm encryption passphrase"); } }
        public static string Description { get { return LC.L(@"This module will ask the user for an encryption password on the command line unless encryption is disabled or the password is supplied by other means"); } }
        public static string Displayname { get { return LC.L(@"Password prompt"); } }
        public static string EmptyPassphraseError { get { return LC.L(@"Empty passphrases are not allowed"); } }
        public static string EnterPassphrasePrompt { get { return LC.L(@"Enter encryption passphrase"); } }
        public static string PassphraseMismatchError { get { return LC.L(@"The passphrases do not match"); } }
    }
    internal static class CheckMonoSSL {
        public static string Description { get { return LC.L(@"When running with Mono, this module will check if any certificates are installed and suggest installing them otherwise"); } }
        public static string Displayname { get { return LC.L(@"Check for SSL certificates"); } }
        public static string ErrorMessage { get { return LC.L(@"No certificates found, you can install some with one of these commands:{0}    cert-sync /etc/ssl/certs/ca-certificates.crt #for Debian based systems{0}    cert-sync /etc/pki/tls/certs/ca-bundle.crt #for RedHat derivatives{0}Read more: {1}", Environment.NewLine, "http://www.mono-project.com/docs/about-mono/releases/3.12.0/#cert-sync"); } }
    }
    internal static class HttpOptions {
        public static string Description { get { return LC.L(@"This module exposes a number of properties that can be used to change the way http requests are issued"); } }
        public static string DescriptionAcceptAnyCertificateLong { get { return LC.L(@"Use this option to accept any server certificate, regardless of what errors it may have. Please use --accept-specified-ssl-hash instead, whenever possible."); } }
        public static string DescriptionAcceptAnyCertificateShort { get { return LC.L(@"Accept any server certificate"); } }
        public static string DescriptionAcceptHashLong2 { get { return LC.L(@"If your server certificate is reported as invalid (eg. with self-signed certificates), you can supply the certificate hash to approve it anyway. The hash value must be entered in hex format without spaces. You can enter multiple hashes separated by commas."); } }
        public static string DescriptionAcceptHashShort { get { return LC.L(@"Optionally accept a known SSL certificate"); } }
        public static string DisableExpect100Long { get { return LC.L(@"The default HTTP request has the header ""Expect: 100-Continue"" attached, which allows some optimizations when authenticating, but also breaks some web servers, causing them to report ""417 - Expectation failed"""); } }
        public static string DisableExpect100Short { get { return LC.L(@"Disable the expect header"); } }
        public static string DisableNagleLong { get { return LC.L(@"By default the http requests use the RFC 896 nagling algorithm to support transfer of small packages more efficiently."); } }
        public static string DisableNagleShort { get { return LC.L(@"Disable nagling"); } }
        public static string DisplayName { get { return LC.L(@"Configure http requests"); } }
        public static string OauthurlShort { get { return LC.L(@"Alternate OAuth URL"); } }
        public static string OauthurlLong { get { return LC.L(@"Duplicati uses an external server to support the OAuth authentication flow. If you have set up your own Duplicati OAuth server, you can supply the refresh url."); } }
        public static string SslversionsShort { get { return LC.L(@"Sets allowed SSL versions"); } }
        public static string SslversionsLong { get { return LC.L(@"This option changes the default SSL versions allowed. This is an advanced option and should only be used if you want to enhance security or work around an issue with a particular SSL protocol."); } }
    }
    internal static class HyperVOptions {
        public static string Description { get { return LC.L(@"This module works internaly to parse source parameters to backup Hyper-V virtual machines"); } }
        public static string DisplayName { get { return LC.L(@"Configure Hyper-V module"); } }
    }
    internal static class MSSQLOptions
    {
        public static string Description { get { return LC.L(@"This module works internaly to parse source parameters to backup Microsoft SQL Server databases"); } }
        public static string DisplayName { get { return LC.L(@"Configure Microsoft SQL Server module"); } }
    }
    internal static class RunScript {
        public static string Description { get { return LC.L(@"Executes a script before starting an operation, and again on completion"); } }
        public static string DisplayName { get { return LC.L(@"Run script"); } }
        public static string FinishoptionLong { get { return LC.L(@"Executes a script after performing an operation. The script will receive the operation results written to stdout."); } }
        public static string FinishoptionShort { get { return LC.L(@"Run a script on exit"); } }
        public static string InvalidExitCodeError(string script, int exitcode) { return LC.L(@"The script ""{0}"" returned with exit code {1}", script, exitcode); }
        public static string RequiredoptionLong { get { return LC.L(@"Executes a script before performing an operation. The operation will block until the script has completed or timed out. If the script returns a non-zero error code or times out, the operation will be aborted."); } }
        public static string RequiredoptionShort { get { return LC.L(@"Run a required script on startup"); } }
        public static string ScriptExecuteError(string script, string message) { return LC.L(@"Error while executing script ""{0}"": {1}", script, message); }
        public static string ScriptTimeoutError(string script) { return LC.L(@"Execution of the script ""{0}"" timed out", script); }
        public static string StartupoptionLong { get { return LC.L(@"Executes a script before performing an operation. The operation will block until the script has completed or timed out."); } }
        public static string StartupoptionShort { get { return LC.L(@"Run a script on startup"); } }
        public static string StdErrorReport(string script, string message) { return LC.L(@"The script ""{0}"" reported error messages: {1}", script, message); }
        public static string TimeoutoptionLong { get { return LC.L(@"Sets the maximum time a script is allowed to execute. If the script has not completed within this time, it will continue to execute but the operation will continue too, and no script output will be processed."); } }
        public static string TimeoutoptionShort { get { return LC.L(@"Sets the script timeout"); } }
    }
    internal static class SendMail {
        public static string Description { get { return LC.L(@"This module can send email after an operation completes"); } }
        public static string Displayname { get { return LC.L(@"Send mail"); } }
        public static string FailedToLookupMXServer(string optionname) { return LC.L(@"Unable to find the destination mail server through MX lookup, please use the option {0} to specify what smtp server to use.", optionname); }
        public static string OptionBodyLong { get { return LC.L(@"This value can be a filename. If the file exists, the file contents will be used as the message body.

In the message body, certain tokens are replaced:
%OPERATIONNAME% - The name of the operation, normally ""Backup""
%REMOTEURL% - Remote server url
%LOCALPATH% - The path to the local files or folders involved in the operation (if any)
%PARSEDRESULT% - The parsed result, if the operation is a backup. Possible values are: Error, Warning, Success

All command line options are also reported within %value%, e.g. %volsize%. Any unknown/unset value is removed."); } }
        public static string OptionBodyShort { get { return LC.L(@"The message body"); } }
        public static string OptionPasswordLong { get { return LC.L(@"The password used to authenticate with the SMTP server if required."); } }
        public static string OptionPasswordShort { get { return LC.L(@"SMTP Password"); } }
        public static string OptionRecipientLong { get { return LC.L(@"This setting is required if mail should be sent, all other settings have default values. You can supply multiple email addresses separated with commas, and you can use the normal address format as specified by RFC2822 section 3.4.
Example with 3 recipients: 

Peter Sample <peter@example.com>, John Sample <john@example.com>, admin@example.com"); } }
        public static string OptionRecipientShort { get { return LC.L(@"Email recipient(s)"); } }
        public static string OptionSendallLong { get { return LC.L(@"By default, mail will only be sent after a Backup operation. Use this option to send mail for all operations."); } }
        public static string OptionSendallShort { get { return LC.L(@"Send email for all operations"); } }
        public static string OptionSenderLong { get { return LC.L(@"Address of the email sender. If no host is supplied, the hostname of the first recipient is used. Examples of allowed formats:

sender
sender@example.com
Mail Sender <sender>
Mail Sender <sender@example.com>"); } }
        public static string OptionSenderShort { get { return LC.L(@"Email sender"); } }
        public static string OptionSendlevelLong(string success, string warning, string error, string fatal, string all) { return LC.L(@"You can specify one of ""{0}"", ""{1}"", ""{2}"", ""{3}"". You can supply multiple options with a comma separator, e.g. ""{0},{1}"". The special value ""{4}"" is a shorthand for ""{0},{1},{2},{3}"" and will cause all backup operations to send an email.", success, warning, error, fatal, all); }
        public static string OptionSendlevelShort { get { return LC.L(@"The messages to send"); } }
        public static string OptionServerLong { get { return LC.L(@"A url for the SMTP server, e.g. smtp://example.com:25. Multiple servers can be supplied in a prioritized list, separated with semicolon. If a server fails, the next server in the list is tried, until the message has been sent.
If no server is supplied, a DNS lookup is performed to find the first recipient's MX record, and all SMTP servers are tried in their priority order until the message is sent.

To enable SMTP over SSL, use the format smtps://example.com. To enable SMTP STARTTLS, use the format smtp://example.com:25/?starttls=when-available or smtp://example.com:25/?starttls=always. If no port is specified, port 25 is used for non-ssl, and 465 for SSL connections. To force not to use STARTTLS use smtp://example.com:25/?starttls=never."); } }
        public static string OptionServerShort { get { return LC.L(@"SMTP Url"); } }
        public static string OptionSubjectLong(string optionname) { return LC.L(@"This setting supplies the email subject. Values are replaced as described in the description for --{0}.", optionname); }
        public static string OptionSubjectShort { get { return LC.L(@"The email subject"); } }
        public static string OptionUsernameLong { get { return LC.L(@"The username used to authenticate with the SMTP server if required."); } }
        public static string OptionUsernameShort { get { return LC.L(@"SMTP Username"); } }
        public static string SendMailFailedError(string message) { return LC.L(@"Failed to send email: {0}", message); }
        public static string SendMailLog(string message) { return LC.L(@"Whole SMTP communication: {0}", message); }
        public static string SendMailFailedRetryError(string failedserver, string message, string retryserver) { return LC.L(@"Failed to send email with server: {0}, message: {1}, retrying with {2}", failedserver, message, retryserver); }
        public static string SendMailSuccess(string server) { return LC.L(@"Email sent successfully using server: {0}", server); }
    }
    internal static class SendJabberMessage {
        public static string SendxmpptoShort { get { return LC.L(@"XMPP recipient email"); } }
        public static string SendxmpptoLong { get { return LC.L(@"The users who should have the messages sent, specify multiple users separated with commas"); } }
        public static string SendxmppmessageShort { get { return LC.L(@"The message template"); } }
        public static string SendxmppmessageLong { get { return LC.L(@"This value can be a filename. If the file exists, the file contents will be used as the message.

In the message, certain tokens are replaced:
%OPERATIONNAME% - The name of the operation, normally ""Backup""
%REMOTEURL% - Remote server url
%LOCALPATH% - The path to the local files or folders involved in the operation (if any)
%PARSEDRESULT% - The parsed result, if the operation is a backup. Possible values are: Error, Warning, Success

All command line options are also reported within %value%, e.g. %volsize%. Any unknown/unset value is removed."); } }
        public static string SendxmppusernameShort { get { return LC.L(@"The XMPP username"); } }
        public static string SendxmppusernameLong { get { return LC.L(@"The username for the account that will send the message, including the hostname. I.e. ""account@jabber.org/Home"""); } }
        public static string SendxmpppasswordShort { get { return LC.L(@"The XMPP password"); } }
        public static string SendxmpppasswordLong { get { return LC.L(@"The password for the account that will send the message"); } }
        public static string SendxmpplevelShort { get { return LC.L(@"The messages to send"); } }
        public static string SendxmpplevelLong(string success, string warning, string error, string fatal, string all) { return LC.L(@"You can specify one of ""{0}"", ""{1}"", ""{2}"", ""{3}"". 
You can supply multiple options with a comma separator, e.g. ""{0},{1}"". The special value ""{4}"" is a shorthand for ""{0},{1},{2},{3}"" and will cause all backup operations to send a message.", success, warning, error, fatal, all); }
        public static string SendxmppanyoperationShort { get { return LC.L(@"Send messages for all operations"); } }
        public static string SendxmppanyoperationLong { get { return LC.L(@"By default, messages will only be sent after a Backup operation. Use this option to send messages for all operations"); } }
        public static string DisplayName { get { return LC.L(@"XMPP report module"); } }
        public static string Description { get { return LC.L(@"This module provides support for sending status reports via XMPP messages"); } }
        public static string LoginTimeoutError { get { return LC.L(@"Timeout occurred while logging in to jabber server"); } }
        public static string SendMessageError(string message) { return LC.L(@"Failed to send jabber message: {0}", message); }
    }

    internal static class SendHttpMessage {
        public static string DisplayName { get { return LC.L(@"HTTP report module"); } }
        public static string Description { get { return LC.L(@"This module provides support for sending status reports via HTTP messages"); } }
        public static string SendhttpurlShort { get { return LC.L(@"HTTP report url"); } }
        public static string SendhttpurlLong { get { return LC.L(@"HTTP report url"); } }
        public static string SendhttpmessageShort { get { return LC.L(@"The message template"); } }
        public static string SendhttpmessageLong { get { return LC.L(@"This value can be a filename. If the file exists, the file contents will be used as the message.

In the message, certain tokens are replaced:
%OPERATIONNAME% - The name of the operation, normally ""Backup""
%REMOTEURL% - Remote server url
%LOCALPATH% - The path to the local files or folders involved in the operation (if any)
%PARSEDRESULT% - The parsed result, if the operation is a backup. Possible values are: Error, Warning, Success

All command line options are also reported within %value%, e.g. %volsize%. Any unknown/unset value is removed."); } }
        public static string SendhttpmessageparameternameShort { get { return LC.L(@"The name of the parameter to send the message as"); } }
        public static string SendhttpmessageparameternameLong { get { return LC.L(@"The name of the parameter to send the message as."); } }
        public static string SendhttpextraparametersShort { get { return LC.L(@"Extra parameters to add to the http message"); } }
        public static string SendhttpextraparametersLong { get { return LC.L(@"Extra parameters to add to the http message. I.e. ""parameter1=value1&parameter2=value2"""); } }
        public static string SendhttplevelShort { get { return LC.L(@"The messages to send"); } }
        public static string SendhttplevelLong(string success, string warning, string error, string fatal, string all) { return LC.L(@"You can specify one of ""{0}"", ""{1}"", ""{2}"", ""{3}"". 
You can supply multiple options with a comma separator, e.g. ""{0},{1}"". The special value ""{4}"" is a shorthand for ""{0},{1},{2},{3}"" and will cause all backup operations to send a message.", success, warning, error, fatal, all); }
        public static string SendhttpanyoperationShort { get { return LC.L(@"Send messages for all operations"); } }
        public static string SendhttpanyoperationLong { get { return LC.L(@"By default, messages will only be sent after a Backup operation. Use this option to send messages for all operations"); } }
        public static string SendMessageError(string message) { return LC.L(@"Failed to send http message: {0}", message); }
    }
}
