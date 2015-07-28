using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
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

            public static void Info(object Message) {
                if (Level >= Levels.Info) {
                    Print("[INFO] ", Message);
                }
            }

            public static void Info(string Format, params object[] Args) {
                Info(string.Format(Format, Args) as object);
            }

            public static void Warning(object Message) {
                if (Level >= Levels.Warning) {
                    Print("[WARNING] ", Message);
                }
            }

            public static void Warning(string Format, params object[] Args) {
                Warning(string.Format(Format, Args) as object);
            }

            public static void Error(object Message) {
                if (Level >= Levels.Error) {
                    Print("[ERROR] ", Message);
                }
            }

            public static void Error(string Format, params object[] Args) {
                Error(string.Format(Format, Args) as object);
            }

            public static void Fatal(object Message) {
                if (Level >= Levels.Fatal) {
                    Print("[FATAL] ", Message);
                }
                App.Exit(-1);
            }

            public static void Fatal(string Format, params object[] Args) {
                Fatal(string.Format(Format, Args) as object);
            }

            public static void Print(params object[] Messages) {
                if (!Active)
                    return;

                foreach (string part in 
                    from obj in Messages
                    where obj != null
                    select obj.ToString()) 
                {
					Handler(part);
                }

				Handler(Environment.NewLine);
            }

            public static Task Benchmark(string Name, int Iterations, Action Todo) {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                Todo();

                return Task.Factory.StartNew(() => {
                    var watch = Stopwatch.StartNew();

                    int i = 0;
                    
                    try {
                        for (; i < Iterations; i++) {
                            Todo();
                        }
                    }
                    catch { }

                    watch.Stop(); 
                    Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec)", Name, watch.Elapsed.TotalMilliseconds, i / watch.Elapsed.TotalSeconds);
                });
            }

            public static Data.jsObject Benchmark(string Name, int Iterations, Event.Handler Todo) {
                Data.jsObject Obj = new Data.jsObject();

                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();

                var watch = new Stopwatch();
                for (int i = 0; i < Iterations; i++) {
                    watch.Start();
                    Todo(Obj);
                    watch.Stop();
                }
                Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec)", Name, watch.Elapsed.TotalMilliseconds, Iterations / watch.Elapsed.TotalSeconds);
                return Obj;
            }
        }
    }
}