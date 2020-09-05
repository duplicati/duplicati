using Duplicati.Library.Localization.Short;

namespace Duplicati.Library.Backend
{
    internal static class Strings
    {
        #region Constants

        public const string API_ID_KEY = "api-id";
        public const string API_HASH_KEY = "api-hash";
        public const string PHONE_NUMBER_KEY = "phone-number";
        public const string AUTH_CODE_KEY = "auth-code";
        public const string AUTH_PASSWORD = "auth-password";
        public const string CHANNEL_NAME = "channel-name";

        #endregion

        #region General

        public static string DisplayName { get; } = "Telegram";
        public static string Description => LC.L("This backend can read and write data to a Telegram backend");

        #endregion

        #region Errors

        public static string NoAuthCodeError => LC.L("The auth code is missing");
        public static string NoPasswordError => LC.L("The password is missing");
        public static string NoChannelNameError => LC.L("The channel name is missing");
        public static string NoApiIdError => LC.L("The API ID is missing");
        public static string NoApiHashError => LC.L("The API hash is missing");
        public static string NoPhoneNumberError => LC.L("The phone number is missing");
        public static string ChannelIsNotEmptyError => LC.L("The selected channel is not empty");
        public static string CouldNotCreateChannelError => LC.L("Could not create channel. You can create it manually and try again");

        #endregion


        #region Descriptions

        public static string ApiIdShort => LC.L("The API ID");
        public static string ApiHashShort => LC.L("The API hash");
        public static string ApiIdLong => LC.L("The API ID retrieved from https://my.telegram.org/");
        public static string ApiHashLong => LC.L("The API hash retrieved from https://my.telegram.org/");
        public static string PhoneNumberShort => LC.L("Your phone number");
        public static string PhoneNumberLong => LC.L("The phone number you registered with");
        public static string AuthCodeShort => LC.L("The code you should have received (if you did)");
        public static string AuthCodeLong => LC.L("The auth code that you received. Input only if you did receive it");
        public static string PasswordShort => LC.L("2FA password (if enabled)");
        public static string PasswordLong => LC.L("The 2 step verification password. Input only if you have set it up");
        public static string ChannelNameShort => LC.L("The channel name of the backup");
        public static string ChannelNameLong => LC.L("The channel name used to re/store the backup files from/to");

        #endregion
    }
}