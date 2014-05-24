using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;


namespace Poly {
    public partial class App {
		public static bool Running = false;

		public static void Init(int LogLevel = Log.Levels.None) {
            if (LogLevel != Log.Levels.None) {
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

        public static void Wait() {
            Console.ReadKey();
        }

		public static void Exit(int Status = 0) {
            Log.Info("Applcation Exiting...");

            Running = false;
			Thread.Sleep(1000);
			Environment.Exit(0);
		}
	}
}
