using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Poly {
    using Data;

	public class Log {
		public enum Levels {
			Fatal,
			Error,
			Warning,
			Info,
			Debug
		};

		public static bool Active = true;
		public static Levels Level = Levels.Fatal;
		public static Action<string> Handler = Console.WriteLine;

        public static void Print(Levels level, params object[] messages) {
            if (Active && Level <= level) {
                var Output = new StringBuilder();

                Output.Append('[').Append(level.ToString().ToUpper()).Append("] ");

                foreach (var message in messages)
                    Output.Append(message);

                Handler(Output.ToString());
            }
        }


        public static void Print(Levels level, string format, params object[] args) {
            if (Active && Level <= level) {
                var Output = new StringBuilder();

                Output.Append('[').Append(level.ToString().ToUpper()).Append("] ");
                Output.AppendFormat(format, args);

                Handler(Output.ToString());
            }
        }


        public static void Debug(object message) {
            Print(Levels.Debug, message);
		}

		public static void Debug(string format, params object[] args) {
            Print(Levels.Debug, format, args);
        }

        public static void Info(object message) {
            Print(Levels.Info, message);
        }

        public static void Info(string format, params object[] args) {
            Print(Levels.Info, format, args);
        }

        public static void Warning(object message) {
            Print(Levels.Warning, message);
        }

        public static void Warning(string format, params object[] args) {
            Print(Levels.Warning, format, args);
        }

        public static void Error(object message) {
            Print(Levels.Error, message);
        }

        public static void Error(string format, params object[] args) {
            Print(Levels.Error, format, args);
        }

        public static void Fatal(object message) {
            Print(Levels.Fatal, message);
        }

        public static void Fatal(string format, params object[] args) {
            Print(Levels.Fatal, format, args);
        }

		public static void Benchmark(string Name, int Iterations, Action Todo) {
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Todo();
            
			var watch = new Stopwatch();

			int i = 0;
			for (; i < Iterations; i++) {
				watch.Start();
				Todo();
				watch.Stop();
			}

			Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec) ~ {3}ms / iteration", Name, watch.Elapsed.TotalMilliseconds, i / watch.Elapsed.TotalSeconds, watch.Elapsed.TotalMilliseconds / i);
		}

		public static void Benchmark(string Name, int Iterations, Action<int> Todo) {
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Todo(0);
            
			var watch = new Stopwatch();

			int i = 0;
			for (; i < Iterations; i++) {
				watch.Start();
				Todo(i);
				watch.Stop();
			}

			Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec) ~ {3}ms / iteration", Name, watch.Elapsed.TotalMilliseconds, i / watch.Elapsed.TotalSeconds, watch.Elapsed.TotalMilliseconds / i);
		}
	}
}