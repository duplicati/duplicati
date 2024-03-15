
using Duplicati.Server;
using System;
using System.Collections.Generic;

namespace Duplicati.Library.RestAPI
{
    /**
     * In the absense of dependancy injection, there is a significant amount of variables exposed through Program as globals.
     * This causes a problem decoupling classes and leads to circular dependancies.
     */
    public static class FIXMEGlobal
    {

        /// <summary>
        /// This is the only access to the database
        /// </summary>
        public static Server.Database.Connection DataConnection;

        /// <summary>
        /// The controller interface for pause/resume and throttle options
        /// </summary>
        public static LiveControls LiveControl;

        /// <summary>
        /// A delegate method for creating a copy of the current progress state
        /// </summary>
        public static Func<Duplicati.Server.Serialization.Interface.IProgressEventData> GenerateProgressState;

        /// <summary>
        /// The status event signaler, used to control long polling of status updates
        /// </summary>
        public static readonly EventPollNotify StatusEventNotifyer = new EventPollNotify();

        /// <summary>
        /// This is the working thread
        /// </summary>
        public static Duplicati.Library.Utility.WorkerThread<Runner.IRunnerData> WorkThread;

        public static Func<long> PeekLastDataUpdateID;
        public static Func<long> PeekLastNotificationUpdateID;

        public static Action IncrementLastDataUpdateID;

        public static Action IncrementLastNotificationUpdateID;

        public static Action StartOrStopUsageReporter;

        public static Action UpdateThrottleSpeeds;

        /// <summary>
        /// Gets the folder where Duplicati data is stored
        /// </summary>
        public static string DataFolder;

        /// <summary>
        /// This is the scheduling thread
        /// </summary>
        public static Scheduler Scheduler;

        /// <summary>
        /// The log redirect handler
        /// </summary>
        public static readonly LogWriteHandler LogHandler = new LogWriteHandler();

        public static Func<Dictionary<string, string>, Server.Database.Connection> GetDatabaseConnection;

        /// <summary>
        /// The update poll thread.
        /// </summary>
        public static UpdatePollThread UpdatePoller;


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
    }
}
