using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public class Conversation : JSON {
        public string Name { 
            get { return Get<string>("Name"); }
            set { Set("Name", value); }
        }

        public string Topic { 
            get { return Get<string>("Topic"); }
            set { Set("Topic", value); }
        }

        public JSON Users { 
            get { return Get<JSON>("Users"); }
            set { Set("Users", value); }
        }

        public JSON Modes { 
            get { return Get<JSON>("Modes"); }
            set { Set("Modes", value); }
        }

        public Conversation(string name) {
            Name = name;
            Topic = string.Empty;
            Users = new JSON();
            Modes = new JSON();
        }


        public override string ToString() {
            return Name;
        }
    }
}