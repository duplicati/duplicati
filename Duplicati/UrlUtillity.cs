using System;
using System.Collections.Generic;
using System.Text;
using System.Windows.Forms;

namespace Duplicati
{
    public static class UrlUtillity
    {
        public static string SystemBrowser = null;
        /// <summary>
        /// Opens the given URL in a browser
        /// </summary>
        public static void OpenUrl(string url)
        {
            try
            {
                if (!url.StartsWith("http://") && !url.StartsWith("https://"))
                    throw new Exception("Malformed URL");

                if (string.IsNullOrEmpty(SystemBrowser))
                {
                    try
                    {
                        System.Diagnostics.Process process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = url;
                        process.StartInfo.UseShellExecute = true;
                        process.Start();
                    }
                    catch
                    {
                        //The straightforward method gives an error: "The requested lookup key was not found in any active activation context"
                        System.Diagnostics.Process process = new System.Diagnostics.Process();
                        process.StartInfo.FileName = "rundll32.exe";
                        process.StartInfo.Arguments = "url.dll,FileProtocolHandler " + url;
                        process.StartInfo.UseShellExecute = true;
                        process.Start();
                    }
                }
                else
                {
                    System.Diagnostics.Process process = new System.Diagnostics.Process();
                    process.StartInfo.FileName = SystemBrowser;
                    process.StartInfo.Arguments = url;
                    process.StartInfo.UseShellExecute = true;
                    process.Start();
                }

            }
            catch (Exception ex)
            {
                string s = ex.Message;
                MessageBox.Show(string.Format("Unable to open a browser window, please manually visit: \r\n{0}", url), Application.ProductName, MessageBoxButtons.OK, MessageBoxIcon.Error);
            }

        }
    }
}
