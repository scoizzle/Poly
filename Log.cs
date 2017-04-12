using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace Poly {
	public class Log {
		public enum Levels {
			None = -1,
			Fatal,
			Error,
			Warning,
			Info,
			Debug
		};

		public static bool Active;
		public static Levels Level;
		public static Action<string> Handler = Console.WriteLine;

		public static void Debug(object Message) {
			if (Level <= Levels.Debug) {
				Print("[DEBUG] ", Message);
			}
		}

		public static void Debug(string Format, params object[] Args) {
			Debug(string.Format(Format, Args) as object);
		}

		public static void Info(object Message) {
			if (Level <= Levels.Info) {
				Print("[INFO] ", Message);
			}
		}

		public static void Info(string Format, params object[] Args) {
			if (Args.Length == 0)
				Info(Format as object);
			else
				Info(string.Format(Format, Args) as object);
		}

		public static void Warning(object Message) {
			if (Level <= Levels.Warning) {
				Print("[WARNING] ", Message);
			}
		}

		public static void Warning(string Format, params object[] Args) {
			Warning(string.Format(Format, Args) as object);
		}

		public static void Error(object Message) {
			if (Level <= Levels.Error) {
				Print("[ERROR] ", Message);
			}
		}

		public static void Error(string Format, params object[] Args) {
			Error(string.Format(Format, Args) as object);
		}

		public static void Fatal(object Message) {
			if (Level <= Levels.Fatal) {
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

			Handler(string.Concat(Messages));
		}

		public static Task Benchmark(string Name, int Iterations, Action Todo) {
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Todo();

			return Task.Run(() => {
				var watch = new Stopwatch();

				int i = 0;
				for (; i < Iterations; i++) {
					watch.Start();
					Todo();
					watch.Stop();
				}

				Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec) ~ {3}ms / iteration", Name, watch.Elapsed.TotalMilliseconds, i / watch.Elapsed.TotalSeconds, watch.Elapsed.TotalMilliseconds / i);
			});
		}

		public static Task Benchmark(string Name, int Iterations, Action<int> Todo) {
			GC.Collect();
			GC.WaitForPendingFinalizers();
			GC.Collect();

			Todo(0);

			return Task.Factory.StartNew(() => {
				var watch = new Stopwatch();

				int i = 0;
				for (; i < Iterations; i++) {
					watch.Start();
					Todo(i);
					watch.Stop();
				}

				Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec) ~ {3}ms / iteration", Name, watch.Elapsed.TotalMilliseconds, i / watch.Elapsed.TotalSeconds, watch.Elapsed.TotalMilliseconds / i);
			});
		}
	}
}