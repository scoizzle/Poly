using Poly.Data;
using System;
using System.Text;
using System.Threading;

namespace Poly {

    public partial class App {
        public static bool Running;
        public static Event.Engine<Event.Handler> Commands;

        public static Encoding Encoding = Encoding.UTF8;
        public static readonly string NewLine = "\r\n";
        public static readonly byte[] NewLineBytes = Encoding.GetBytes(NewLine);

        static App() {
            Commands = new Event.Engine<Event.Handler>();
        }

        public static void Init(params string[] Commands) {
            Running = true;

            Log.Info("Application initializing...");

            foreach (var cmd in Commands) {
                if (App.Commands.TryGetHandler(cmd, out Event.Handler handler, out JSON arguments)) {
                    handler(arguments);
                }
            }

            Log.Info("Application running...");
        }

        public static void WaitforExit() {
            while (Running) {
                var input = Console.ReadLine();

                if (Commands.TryGetHandler(input, out Event.Handler handler, out JSON arguments)) {
                    handler(arguments);
                }

                Thread.Sleep(200);
            }
        }

        public static void Exit(int Status = 0) {
            Log.Info("Applcation Exiting...");

            Running = false;
        }
    }
}