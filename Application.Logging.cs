using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;

namespace Poly {
    public partial class App {
        public class Log {
            public static bool Active = false;
            public static int Level = 0;

            public class Levels {
                public const int None = -1;
                public const int Fatal = 0;
                public const int Error = 1;
                public const int Warning = 2;
                public const int Info = 3;
            };

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
                    Console.Write(part);
                }

                Console.WriteLine();
            }

            public static void Benchmark(string Name, int Iterations, Action<int> Todo) {
                int Start = Environment.TickCount;
                for (int i = 0; i < Iterations; i++) {
                    Todo(i);
                }
                int Stop = Environment.TickCount;

                Info(Name + ": " + (Stop - Start).ToString());
            }
        }
    }
}