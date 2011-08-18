
using System;
using System.Runtime.InteropServices;
using System.Security;
using System.Windows.Forms;

namespace Duplicati.Scheduler.Utility
{
    public partial class SecureTextBox : System.Windows.Forms.TextBox
    {
        private System.Security.SecureString itsValue = new SecureString();
        public System.Security.SecureString Value
        {
            get
            {
                System.Security.SecureString Result = itsValue.Copy();
                Result.MakeReadOnly();
                return Result;
            }
            private set
            {
                itsValue = value.Copy();
                base.Text = new string('*', itsValue.Length);
            }
        }
        public byte[] ProtectedValue
        {
            get
            {
                return Duplicati.Scheduler.Utility.Tools.SecureProtect(itsValue);
            }
            private set
            {
                itsValue = Duplicati.Scheduler.Utility.Tools.SecureUnprotect(value);
                base.Text = new string('*', itsValue.Length);
            }
        }
        public new bool UseSystemPasswordChar { get { return true; } set { } }
        public new string Text { get { return string.Empty; } set { } }
        public new bool AcceptsTab { get { return false; } }
        public new bool ShortcutsEnabled { get { return false; } set { } }
        public new ContextMenuStrip ContextMenuStrip { get { return null; } }
        public new bool ReadOnly
        {
            get { return base.ReadOnly; }
            set
            {
                base.ReadOnly = value;
                if (value) base.Text = new string('*', 142);
                else base.Text = string.Empty;
                this.Focus();
            }
        }
        private volatile bool IgnoreThis = false;
        public SecureTextBox()
        {
            InitializeComponent();
            base.UseSystemPasswordChar = true;
            base.AcceptsTab = false;
            base.ShortcutsEnabled = false;
            base.ContextMenuStrip = new ContextMenuStrip();
        }
        public void dispose()
        {
            itsValue.Clear();
            itsValue.Dispose();
        }
#if DEBUG
        public static void Spew(System.Security.SecureString aS)
        {
            IntPtr ptr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(aS);
            string contents = System.Runtime.InteropServices.Marshal.PtrToStringAuto(ptr);
            Console.WriteLine(contents);
        }
#endif
        protected override void OnTextChanged(EventArgs e)
        {
            // base.OnTextChanged(e);
            if (!IgnoreThis)
            {
                // OK, user is trying to use the arrow keys or something...
                if (itsValue.Length == base.Text.Length)
                {
                    System.Media.SystemSounds.Exclamation.Play();
                    return;
                }
                while (itsValue.Length > base.Text.Length)
                    itsValue.RemoveAt(itsValue.Length - 1);
                for (int ix = itsValue.Length; ix < base.Text.Length; ix++)
                    itsValue.AppendChar(base.Text[ix]);
                IgnoreThis = true;
                base.Text = new string('*', base.Text.Length);
                this.SelectionStart = base.Text.Length;
            }
            IgnoreThis = false;
        }
    }
}
