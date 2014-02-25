using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Poly.Net.Irc {
    public partial class Server {
        public partial class Channel : Irc.Conversation {
            public Channel(string Name)
                : base(Name) {
            }

            public void Broadcast(Packet Packet, params string[] Ignore) {
                Users.ForEach<User>((Key, Person) => {
                    if (Ignore.Contains(Key))
                        return;

                    Person.Send(Packet);
                });
            }
        }
    }
}
