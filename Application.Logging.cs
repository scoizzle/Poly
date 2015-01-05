using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Diagnostics;

namespace Poly {
    public partial class App {
        public class Log {
            public static bool Active;
            public static int Level;

			public static Action<string> Handler;

            public class Levels {
                public const int None = -1;
                public const int Fatal = 0;
                public const int Error = 1;
                public const int Warning = 2;
                public const int Info = 3;
            };

			static Log() {
				Active = false;
				Level = Levels.Fatal;

				Handler = Console.Write;
			}

            public static void Info(string Message) {
                if (Level >= Levels.Info) {
                    Print("[INFO] ", Message);
                }
            }

            public static void Warning(string Message) {
                if (Level >= Levels.Warning) {
                    Print("[WARNING] ", Message);
                }
            }

            public static void Error(string Message) {
                if (Level >= Levels.Error) {
                    Print("[ERROR] ", Message);
                }
            }

            public static void Fatal(string Message) {
                if (Level >= Levels.Fatal) {
                    Print("[FATAL] ", Message);
                }
                App.Exit(-1);
            }

            public static void Print(params string[] Message) {
                if (!Active)
                    return;

                foreach (string part in Message) {
					Handler(part);
                }

				Handler(Environment.NewLine);
            }

            public static void Benchmark(string Name, int Iterations, Action Todo) {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Todo();
                
                var watch = Stopwatch.StartNew();
                for (int i = 0; i < Iterations; i++) {
                    Todo();
                }
                watch.Stop();
                Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec)", Name, watch.Elapsed.TotalMilliseconds,Iterations / watch.Elapsed.TotalSeconds);
            }

            public static void Benchmark(string Name, int Iterations, Event.Handler Todo) {
                Data.jsObject Obj = new Data.jsObject();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var watch = new Stopwatch();
                for (int i = 0; i < Iterations; i++) {
                    watch.Start();
                    Todo(Obj);
                    watch.Stop();
                    Obj.Clear();
                }
                Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec)", Name, watch.Elapsed.TotalMilliseconds, Iterations / watch.Elapsed.TotalSeconds);
            }
        }
    }
}