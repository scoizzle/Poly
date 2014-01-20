using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;


namespace Poly {
    public delegate object Handler(Data.jsObject Args);

    public partial class App {
		public static bool Running = false;
        public static Logging Log = new Logging();

		public static void Init(int LogLevel = Logging.Levels.None) {
            if (LogLevel != Logging.Levels.None) {
                App.Log.Active = true;
                App.Log.Level = LogLevel;
            }

            App.Log.Info("Application initializing...");
            
            int workerThreads, completionThreads;

            ThreadPool.GetMaxThreads(out workerThreads, out completionThreads);
            ThreadPool.SetMaxThreads(workerThreads, completionThreads * 16);

			Running = true;
            App.Log.Info("Application running...");
		}

		public static void Exit(int Status = 0) {
            Log.Info("Applcation Exiting...");

            Running = false;
			Thread.Sleep(1000);
			Environment.Exit(0);
		}
	}
}
