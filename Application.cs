using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
            Commands = new Event.Engine();
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
            var eng = new Script.Engine();

            while (Running) {
				var Line = Console.ReadLine();

                if (Line == null)
                    Task.Delay(500);
                else if (Commands.MatchAndInvoke(Line, GlobalContext)) continue;
                else {
                    if (eng.Parse(Line)) {
                        Console.WriteLine(eng.Evaluate(GlobalContext) ?? string.Empty);
                        eng = new Script.Engine();
                    }
                }

                Task.Delay(50);
            }
        }

		public static void Exit(int Status = 0) {
            Log.Info("Applcation Exiting...");

            Running = false;
            Task.Delay(1000);
            Environment.Exit(0);
		}
	}
}
