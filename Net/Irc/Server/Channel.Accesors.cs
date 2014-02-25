using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net.Irc {
    public partial class Server {
        public partial class Channel : Irc.Conversation {
            public string Password {
                get {
                    return getString("Password");
                }
                set {
                    Set("Password", value);
                }
            }

            public new string Topic {
                get {
                    return base.Topic;
                }
                set {
                    TopicSetTime = DateTime.Now;
                    base.Topic = value;
                }
            }

            public DateTime TopicSetTime {
                get {
                    return DateTime.FromBinary(
                        getLong("TopicSetTime")
                    );
                }
                set {
                    Set("TopicSetTime", value.ToBinary().ToString());
                }
            }
        }
    }
}
