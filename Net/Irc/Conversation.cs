using System;
using System.Collections;
using System.Collections.Generic;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public class Conversation : jsObject {
        public Conversation(string Name) {
            this.Name = Name;
        }

        public jsObject Users {
            get {
                if (!ContainsKey("Users"))
                    Set("Users", new jsObject() { IsArray = true });

                return Get<jsObject>("Users", (jsObject)null);
            }
        }

        public string Name {
            get {
                return Get<string>("Name", string.Empty);
            }
            set {
                Set("Name", value);
            }
        }

        public string Topic {
            get {
                return Get<string>("Topic", string.Empty);
            }
            set {
                Set("Topic", value);
            }
        }

        public jsObject Modes {
            get {
                if (!ContainsKey("Modes")) {
                    Set("Modes", new jsObject() { IsArray = true });
                }

                return Get<jsObject>("Modes", (jsObject)null);
            }
        }

        public override string ToString() {
            return Name;
        }
    }
}