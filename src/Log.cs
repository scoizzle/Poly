using System;
using System.Diagnostics;
using System.IO;

namespace Poly {

    public class Log {
        public static Action<string> WriteLine = (str) => { };

        public static void To(Action<string> method) =>
            WriteLine = method;

        public static void ToConsole() =>
            To(Console.WriteLine);

        public static void ToFile(string file_name) =>
            To(str => { File.AppendAllText(file_name, str + Environment.NewLine); });

        private static void Print(string format, params object[] args) =>
            WriteLine(args.Length == 0 ? format : string.Format(format, args));

        private static void Print(string level, DateTime time, string message) =>
            WriteLine(
                string.Join(
                    " ",
                    time.ToString("yyyy-MM-dd hh:mm:ss"),
                    level,
                    message
            ));

        private static void Print(string level, DateTime time, string format, params object[] args) =>
            WriteLine(
                string.Join(
                    " ",
                    time.ToString("yyyy-MM-dd hh:mm:ss"),
                    level,
                    args.Length == 0 ? format : string.Format(format, args)
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

        public static void Benchmark(string name, Stopwatch watch) =>
            Print(
                "BENCH", 
                DateTime.UtcNow, 
                "{0} Time Elapsed {1} ms ({2} iterations/sec)", 
                    name, 
                    watch.Elapsed.TotalMilliseconds, 
                    1000 / watch.Elapsed.TotalMilliseconds
            );

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

            Benchmark(name, watch);
        }

        [Conditional("RELEASE")]
        public static void Benchmark(string name, int iterations, Action Todo) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Todo();

            var watch = new Stopwatch();

            int i = 0;
            for (; i < iterations; i++) {
                watch.Start();
                Todo();
                watch.Stop();
            }

            Benchmark(name, watch);
        }

        [Conditional("RELEASE")]
        public static void Benchmark(string name, int iterations, Action<int> Todo) {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            Todo(0);

            var watch = new Stopwatch();

            int i = 1;
            for (; i < iterations; i++) {
                watch.Start();
                Todo(i);
                watch.Stop();
            }

            Benchmark(name, watch);
        }
    }
}