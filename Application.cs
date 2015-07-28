using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Diagnostics;

using Poly.Data;

namespace Poly {
    public partial class App {
        public static bool Running = false;
		public static jsObject GlobalContext = new jsObject();
        public static Event.Engine Commands = new Event.Engine();
        
		public static void Init(int LogLevel = Log.Levels.None) {
            if (LogLevel != Log.Levels.None) {
                App.Log.Active = true;
                App.Log.Level = LogLevel;
            }
            
            App.Log.Info("Application initializing...");

			Running = true;
            App.Log.Info("Application running...");
		}

        public static void Init(int LogLevel = Log.Levels.None, params string[] Commands) {
            Init(LogLevel);

            foreach (var Cmd in Commands) {
                App.Commands.MatchAndInvoke(Cmd, new jsObject(), true);
            }
        }

        public static bool IsRunningOnMono() {
            return Type.GetType("Mono.Runtime") != null;
        }

        public static void WaitforExit() {
			while (Running) {
				Commands.MatchAndInvoke (Console.ReadLine(), GlobalContext, true);
			}
        }

		public static void Exit(int Status = 0) {
            Log.Info("Applcation Exiting...");

            Running = false;
			Thread.Sleep(1000);
			Environment.Exit(0);
		}
	}
}
