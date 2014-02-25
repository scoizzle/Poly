using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

namespace Poly.Net.Irc {
    public partial class Server {
        public void PingPong(object arg) {
            while (true) {
                Users.ForEach<User>((Nick, User) => {
                    var Delay = DateTime.Now - User.LastPongTime;

                    if (Delay.TotalSeconds > PingTimeout * 1000) {
                        User.Disconnect("Ping timeout.");
                    }
                    else {
                        User.Ping();
                    }
                });

                Thread.Sleep(PingDelay * 1000);
            }
        }
    }
}
