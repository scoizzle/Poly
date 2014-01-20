using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Client : User {
        public Poly.Event.Engine Events = new Event.Engine();

        public void InvokeEvent(string Name, jsObject Args) {
            Events.MatchAndInvoke(Name, Args);
        }

        public void AddEvent(string Name, Poly.Event.Handler Ev) {
            Events.Register(Name, Ev);
        }
    }
}