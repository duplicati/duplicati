using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Drawing;
using System.Data;
using System.Text;
using System.Windows.Forms;

namespace Duplicati.Winforms.Controls
{
    /// <summary>
    /// This class implements the logic for a control that displays a hidden password.
    /// It allows the user to toggle the password visibility, to prevent typing errors,
    /// and can also prevent the underlying password from being revealed, while still
    /// allowing the user to enter a new password.
    /// </summary>
    public partial class PasswordControl : UserControl
    {
        /// <summary>
        /// The internal password storage
        /// </summary>
        private string m_password = "";

        /// <summary>
        /// A control field used to distinguish the code generated events from the user generated events
        /// </summary>
        private bool m_isUpdating = false;

        /// <summary>
        /// A variable that controls if the user is asked to clear the password
        /// </summary>
        private bool m_askToEnterNewPassword = false;

        /// <summary>
        /// An event that is raised when the password changed
        /// </summary>
        [Browsable(true)]
        public new event EventHandler TextChanged;

        /// <summary>
        /// Simple delegate with no arguments, used to invoke the update function
        /// </summary>
        private delegate void EmptyDelegate();

        /// <summary>
        /// Constructs a new PasswordControl
        /// </summary>
        public PasswordControl()
        {
            InitializeComponent();
        }

        /// <summary>
        /// The password that this control protects
        /// </summary>
        public override string Text
        {
            //Mono does not play nice if Text returns null
            get { return m_password ?? ""; }
            set
            {
                if (value == null)
                    value = "";

                bool changed = m_password != value;
                m_password = value;
                UpdateDisplay();

                if (changed && TextChanged != null)
                    TextChanged(this, new EventArgs());
            }
        }

        /// <summary>
        /// A value that determines if the user can only clear the password,
        /// and not reveal the original password
        /// </summary>
        public bool AskToEnterNewPassword
        {
            get { return m_askToEnterNewPassword; }
            set 
            { 
                m_askToEnterNewPassword = value;
                UpdateDisplay();
            }
        }

        /// <summary>
        /// Gets or sets a value that indicates if the password is visible to the user
        /// </summary>
        public bool IsPasswordVisible
        {
            get { return ShowPassword.Checked; }
            set { ShowPassword.Checked = value; }
        }

        /// <summary>
        /// Internal helper function that updates the user interface
        /// </summary>
        private void UpdateDisplay()
        {
            try
            {
                m_isUpdating = true;

                TextBox.UseSystemPasswordChar = !ShowPassword.Checked;
                if (TextBox.Focused)
                    TextBox.Text = m_askToEnterNewPassword ? "" : m_password;
                else
                    TextBox.Text = m_askToEnterNewPassword ? "password" : m_password;

                TextBox.SelectionStart = TextBox.Text.Length;
                TextBox.SelectionLength = 0;
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        /// <summary>
        /// Event handler for the TextChanged event
        /// </summary>
        /// <param name="sender">Unused sender argument</param>
        /// <param name="e">Unused event argument</param>
        private void TextBox_TextChanged(object sender, EventArgs e)
        {
            if (m_isUpdating)
                return;

            if (m_askToEnterNewPassword)
            {
                if (MessageBox.Show(this, Strings.PasswordControl.ConfirmClearingPassword, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
                {
                    try
                    {
                        m_isUpdating = true;
                        TextBox.Text = "";

                        BeginInvoke(new EmptyDelegate(OutOfOrderFocus));

                        return;
                    }
                    finally
                    {
                        m_isUpdating = false;
                    }
                }
            }

            m_password = TextBox.Text;
            m_askToEnterNewPassword = false;
            if (TextChanged != null)
                TextChanged(this, e);
        }

        /// <summary>
        /// A helper for providing out-of-order focusing
        /// </summary>
        private void OutOfOrderFocus()
        {
            try { ShowPassword.Focus(); }
            catch { }
        }

        /// <summary>
        /// Event handler for the CheckedChanged event
        /// </summary>
        /// <param name="sender">Unused sender argument</param>
        /// <param name="e">Unused event argument</param>
        private void ShowPassword_CheckedChanged(object sender, EventArgs e)
        {
            if (ShowPassword.Checked && m_askToEnterNewPassword)
            {
                if (MessageBox.Show(this, Strings.PasswordControl.ConfirmClearingPassword, Application.ProductName, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question, MessageBoxDefaultButton.Button1) != DialogResult.Yes)
                {
                    ShowPassword.Checked = false;
                    BeginInvoke(new EmptyDelegate(OutOfOrderFocus));
                    return;
                }
                else
                {
                    m_password = "";
                    m_askToEnterNewPassword = false;
                }
            }

            UpdateDisplay();
        }

        /// <summary>
        /// Event handler for the Enter event
        /// </summary>
        /// <param name="sender">Unused sender argument</param>
        /// <param name="e">Unused event argument</param>
        private void TextBox_Enter(object sender, EventArgs e)
        {
            this.BeginInvoke(new EmptyDelegate(UpdateDisplay));
        }

        /// <summary>
        /// Event handler for the Leave event
        /// </summary>
        /// <param name="sender">Unused sender argument</param>
        /// <param name="e">Unused event argument</param>
        private void TextBox_Leave(object sender, EventArgs e)
        {
            this.BeginInvoke(new EmptyDelegate(UpdateDisplay));
        }

        /// <summary>
        /// Event handler for the checkbox click event
        /// </summary>
        /// <param name="sender">Unused sender argument</param>
        /// <param name="e">Unused event argument</param>
        private void ShowPassword_Click(object sender, EventArgs e)
        {
            try { TextBox.Focus(); }
            catch { }
        }
    }
}
