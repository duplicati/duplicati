using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Security;
using System.Text;
using System.Windows.Forms;
using System.ComponentModel;

namespace Microsoft.Win32.TaskScheduler
{
    /// <summary>
    /// Dialog box which prompts for user credentials using the Win32 CREDUI methods.
    /// </summary>
    [ToolboxItem(true), ToolboxItemFilter("System.Windows.Forms.Control.TopLevel"), Description("Dialog that prompts the user for credentials."), Designer("System.ComponentModel.Design.ComponentDesigner, System.Design, Version=2.0.0.0, Culture=neutral, PublicKeyToken=b03f5f7f11d50a3a"), DesignTimeVisible(true)]
    public class CredentialsDialog : CommonDialog
    {
        private const int maxStringLength = 100;

        /// <summary>
        /// Initializes a new instance of the <see cref="CredentialsDialog"/> class.
        /// </summary>
        public CredentialsDialog()
        {
            Reset();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CredentialsDialog"/> class.
        /// </summary>
        /// <param name="caption">The caption.</param>
        /// <param name="message">The message.</param>
        /// <param name="userName">Name of the user.</param>
        /// <param name="options">The options.</param>
        public CredentialsDialog(string caption) : this(caption, null, null, CredentialsDialogOptions.Default) { }
        public CredentialsDialog(string caption, string message) : this(caption, message, null, CredentialsDialogOptions.Default) { }
        public CredentialsDialog(string caption, string message, string userName) : this(caption, message, userName, CredentialsDialogOptions.Default) { }
        public CredentialsDialog(string caption, string message, string userName, CredentialsDialogOptions options)
            : this()
        {
            this.Caption = caption;
            this.Message = message;
            this.UserName = userName;
            this.Options = options;
        }

        internal enum CredUIReturnCodes
        {
            NO_ERROR = 0,
            ERROR_CANCELLED = 1223,
            ERROR_NO_SUCH_LOGON_SESSION = 1312,
            ERROR_NOT_FOUND = 1168,
            ERROR_INVALID_ACCOUNT_NAME = 1315,
            ERROR_INSUFFICIENT_BUFFER = 122,
            ERROR_INVALID_PARAMETER = 87,
            ERROR_INVALID_FLAGS = 1004,
        }

        /// <summary>
        /// Gets or sets the Windows Error Code that caused this credential dialog to appear, if applicable.
        /// </summary>
        [System.ComponentModel.DefaultValue(0), Category("Data"), Description("Windows Error Code that caused this credential dialog")]
        public int AuthenticationError { get; set; }

        /// <summary>
        /// Gets or sets the image to display as the banner for the dialog
        /// </summary>
        [System.ComponentModel.DefaultValue((string)null), Category("Appearance"), Description("Image to display in dialog banner")]
        public Bitmap Banner { get; set; }

        /// <summary>
        /// Gets or sets the caption for the dialog
        /// </summary>
        [System.ComponentModel.DefaultValue((string)null), Category("Appearance"), Description("Caption to display for dialog")]
        public string Caption { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether to encrypt password.
        /// </summary>
        /// <value><c>true</c> if password is to be encrypted; otherwise, <c>false</c>.</value>
        [System.ComponentModel.DefaultValue(false), Category("Behavior"), Description("Indicates whether to encrypt password")]
        public bool EncryptPassword { get; set; }

        /// <summary>
        /// Gets or sets the message to display on the dialog
        /// </summary>
        [System.ComponentModel.DefaultValue((string)null), Category("Appearance"), Description("Message to display in the dialog")]
        public string Message { get; set; }

        /// <summary>
        /// Gets or sets the options for the dialog.
        /// </summary>
        /// <value>The options.</value>
        [System.ComponentModel.DefaultValue(typeof(CredentialsDialogOptions), "Default"), Category("Behavior"), Description("Options for the dialog")]
        public CredentialsDialogOptions Options { get; set; }

        /// <summary>
        /// Gets the password entered by the user
        /// </summary>
        [System.ComponentModel.DefaultValue((string)null), Browsable(false)]
        public string Password { get; private set; }

        /// <summary>
        /// Gets or sets a boolean indicating if the save check box was checked
        /// </summary>
        /// <remarks>
        /// Only valid if <see cref="CredentialsDialog.Options"/> has the <see cref="CredentialsDialogOptions.DoNotPersist"/> flag set.
        /// </remarks>
        [System.ComponentModel.DefaultValue(false), Category("Behavior"), Description("Indicates if the save check box is checked.")]
        public bool SaveChecked { get; set; }

        /// <summary>
        /// Gets the password entered by the user using an encrypted string
        /// </summary>
        [System.ComponentModel.DefaultValue(null), Browsable(false)]
        public SecureString SecurePassword { get; private set; }

        /// <summary>
        /// Gets or sets the name of the target for these credentials
        /// </summary>
        /// <remarks>
        /// This value is used as a key to store the credentials if persisted
        /// </remarks>
        [System.ComponentModel.DefaultValue((string)null), Category("Data"), Description("Target for the credentials")]
        public string Target { get; set; }

        /// <summary>
        /// Gets or sets the username entered
        /// </summary>
        /// <remarks>
        /// If non-empty before calling <see cref="RunDialog"/>, this value will be displayed in the dialog
        /// </remarks>
        [System.ComponentModel.DefaultValue((string)null), Category("Data"), Description("User name displayed in the dialog")]
        public string UserName { get; set; }

        /// <summary>
        /// Gets a default value for the target.
        /// </summary>
        /// <value>The default target.</value>
        private string DefaultTarget
        {
            get { return Environment.UserDomainName; }
        }

        /// <summary>
        /// Confirms the credentials.
        /// </summary>
        /// <param name="storedCredentials">If set to <c>true</c> the credentials are stored in the credential manager.</param>
        public void ConfirmCredentials(bool storedCredentials)
        {
            CredUIReturnCodes ret = CredUIConfirmCredentials(this.Target, storedCredentials);
            if (ret != CredUIReturnCodes.NO_ERROR && ret != CredUIReturnCodes.ERROR_INVALID_PARAMETER)
                throw new InvalidOperationException(String.Format("Call to CredUIConfirmCredentials failed with error code: {0}", ret));
        }

        /// <summary>
        /// When overridden in a derived class, resets the properties of a common dialog box to their default values.
        /// </summary>
        public override void Reset()
        {
            this.Target = this.UserName = this.Caption = this.Message = this.Password = null;
            this.Banner = null;
            this.EncryptPassword = this.SaveChecked = false;
            this.Options = CredentialsDialogOptions.Default;
        }

        [DllImport("credui.dll", CharSet = CharSet.Unicode, EntryPoint = "CredUIConfirmCredentialsW")]
        internal static extern CredUIReturnCodes CredUIConfirmCredentials(string targetName, [MarshalAs(UnmanagedType.Bool)] bool confirm);

        [DllImport("credui.dll", CharSet = CharSet.Unicode, EntryPoint = "CredUIPromptForCredentialsW")]
        internal static extern CredUIReturnCodes CredUIPromptForCredentials(ref CREDUI_INFO creditUR,
            string targetName,
            IntPtr reserved1,
            int iError,
            StringBuilder userName,
            int maxUserName,
            StringBuilder password,
            int maxPassword,
            [MarshalAs(UnmanagedType.Bool)] ref bool pfSave,
            CredentialsDialogOptions flags);

        [DllImport("gdi32.dll")]
        internal static extern bool DeleteObject(IntPtr hObject);

        /// <summary>
        /// When overridden in a derived class, specifies a common dialog box.
        /// </summary>
        /// <param name="parentWindowHandle">A value that represents the window handle of the owner window for the common dialog box.</param>
        /// <returns>
        /// true if the dialog box was successfully run; otherwise, false.
        /// </returns>
        protected override bool RunDialog(IntPtr parentWindowHandle)
        {
            CREDUI_INFO info = new CREDUI_INFO(parentWindowHandle, this.Caption, this.Message, this.Banner);
            try
            {

                StringBuilder userName = new StringBuilder(this.UserName, maxStringLength);
                StringBuilder password = new StringBuilder(maxStringLength);
                bool save = this.SaveChecked;

                if (string.IsNullOrEmpty(this.Target)) this.Target = this.DefaultTarget;
                CredUIReturnCodes ret = CredUIPromptForCredentials(ref info, this.Target, IntPtr.Zero,
                    this.AuthenticationError, userName, maxStringLength, password, maxStringLength, ref save, this.Options);
                switch (ret)
                {
                    case CredUIReturnCodes.NO_ERROR:
                        /*if (save)
                        {
                            CredUIReturnCodes cret = CredUIConfirmCredentials(this.Target, false);
                            if (cret != CredUIReturnCodes.NO_ERROR && cret != CredUIReturnCodes.ERROR_INVALID_PARAMETER)
                            {
                                this.Options |= CredentialsDialogOptions.IncorrectPassword;
                                return false;
                            }
                        }*/
                        break;
                    case CredUIReturnCodes.ERROR_CANCELLED:
                        return false;
                    default:
                        throw new InvalidOperationException(String.Format("Call to CredUIPromptForCredentials failed with error code: {0}", ret));
                }

                if (this.EncryptPassword)
                {
                    // Convert the password to a SecureString
                    SecureString newPassword = StringBuilderToSecureString(password);

                    // Clear the old password and set the new one (read-only)
                    if (this.SecurePassword != null)
                        this.SecurePassword.Dispose();
                    newPassword.MakeReadOnly();
                    this.SecurePassword = newPassword;
                }
                else
                    this.Password = password.ToString();

                // Update other properties
                this.UserName = userName.ToString();
                this.SaveChecked = save;
                return true;
            }
            finally
            {
                info.Dispose();
            }
        }

        private static SecureString StringBuilderToSecureString(StringBuilder password)
        {
            // Copy the password into the secure string, zeroing the original buffer as we go
            SecureString newPassword = new SecureString();
            for (int i = 0; i < password.Length; i++)
            {
                newPassword.AppendChar(password[i]);
                password[i] = '\0';
            }
            return newPassword;
        }

        //[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct CREDUI_INFO
        {
            public int cbSize;
            public IntPtr hwndParent;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszMessageText;
            [MarshalAs(UnmanagedType.LPWStr)]
            public string pszCaptionText;
            public IntPtr hbmBanner;

            public CREDUI_INFO(IntPtr hwndOwner, string caption, string message, Bitmap banner)
            {
                cbSize = Marshal.SizeOf(typeof(CREDUI_INFO));
                hwndParent = hwndOwner;
                pszCaptionText = caption;
                pszMessageText = message;
                hbmBanner = banner != null ? banner.GetHbitmap() : IntPtr.Zero;
            }

            public void Dispose()
            {
                if (hbmBanner != IntPtr.Zero)
                    DeleteObject(hbmBanner);
            }
        }
    }

    /// <summary>
    /// Options for the display of the <see cref="CredentialsDialog"/> and its functionality.
    /// </summary>
    [Flags]
    public enum CredentialsDialogOptions
    {
        /// <summary>
        /// Default flags settings These are the following values:
        /// <see cref="GenericCredentials"/>, <see cref="AlwaysShowUI"/> and <see cref="ExpectConfirmation"/>
        /// </summary>
        Default = GenericCredentials | AlwaysShowUI | ExpectConfirmation,
        /// <summary>No options are set.</summary>
        None = 0,
        /// <summary>Notify the user of insufficient credentials by displaying the "Logon unsuccessful" balloon tip.</summary>
        IncorrectPassword = 0x1,
        /// <summary>Do not store credentials or display check boxes. You can pass ShowSaveCheckBox with this flag to display the Save check box only, and the result is returned in the <see cref="CredentialsDialog.SaveChecked"/> property.</summary>
        DoNotPersist = 0x2,
        /// <summary>Populate the combo box with local administrators only.</summary>
        RequestAdministrator = 0x4,
        /// <summary>Populate the combo box with user name/password only. Do not display certificates or smart cards in the combo box.</summary>
        ExcludeCertificates = 0x8,
        /// <summary>Populate the combo box with certificates and smart cards only. Do not allow a user name to be entered.</summary>
        RequireCertificate = 0x10,
        /// <summary>If the check box is selected, show the Save check box and return <c>true</c> in the <see cref="CredentialsDialog.SaveChecked"/> property, otherwise, return <c>false</c>. Check box uses the value in the <see cref="CredentialsDialog.SaveChecked"/> property by default.</summary>
        ShowSaveCheckBox = 0x40,
        /// <summary>Specifies that a user interface will be shown even if the credentials can be returned from an existing credential in credential manager. This flag is permitted only if GenericCredentials is also specified.</summary>
        AlwaysShowUI = 0x80,
        /// <summary>Populate the combo box with certificates or smart cards only. Do not allow a user name to be entered.</summary>
        RequireSmartcard = 0x100,
        /// <summary></summary>
        PasswordOnlyOk = 0x200,
        /// <summary></summary>
        ValidateUsername = 0x400,
        /// <summary></summary>
        CompleteUsername = 0x800,
        /// <summary>Do not show the Save check box, but the credential is saved as though the box were shown and selected.</summary>
        Persist = 0x1000,
        /// <summary>This flag is meaningful only in locating a matching credential to prefill the dialog box, should authentication fail. When this flag is specified, wildcard credentials will not be matched. It has no effect when writing a credential. CredUI does not create credentials that contain wildcard characters. Any found were either created explicitly by the user or created programmatically, as happens when a RAS connection is made.</summary>
        ServerCredential = 0x4000,
        /// <summary>Specifies that the caller will call ConfirmCredentials after checking to determine whether the returned credentials are actually valid. This mechanism ensures that credentials that are not valid are not saved to the credential manager. Specify this flag in all cases unless DoNotPersist is specified.</summary>
        ExpectConfirmation = 0x20000,
        /// <summary>Consider the credentials entered by the user to be generic credentials, rather than windows credentials.</summary>
        GenericCredentials = 0x40000,
        /// <summary>The credential is a "runas" credential. The TargetName parameter specifies the name of the command or program being run. It is used for prompting purposes only.</summary>
        UsernameTargetCredentials = 0x80000,
        /// <summary></summary>
        KeepUsername = 0x100000,
    }
}