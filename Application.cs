using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Reflection;


namespace Poly {
    public partial class App {
        public static bool Running = false;
        public static bool SetAllowUnsafeHeaderParsing20() {
            //Get the assembly that contains the internal class
            Assembly aNetAssembly = Assembly.GetAssembly(typeof(System.Net.Configuration.SettingsSection));
            if (aNetAssembly != null) {
                //Use the assembly in order to get the internal type for the internal class
                Type aSettingsType = aNetAssembly.GetType("System.Net.Configuration.SettingsSectionInternal");
                if (aSettingsType != null) {
                    //Use the internal static property to get an instance of the internal settings class.
                    //If the static instance isn't created allready the property will create it for us.
                    object anInstance = aSettingsType.InvokeMember("Section",
                      BindingFlags.Static | BindingFlags.GetProperty | BindingFlags.NonPublic, null, null, new object[] { });

                    if (anInstance != null) {
                        //Locate the private bool field that tells the framework is unsafe header parsing should be allowed or not
                        FieldInfo aUseUnsafeHeaderParsing = aSettingsType.GetField("useUnsafeHeaderParsing", BindingFlags.NonPublic | BindingFlags.Instance);
                        if (aUseUnsafeHeaderParsing != null) {
                            aUseUnsafeHeaderParsing.SetValue(anInstance, true);
                            return true;
                        }
                    }
                }
            }
            return false;
        }

		public static void Init(int LogLevel = Log.Levels.None) {
            if (LogLevel != Log.Levels.None) {
                App.Log.Active = true;
                App.Log.Level = LogLevel;
            }

            App.Log.Info("Application initializing...");
            
            int workerThreads, completionThreads;

            ThreadPool.GetMaxThreads(out workerThreads, out completionThreads);
            ThreadPool.SetMaxThreads(workerThreads, completionThreads * 16);

            SetAllowUnsafeHeaderParsing20();
			Running = true;
            App.Log.Info("Application running...");
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
