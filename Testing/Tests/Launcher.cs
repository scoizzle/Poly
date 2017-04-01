using Poly.Data;
using Poly.Net.Http;
using System;
using System.Diagnostics;

namespace Poly.Test {
    public partial class Tests {
        /*
        1.  Launcher.exe begins generating local MD5 list (async, background)
            Launcher.exe opens http://url/to/ui/
            UI Loads & Does whatever init stuffs
            UI Tells Launcher.exe to download manifest (updates UI to alert user)
            Launcher.exe checks Manifest against MD5 list.
            UI continues to poll Launcher.exe for update status.
            On Finished UI shows Play button.

        2.  UI Sends Launch Request
            Launcher.exe sends redirect to success (or failure???) page
            
        3. ???
        4. Profit. 
        */
        public static void Launcher() {
            var server = new Server("localhost(:1337)?", 1337);

            server.On("/", (Request) => {
                Log.Debug("New connection from: {0}", Request.Client.RemoteIPEndPoint);

                return Result.Send(Request.Client, Result.Ok,
                    ContentString: "<a href=\"/Launch/%2FSuccess%2F/\">Click to Launch!</a>");
            });

            server.On("/Update/Manifest/", (Request) => {
                return Result.Send(Request.Client, Result.Ok,
                    ContentString: "Yay!");
            });

            server.On("/Success/", (Request) => {
                Log.Debug("Game launched!");
                return Result.Send(Request.Client, Result.Ok,
                    ContentString: "Yay!");
            });

            server.On("/Launch/{redir}/", (Request) => {
                Log.Debug("Launching game...");

                return Result.Send(Request.Client, Result.Found, 
                    Headers: new KeyValueCollection<string> {
                        { "Location", Request.Arguments.Get<string>("redir").UriDescape() }
                    });
            });

            if (server.Start()) {
                // Open url to ui
            }
            else {
                // Shit fuck industries.
            }
        }
    }
}