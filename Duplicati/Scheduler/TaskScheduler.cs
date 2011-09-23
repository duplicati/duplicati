#region Disclaimer / License
// Copyright (C) 2011, Kenneth Bergeron, IAP Worldwide Services, Inc
// NOAA :: National Marine Fisheries Service
// 
// This library is free software; you can redistribute it and/or
// modify it under the terms of the GNU Lesser General Public
// License as published by the Free Software Foundation; either
// version 2.1 of the License, or (at your option) any later version.
// 
// This library is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the GNU
// Lesser General Public License for more details.
// 
// You should have received a copy of the GNU Lesser General Public
// License along with this library; if not, write to the Free Software
// Foundation, Inc., 51 Franklin Street, Fifth Floor, Boston, MA  02110-1301  USA
// 
#endregion
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Duplicati.Scheduler
{
    /// <summary>
    /// Interface to the Task Scheduler
    /// </summary>
    public static class TaskScheduler
    {
        /// <summary>
        /// Does a taskname exist?
        /// </summary>
        /// <param name="aName">Task Name</param>
        /// <returns>True if existing</returns>
        public static bool Exists(string aName)
        {
            bool Result = false;
            // Get the service on the local machine
            using (Microsoft.Win32.TaskScheduler.TaskService ts = new Microsoft.Win32.TaskScheduler.TaskService())
                Result = ts.GetTask(aName) != null;
            return Result;
        }
        /// <summary>
        /// Returns a trigger given Task Name
        /// </summary>
        /// <param name="aName">Task Name</param>
        /// <returns>Trigger or null</returns>
        public static Microsoft.Win32.TaskScheduler.Trigger GetTrigger(string aName)
        {
            Microsoft.Win32.TaskScheduler.Trigger Result = null;
            using (Microsoft.Win32.TaskScheduler.TaskService ts = new Microsoft.Win32.TaskScheduler.TaskService())
            using( Microsoft.Win32.TaskScheduler.Task Task =  ts.GetTask(aName) )
            {
                if (Task != null)
                {
                    if (Task.Definition.Triggers.Count > 0)
                        Result = Task.Definition.Triggers[0]; // We only use one trigger...
                }
            }
            return Result;
        }
        /// <summary>
        /// Set a trigger
        /// </summary>
        /// <param name="aName">Task Name</param>
        /// <param name="aTrigger">Trigger to set</param>
        /// <returns>True if all OK</returns>
        public static bool SetTrigger(string aName, Microsoft.Win32.TaskScheduler.Trigger aTrigger)
        {
            bool Result = false;
            using (Microsoft.Win32.TaskScheduler.TaskService ts = new Microsoft.Win32.TaskScheduler.TaskService())
            using (Microsoft.Win32.TaskScheduler.Task Task = ts.GetTask(aName))
            {
                if (Task != null)
                {
                    if (Task.Definition.Triggers.Count > 0)
                        Task.Definition.Triggers[0] = aTrigger; // We only use one trigger...
                    else 
                        Task.Definition.Triggers.Add(aTrigger);
                    Result = true;
                }
            }
            return Result;
        }
        /// <summary>
        /// Updates a task, or creates it if not existing
        /// </summary>
        /// <param name="aName">Task Name</param>
        /// <param name="aTask">Action to run</param>
        /// <param name="aDescription">Plain text</param>
        /// <param name="aUserName">Domain/User</param>
        /// <param name="aPassword">Secure password</param>
        /// <param name="aTrigger">Trigger to use</param>
        /// <param name="aArgument">Arguments to Action</param>
        public static void CreateOrUpdateTask(string aName, string aTask, string aDescription, string aUserName, 
            System.Security.SecureString aPassword,
            Microsoft.Win32.TaskScheduler.Trigger aTrigger, string aArgument)
        {
            // Get the service on the local machine
            using (Microsoft.Win32.TaskScheduler.TaskService ts = new Microsoft.Win32.TaskScheduler.TaskService())
            {
                // Create a new task definition and assign properties
                Microsoft.Win32.TaskScheduler.TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = aDescription;
                td.RegistrationInfo.Author = "Duplicati Scheduler";

                // Create a trigger that will fire the task 
                td.Triggers.Add(aTrigger);
                td.Settings.WakeToRun = true;

                // Create an action that will launch the task whenever the trigger fires
                td.Actions.Add(new Microsoft.Win32.TaskScheduler.ExecAction(aTask, aArgument, null));
                if (ts.HighestSupportedVersion > new Version(1, 2))
                {
                    td.Principal.LogonType = Microsoft.Win32.TaskScheduler.TaskLogonType.Password;
                    td.Principal.RunLevel = Microsoft.Win32.TaskScheduler.TaskRunLevel.LUA;
                }

                // Register the task in the root folder
                string contents = null;
                if (aPassword != null)
                {
                    IntPtr ptr = System.Runtime.InteropServices.Marshal.SecureStringToBSTR(aPassword);
                    contents = System.Runtime.InteropServices.Marshal.PtrToStringAuto(ptr);
                }
                ts.RootFolder.RegisterTaskDefinition(aName, td, Microsoft.Win32.TaskScheduler.TaskCreation.CreateOrUpdate,
                    aUserName, contents, Microsoft.Win32.TaskScheduler.TaskLogonType.Password, null);
            }
        }
        /// <summary>
        /// Add a startup task (not used)
        /// </summary>
        /// <param name="aName">Task Name</param>
        /// <param name="aTask">Action to run</param>
        /// <param name="aDescription">Plain text</param>
        /// <param name="aUserName">Domain/User</param>
        /// <param name="aPassword">Secure password</param>
        public static void AddStartup(string aName, string aTask, string aDescription, string aUserName, System.Security.SecureString aPassword)
        {
            // Get the service on the local machine
            using (Microsoft.Win32.TaskScheduler.TaskService ts = new Microsoft.Win32.TaskScheduler.TaskService())
            {
                // Create a new task definition and assign properties
                Microsoft.Win32.TaskScheduler.TaskDefinition td = ts.NewTask();
                td.RegistrationInfo.Description = aDescription;

                // Create a trigger that will fire the task at boot
                td.Triggers.Add(new Microsoft.Win32.TaskScheduler.BootTrigger());

                // Create an action that will launch the task whenever the trigger fires
                td.Actions.Add(new Microsoft.Win32.TaskScheduler.ExecAction(aTask, null, null));
                td.Principal.LogonType = Microsoft.Win32.TaskScheduler.TaskLogonType.Password;
                td.Principal.RunLevel = Microsoft.Win32.TaskScheduler.TaskRunLevel.Highest;

                // Register the task in the root folder
                ts.RootFolder.RegisterTaskDefinition(aName, td, Microsoft.Win32.TaskScheduler.TaskCreation.Create, aUserName,
                    System.Runtime.InteropServices.Marshal.PtrToStringAuto(System.Runtime.InteropServices.Marshal.SecureStringToBSTR(aPassword)), 
                    Microsoft.Win32.TaskScheduler.TaskLogonType.Password, null);
            }
        }
        /// <summary>
        /// Put up the official credentials dialog
        /// </summary>
        /// <param name="aParentForm">Mom</param>
        /// <param name="aTaskSchedulerName">Task name</param>
        /// <param name="aCredentialPromptMessage">What to say</param>
        /// <param name="aUserName">User name to show</param>
        /// <param name="outPass">Entered password</param>
        /// <returns>Dialog Result</returns>
        public static System.Windows.Forms.DialogResult InvokeCredentialDialog(System.Windows.Forms.Form aParentForm,
            string aTaskSchedulerName, string aCredentialPromptMessage, string aUserName, out System.Security.SecureString outPass)
        {
            outPass = null;
            System.Windows.Forms.DialogResult Result = System.Windows.Forms.DialogResult.None;
            using (Microsoft.Win32.TaskScheduler.CredentialsDialog dlg = new Microsoft.Win32.TaskScheduler.CredentialsDialog(
                aTaskSchedulerName, aCredentialPromptMessage, aUserName))
            {
                dlg.Options |= Microsoft.Win32.TaskScheduler.CredentialsDialogOptions.Persist;
                dlg.EncryptPassword = true;
                Result = dlg.ShowDialog(aParentForm);
                outPass = dlg.SecurePassword;
            }
            return Result;
        }
        /// <summary>
        /// Returns if enabled
        /// </summary>
        /// <param name="aName">Task Name</param>
        /// <returns>true if enabled</returns>
        public static bool Enabled(string aName)
        {
            Microsoft.Win32.TaskScheduler.Trigger t = GetTrigger(aName);
            return t!=null && t.Enabled;
        }
        /// <summary>
        /// Return a text description of the trigger
        /// </summary>
        /// <param name="aName">Task name</param>
        /// <returns>text description of the trigger</returns>
        public static string Describe(string aName)
        {
            return Describe(GetTrigger(aName));
        }
        /// <summary>
        /// Return a text description of the trigger
        /// </summary>
        /// <param name="aTrigger">Trigger</param>
        /// <returns>text description of the trigger</returns>
        public static string Describe(Microsoft.Win32.TaskScheduler.Trigger aTrigger)
        {
            string Result = string.Empty;
            if (aTrigger == null)
                Result = "<null>";
            else if (aTrigger is Microsoft.Win32.TaskScheduler.TimeTrigger)
                Result = ((Microsoft.Win32.TaskScheduler.TimeTrigger)aTrigger).ToString();
            else if (aTrigger is Microsoft.Win32.TaskScheduler.DailyTrigger)
                Result = ((Microsoft.Win32.TaskScheduler.DailyTrigger)aTrigger).ToString();
            else if (aTrigger is Microsoft.Win32.TaskScheduler.WeeklyTrigger)
                Result = ((Microsoft.Win32.TaskScheduler.WeeklyTrigger)aTrigger).ToString();
            else if (aTrigger is Microsoft.Win32.TaskScheduler.MonthlyTrigger)
                Result = ((Microsoft.Win32.TaskScheduler.MonthlyTrigger)aTrigger).ToString();
            else if (aTrigger is Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger)
                Result = ((Microsoft.Win32.TaskScheduler.MonthlyDOWTrigger)aTrigger).ToString();
            return Result.Replace("each every", "each"); // fix grammer L:)
        }
    }
}
