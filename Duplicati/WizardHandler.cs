using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;
using System.Windows.Forms.Wizard;
using System.Data.LightDatamodel;

namespace Duplicati
{
    /// <summary>
    /// This class encapsulates the control of the wizard
    /// </summary>
    public class WizardHandler
    {
        public enum Pages
        {
            MainAction,
            Add_SelectFiles,
            Add_SelectWhen,
            Add_Incremental,
            Add_Password,
            Add_SelectService,
            Add_FileOptions,
            Add_FTPOptions,
            Add_S3Options,
            Add_SSHOptions,
            Add_WebDAVOptions,
            Add_Finished,
        }

        IWizardForm m_form;
        IDataFetcher m_connection;

        public WizardHandler()
        {
            m_connection = new DataFetcherNested(Program.DataConnection);

            m_form = new Dialog();
            m_form.Title = "Duplicati Setup Wizard";

            m_form.Pages.Clear();
            m_form.Pages.AddRange(new IWizardControl[] {
                new Wizard_pages.MainPage(),
                new Wizard_pages.Add_backup.SelectFiles(),
                new Wizard_pages.Add_backup.SelectWhen(),
                new Wizard_pages.Add_backup.IncrementalSettings(),
                new Wizard_pages.Add_backup.PasswordSettings(),
                new Wizard_pages.SelectBackend(),
                new Wizard_pages.Backends.File.FileOptions(),
                new Wizard_pages.Backends.FTP.FTPOptions(),
                new Wizard_pages.Backends.S3.S3Options(),
                new Wizard_pages.Backends.SSH.SSHOptions(),
                new Wizard_pages.Backends.WebDAV.WebDAVOptions(),
            });

            m_form.DefaultImage = Program.NeutralIcon.ToBitmap();
            m_form.BackPressed += new PageChangeHandler(m_form_BackPressed);
            m_form.NextPressed += new PageChangeHandler(m_form_NextPressed);
        }

        void m_form_NextPressed(object sender, PageChangedArgs args)
        {
            switch ((Pages)m_form.Pages.IndexOf(m_form.CurrentPage))
            {
                case Pages.MainAction:
                    switch (((Wizard_pages.MainPage)m_form.CurrentPage).SelectedAction)
                    {
                        case Duplicati.Wizard_pages.MainPage.Action.Add:
                            args.NextPage = m_form.Pages[(int)Pages.Add_SelectFiles];

                            break;
                        /*case Duplicati.Wizard_pages.MainPage.Action.Edit:
                            break;
                        case Duplicati.Wizard_pages.MainPage.Action.Restore:
                            break;*/
                        default:
                            args.Cancel = true;
                            MessageBox.Show(m_form as Form, "Unknown option selected", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break;
                    }
                    break;
                case Pages.Add_SelectService:
                    switch (((Wizard_pages.SelectBackend)m_form.CurrentPage).SelectedProvider)
                    {
                        case Duplicati.Wizard_pages.SelectBackend.Provider.File:
                            args.NextPage = m_form.Pages[(int)Pages.Add_FileOptions];
                            break;
                        case Duplicati.Wizard_pages.SelectBackend.Provider.FTP:
                            args.NextPage = m_form.Pages[(int)Pages.Add_FTPOptions];
                            break;
                        case Duplicati.Wizard_pages.SelectBackend.Provider.SSH:
                            args.NextPage = m_form.Pages[(int)Pages.Add_SSHOptions];
                            break;
                        case Duplicati.Wizard_pages.SelectBackend.Provider.WebDAV:
                            args.NextPage = m_form.Pages[(int)Pages.Add_WebDAVOptions];
                            break;
                        case Duplicati.Wizard_pages.SelectBackend.Provider.S3:
                            args.NextPage = m_form.Pages[(int)Pages.Add_S3Options];
                            break;
                        default:
                            args.Cancel = true;
                            MessageBox.Show(m_form as Form, "Unknown option selected", Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
                            break;
                    }
                    break;

                case Pages.Add_FileOptions:
                case Pages.Add_FTPOptions:
                case Pages.Add_SSHOptions:
                case Pages.Add_WebDAVOptions:
                case Pages.Add_S3Options:
                    args.NextPage = m_form.Pages[(int)Pages.Add_Finished];
                    break;

                case Pages.Add_Finished:
                    SetupFinishPage(m_form.Pages[(int)Pages.Add_Finished] as Wizard_pages.Add_backup.FinishedAdd);
                    break;


            }
        }

        void m_form_BackPressed(object sender, PageChangedArgs args)
        {
            switch ((Pages)m_form.Pages.IndexOf(m_form.CurrentPage))
            {
                case Pages.Add_FileOptions:
                case Pages.Add_FTPOptions:
                case Pages.Add_SSHOptions:
                case Pages.Add_WebDAVOptions:
                case Pages.Add_S3Options:
                    args.NextPage = m_form.Pages[(int)Pages.Add_SelectService];
                    break;
                case Pages.Add_SelectFiles:
                    args.NextPage = m_form.Pages[(int)Pages.MainAction];
                    break;
            }
        }

        public void Show()
        {
            (m_form as Form).ShowDialog();
        }

        private void SetupFinishPage(Wizard_pages.Add_backup.FinishedAdd page)
        {

        }
    }
}
