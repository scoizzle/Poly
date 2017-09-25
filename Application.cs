using System;
using System.Text;
using System.Threading.Tasks;

using Poly.Data;

namespace Poly
{
    public partial class App {
        public static bool Running;
		public static Event.Engine<Event.Handler> Commands;

        public static Encoding Encoding             = Encoding.UTF8;
        public static readonly string NewLine       = "\r\n";
        public static readonly byte[] NewLineBytes  = Encoding.GetBytes(NewLine);

        static App() {
            Running = false;
            Log.Level = Log.Levels.Debug;

            Commands = new Event.Engine<Event.Handler>();
            Commands.Add("Log.Level^=^{Level}^", Event.Wrapper((string Level) => {
                Log.Level = (Log.Levels)Enum.Parse(typeof(Log.Levels), Level);
                return Level;
            }));
        }

        public static string Query(string Question) {
            Console.Write(Question);
            return Console.ReadLine();
        }

        public static void Init(params string[] Commands) { 
            Log.Active = Running = true;

            Log.Info("Application initializing...");

            foreach (var cmd in Commands) {
                var args = new JSON();
                var f = App.Commands.GetHandler(cmd, args);

                if (f != null) {
                    f.Invoke(args);
                    continue;
                }
            }

            Log.Info("Application running...");
        }

        public static void WaitforExit() {
            while (Running) {
				var Line = Console.ReadLine();

                if (Line == null)
                    Task.Delay(500);
                else {
                    var args = new JSON();
                    var f = App.Commands.GetHandler(Line, args);

                    if (f != null) {
                        f.Invoke(args);
                        continue;
                    }
                }

                Task.Delay(50);
            }
        }

		public static void Exit(int Status = 0) {
            Log.Info("Applcation Exiting...");

            Running = false;
            Task.Delay(1000);
		}
	}
}
