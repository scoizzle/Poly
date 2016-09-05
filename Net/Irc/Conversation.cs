using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public class Conversation : jsComplex {
        public string Name, Topic, Key;
        public jsObject Users, Modes;


        public Conversation(string Name) {
            this.Name = Name;
            Topic = string.Empty;
            Users = new jsObject();
            Modes = new jsObject();
        }


        public override string ToString() {
            return Name;
        }
    }
}