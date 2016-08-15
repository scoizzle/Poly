﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Reflection;
using System.Diagnostics;

using Poly.Data;

namespace Poly {
    public partial class App {
        public static bool Running;
		public static jsObject GlobalContext;
		public static Event.Engine Commands;

        public static readonly string NewLine = "\r\n";

        static App() {
            Running = false;
            GlobalContext = new jsObject();
            Commands = new Poly.Event.Engine();
        }

        public static string Query(string Question) {
            Console.Write(Question);
            return Console.ReadLine();
        }

        public static void Init(int LogLevel = Log.Levels.None) {
            if (LogLevel != Log.Levels.None) {
                Log.Active = true;
                Log.Level = LogLevel;
            }

			App.Commands.Add("--log-level={Level}", Event.Wrapper((int Level) => {
                Log.Level = Level;
					return Level;
				})
			);
            
            Log.Info("Application initializing...");

			Running = true;
            Log.Info("Application running...");
		}

        public static void Init(int LogLevel = Log.Levels.None, params string[] Commands) {
            Init(LogLevel);

            foreach (var Cmd in Commands) {
                App.Commands.MatchAndInvoke(Cmd, GlobalContext);
            }
        }

        public static void WaitforExit() {
			while (Running) {
				var Line = Console.ReadLine();

				if (Line == null)
					Thread.Sleep(500);
				else
					Commands.MatchAndInvoke (Line, GlobalContext);
				
				Thread.Sleep (50);
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
