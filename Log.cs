using System;
using System.Diagnostics;
using System.IO;

namespace Poly {

    public class Log {
        public static Action<string> Write = (str) => { };

        public static void ToConsole() =>
            Write = str => { Console.Write(str); };

        public static void ToFile(string file_name) =>
            Write = str => { File.AppendAllText(file_name, str); };

        private static void Print(string level, DateTime time, string message) =>
            Write(
                string.Join(
                    " ",
                    time.ToString("yyyy-MM-dd hh:mm:ss"),
                    level,
                    message,
                    App.NewLine
            ));

        private static void Print(string level, DateTime time, string format, params object[] args) =>
            Write(
                string.Join(
                    " ",
                    time.ToString("yyyy-MM-dd hh:mm:ss"),
                    level,
                    string.Format(format, args),
                    App.NewLine
            ));

        [Conditional("DEBUG")]
        public static void Debug(string format, params object[] args) =>
            Print("DEBUG", DateTime.UtcNow, format, args);

        [Conditional("DEBUG")]
        public static void Debug(Exception error) =>
            Print("DEBUG", DateTime.UtcNow, error.Message);

        public static void Info(string format, params object[] args) =>
            Print("INFO ", DateTime.UtcNow, format, args);

        public static void Info(Exception error) =>
            Print("INFO ", DateTime.UtcNow, error.Message);

        public static void Warning(string format, params object[] args) =>
            Print("WARN ", DateTime.UtcNow, format, args);

        public static void Warning(Exception error) =>
            Print("WARN ", DateTime.UtcNow, error.Message);

        public static void Error(string format, params object[] args) =>
            Print("ERROR", DateTime.UtcNow, format, args);

        public static void Error(Exception error) =>
            Print("ERROR", DateTime.UtcNow, error.Message);

        public static void Fatal(string format, params object[] args) =>
            Print("FATAL", DateTime.UtcNow, format, args);

        public static void Fatal(Exception error) =>
            Print("FATAL", DateTime.UtcNow, error.Message);

        [Conditional("RELEASE")]
        public static void Benchmark(string name, System.Action action) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            action();

            var watch = new Stopwatch();

            watch.Start();
            action();
            watch.Stop();

            Console.WriteLine("{0} Time Elapsed {1} ms ({2} iterations/sec)", name, watch.Elapsed.TotalMilliseconds, 1000 / watch.Elapsed.TotalMilliseconds);
        }

        [Conditional("RELEASE")]
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

        [Conditional("RELEASE")]
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