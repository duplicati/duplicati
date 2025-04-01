// Copyright (C) 2025, The Duplicati Team
// https://duplicati.com, hello@duplicati.com
// 
// Permission is hereby granted, free of charge, to any person obtaining a 
// copy of this software and associated documentation files (the "Software"), 
// to deal in the Software without restriction, including without limitation 
// the rights to use, copy, modify, merge, publish, distribute, sublicense, 
// and/or sell copies of the Software, and to permit persons to whom the 
// Software is furnished to do so, subject to the following conditions:
// 
// The above copyright notice and this permission notice shall be included in 
// all copies or substantial portions of the Software.
// 
// THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS 
// OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, 
// FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE 
// AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER 
// LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING 
// FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER 
// DEALINGS IN THE SOFTWARE.

using Duplicati.Server;
using System;
using System.Collections.Generic;
using Duplicati.Library.RestAPI.Abstractions;
using Duplicati.Library.Utility;
using Duplicati.WebserverCore.Abstractions;
using Microsoft.Extensions.DependencyInjection;
using Duplicati.Library.Interface;

namespace Duplicati.Library.RestAPI
{
    /**
     * In the absense of dependancy injection, there is a significant amount of variables exposed through Program as globals.
     * This causes a problem decoupling classes and leads to circular dependancies.
     */
    public static class FIXMEGlobal
    {
        /// <summary>
        /// The placeholder for passwords in the UI
        /// </summary>
        public const string PASSWORD_PLACEHOLDER = "**********";

        public static IServiceProvider Provider { get; set; }

        /// <summary>
        /// This is the only access to the database
        /// </summary>
        public static Server.Database.Connection DataConnection;

        /// <summary>
        /// A delegate method for creating a copy of the current progress state
        /// </summary>
        public static Func<Duplicati.Server.Serialization.Interface.IProgressEventData> GenerateProgressState;

        /// <summary>
        /// The status event signaler, used to control long polling of status updates
        /// </summary>
        public static EventPollNotify StatusEventNotifyer => Provider.GetRequiredService<EventPollNotify>();

        /// <summary>
        /// For keeping and incrementing last last events Ids of db save and last notification
        /// </summary>
        public static INotificationUpdateService NotificationUpdateService => Provider.GetRequiredService<INotificationUpdateService>();
        /// <summary>
        /// Checks if the server has started and is listening for events
        /// </summary>
        public static bool IsServerStarted => Provider != null;

        /// <summary>
        /// This is the working thread
        /// </summary>
        public static WorkerThread<Runner.IRunnerData> WorkThread =>
            Provider.GetRequiredService<IWorkerThreadsManager>().WorkerThread;

        public static IWorkerThreadsManager WorkerThreadsManager =>
            Provider.GetRequiredService<IWorkerThreadsManager>();

        public static Action StartOrStopUsageReporter;

        /// <summary>
        /// Gets the folder where Duplicati data is stored
        /// </summary>
        public static string DataFolder;

        /// <summary>
        /// This is the scheduling thread
        /// </summary>
        public static IScheduler Scheduler => Provider.GetRequiredService<IScheduler>();

        /// <summary>
        /// The log redirect handler
        /// </summary>
        public static readonly LogWriteHandler LogHandler = new LogWriteHandler();

        /// <summary>
        /// The update poll thread.
        /// </summary>
        public static UpdatePollThread UpdatePoller => Provider.GetRequiredService<UpdatePollThread>();


        /// <summary>
        /// Used to check the origin of the web server (e.g. Tray icon or a stand alone Server)
        /// </summary>
        public static string Origin = "Server";


        /// <summary>
        /// The application exit event
        /// </summary>
        public static System.Threading.ManualResetEvent ApplicationExitEvent;


        /// <summary>
        /// List of completed task results
        /// </summary>
        public static readonly List<KeyValuePair<long, Exception>> TaskResultCache = new List<KeyValuePair<long, Exception>>();


        /// <summary>
        /// This is the lock to be used before manipulating the shared resources
        /// </summary>
        public static readonly object MainLock = new object();

        /// <summary>
        /// The shared secret provider from the server invocation
        /// </summary>
        public static ISecretProvider SecretProvider { get; set; }
        /// <summary>
        /// Flag to indicate if the settings encryption key was provided externally
        /// </summary>
        public static bool SettingsEncryptionKeyProvidedExternally { get; set; }

    }
}
