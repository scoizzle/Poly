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

        public static Event.Engine Commands = new Event.Engine() {
            { "--fork[-{flag}]", (Args) => {
                App.Fork("redirect".Compare(Args.Get<string>("flag"), true, 0));
                return null;
            }}
        };

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

        public static void Init(int LogLevel = Log.Levels.None, params string[] Commands) {
            Init(LogLevel);

            foreach (var Cmd in Commands) {
                App.Commands.MatchAndInvoke(Cmd, new jsObject(), true);
            }
        }

        public static void Fork(bool RedirectOutput = false) {
            var This = Process.GetCurrentProcess();

            List<string> Args = new List<string>(Environment.GetCommandLineArgs());

            Args.RemoveAt(0);
            Args.Remove("--fork");
            Args.Remove("--fork-redirect");

            string FileName = IsRunningOnMono() ? "mono" : This.MainModule.FileName;
            string Arguments = IsRunningOnMono() ? This.MainModule.FileName + " " + string.Join(" ", Args) : string.Join(" ", Args);

            if (RedirectOutput) {
                Process Worker = Process.Start(
                    new ProcessStartInfo(FileName, Arguments) {
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true
                    }
                );

                Worker.Start();

                ThreadPool.QueueUserWorkItem((obj) => {
                    Process P = obj as Process;
                    while (!P.HasExited && App.Running) {
                        Console.WriteLine(P.StandardOutput.ReadLine());
                    }
                }, Worker);
            }
            else {
                Process.Start(FileName, Arguments);
                Environment.Exit(0);
            }
        }
        public static bool IsRunningOnMono() {
            return Type.GetType("Mono.Runtime") != null;
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
