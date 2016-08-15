using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Client : User {
        public void HandleBasicConnection() {
            RegisterConnection();

            while (Connected)
            try {
                do {
                    var In = Packet.Receive(Connection);

                    if (In != null)
                            HandlePacket(In);
                } while (Connected);
            }
            catch { }

            App.Log.Warning("Connection lost.");
            App.Log.Warning("Reconnecting.");
            Thread.Sleep(1000);
            Restart();
        }

        public void HandlePacket(object rawPacket) {
            var Packet = rawPacket as Packet;
            User Sender = new User(Packet.Sender);

            if (Users.ContainsKey(Sender.Nick))
                Sender = Users.GetValue<User>(Sender.Nick);
            else
                Users[Sender.Nick] = Sender;

            Conversation Conv;

            if (string.IsNullOrEmpty(Packet.Receiver)) {
                Conv = Conversations[this.Nick] ?? (Conversations[this.Nick] = new Conversation(this.Nick));
            }
            else
            if (Packet.Receiver == this.Nick){
                Conv = Conversations[Sender.Nick] ?? (Conversations[Sender.Nick] = new Conversation(Sender.Nick));
            }
            else {
                Conv = Conversations[Packet.Receiver] ?? (Conversations[Packet.Receiver] = new Conversation(Packet.Receiver));
            }                

            if (!Conv.Users.ContainsKey(Sender.Nick))
                Conv.Users.Add(Sender.Nick, Sender);

            var Context = new jsObject(
                "Convo", Conv,
                "User", Sender,
                "Packet", Packet
            );

            int Val = 0;
            if (!int.TryParse(Packet.Type, out Val)) {
                switch (Packet.Type) {
                    case Packet.Ping:
                        SendPong(Packet.Message);
                        break;

                    case Packet.Nick:
                        Users.Remove(Sender.Nick);
                        Sender.Nick = Packet.Receiver;
                        Users.Set(Sender.Nick, Sender);
                        break;

                    case Packet.Join:
                        if (Sender.Nick != Nick) {
                            Conv.Users.Set(Sender.Nick, Sender);
                        }
                        break;

                    case Packet.Part:
                        if (Sender.Nick == Nick) {
                            Conversations.Remove(Conv.Name);
                        }
                        else {
                            Conv.Users.Remove(Sender.Nick);
                        }
                        break;

                    case Packet.Quit:
                        if (Conversations.ContainsKey(Sender.Nick))
                            Conversations.Remove(Sender.Nick);

                        Conversations.ForEach<Conversation>((Name, Conver) => {
                            if (Conver.Users.ContainsKey(Sender.Nick)) {
                                Conver.Users.Remove(Sender.Nick);
                            }
                        });
                        break;

                    case Packet.Msg:
                        if (Packet.IsCTCP) {
                            jsObject Extract = Packet.Message.Match("\x01PING {Msg}\x01");

                            if (Extract != null)
                                SendMessage(Conv, Extract.Template("\x01PONG {Msg}\x01"));
                        }
                        Packet.Type = "Msg";
                        break;

                    case Packet.Mode: {
                            var Modes = Packet.Message.ToCharArray();
                            var Nicks = Packet.Receiver.Split(',');

                            var Operator = '+';

                            for (int Index = 0; Index < Nicks.Length && Index < Modes.Length; Index++) {
                                var Target = Nicks[Index];
                                var TargetMode = Modes[Index];

                                if (string.IsNullOrEmpty(TargetMode.ToString()))
                                    continue;

                                if (TargetMode == '+') {
                                    Operator = '+';
                                    continue;
                                }
                                else if (TargetMode == '-') {
                                    Operator = '-';
                                    continue;
                                }

                                var Usr = Users.Get<User>(Target) ?? new User(Target);

                                if (Operator == '+') {
                                    Usr.Modes[TargetMode.ToString()] = true;
                                }
                                else {
                                    Usr.Modes[TargetMode.ToString()] = null;
                                }
                            }
                        } break;
                        /*
                    case Packet.Reply.OnChannelMode: {
                            var Modes = Packet.Message.ToCharArray();
                            var Nicks = Packet.Receiver.Split(',');

                            var Channel = Packet.Get<string>("Channel");

                            var Operator = '+';
                            for (int Index = 0; Index < Nicks.Length && Index < Modes.Length; Index++) {
                                var Target = Nicks[Index];
                                var TargetMode = Modes[Index];

                                if (string.IsNullOrEmpty(TargetMode.ToString()))
                                    continue;

                                if (TargetMode == '+') {
                                    Operator = '+';
                                    continue;
                                }
                                else if (TargetMode == '-') {
                                    Operator = '-';
                                    continue;
                                }

                                var Conver = Conversations.Get<Conversation>(Channel);

                                if (Conver == null)
                                    continue;

                                if (Operator == '+') {
                                    Conver.Modes[Target, TargetMode.ToString()] = true;
                                }
                                else {
                                    Conver.Modes[Target, TargetMode.ToString()] = null;
                                }
                            }
                        } break;
                         * */
                    case Packet.Topic:
                        Conv.Topic = Packet.Message;
                        break;
                }

                InvokeEvent("On" + Packet.Type, Context);
            }
            else {
                Packet.Reply Type;

                try { Type = (Packet.Reply)(Val); }
                catch { return; }

                switch (Type) {
                    case Packet.Reply.OnTopicInit:
                        Conv.Topic = Packet.Message;
                        break;

                    case Packet.Reply.OnChannelUserList: {
                        var Nicks = Packet.Message.Split(' ');
                        var Channel = Packet.Args.Split(' ')[1];

                        var Conver = Conversations.Get<Conversation>(Channel);

                        if (Conver == null)
                            break;

                        foreach (var N in Nicks) {
                            if (string.IsNullOrEmpty(N))
                                continue;

                            var Name = N;
                            var C = Name[0];

                            if (!char.IsLetter(C)) {
                                C = CharModes.Get<char>(N.Substring(0, 1));
                                Name = Name.Substring(1);
                            }

                            var Usr = Users.Get<User>(Name) ?? new User(Name);

                            if (C != default(char) && !char.IsLetter(N[0])) {
                                Usr.Modes[Channel, C.ToString()] = true;
                            }
                        }

                        SendWho(Channel); 
                        break;
                    }

                    case Packet.Reply.OnWho: {
                            var Arguments = Packet.Args.Split(' ');

                            if (Arguments.Length < 5)
                                break;

                            var TargetNick = Arguments[4]; 
                            var Target = Users.Get<User>(TargetNick) ?? (Users[TargetNick] = new User(Packet.Template(User.TemplateFormat)));
                            
                            Target.Ident = Arguments[1];
                            Target.Host = Arguments[2];
                            Target.Realname = Packet.Message.Substring(" ", "");


                            var TargetModes = Arguments[5].ToCharArray();

                            foreach (var M in TargetModes) {
                                var C = M;
                                if (!char.IsLetter(C))
                                    C = CharModes.Get<char>(M.ToString());

                                if (C == default(char))
                                    continue;

                                Target.Modes[Arguments[0], C.ToString()] = true;
                            }
                        } break;

                    case Packet.Reply.OnServerFeatures: {
                            var Features = Packet.Get<string>("Features");
                            var Data = Features.Match("*PREFIX=({Modes}){Chars} *");

                            if (Data == null)
                                break;

                            var Modes = Data.Get<string>("Modes").ToCharArray();
                            var Chars = Data.Get<string>("Chars").ToCharArray();

                            for (int Index = 0; Index < Modes.Length; Index++) {
                                CharModes[Chars[Index].ToString()] = Modes[Index];
                            }
                        } break;

                }

                var EventName = Enum.GetName(typeof(Packet.Reply), Val);

                if (string.IsNullOrEmpty(EventName))
                    return;

                InvokeEvent(EventName, Context);
            }
        }
    }
}