using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Threading.Tasks;

using Poly;
using Poly.Data;

namespace Poly.Net.Irc {
    public partial class Client : User {
        public bool Connect() {
            return Connect(Server, Port);
        }

        public bool Connect(string Server, int Port) {
            Connection.Connect(Server, Port);

            return Connection.Connected;
        }

        public void Disconnect() {
            if (Connection != null)
                Connection.Close();
        }

        public async Task Start() {
            Connection = new Tcp.Client();
            Connect();

            await Task.Factory.StartNew(HandleBasicConnection);
        }

        public async void Restart() {
            Stop();
            await Start();
        }

        public void Stop() {
            Disconnect();

            Connection = null;
        }

        public void Send(Packet Packet) {
            if (Packet.Message.Contains(Environment.NewLine)) {
                var Messages = Packet.Message.Split('\n');

                foreach (var Msg in Messages) {
                    Packet.Message = Msg;
                    Send(Packet);
                }
            }
            else {
                Packet.Send(Connection);
            }
        }

        public void SendPong(string Message) {
            Send(new Packet(Packet.Pong, Message));
        }

        public void SendUser(string Name, string RealName, bool Hidden = false) {
            Packet.Send(Connection, Packet.User, RealName, new string[] {
                Name, 
                Hidden ? "8" : "0",
                "*"
            });
        }

        public void SendPass(string Pass) {
            Send(new Packet(Packet.Pass, string.Empty, Pass));
        }

        public void SendNick(string Nick) {
            Send(new Packet(Packet.Nick, string.Empty, Nick));
        }

        public void SendOper(string Name, string Pass) {
            Send(new Packet(Packet.Oper, Pass, Name));
        }

        public void SendTopic(string Convo, string Topic) {
            Send(new Packet(Packet.Topic, Topic, Convo));
        }

        public void JoinChannel(string Channel) {
            JoinChannel(Channel, "");
        }

        public void JoinChannel(string Channel, string Key = "") {
            Send(new Packet(Packet.Join, Key, Channel));

            if (!Conversations.ContainsKey(Channel))
                Conversations.Set(Channel, new Conversation(Channel));
        }

		public void PartChannel(string Channel) {
			PartChannel (Channel, "");
		}

        public void PartChannel(string Channel, string Message = "") {
            Send(new Packet(Packet.Part, Message, Channel));

            if (Conversations.ContainsKey(Channel))
                Conversations.Remove(Channel);
        }

        public void QuitServer(string Message = "") {
            Send(new Packet(Packet.Quit, Message));
        }

        public void SendMessage(string Target, string Message) {
            Send(new Packet(Packet.Msg, Message, Target));
        }

        public void SendMessage(Conversation Convo, string Message) {
            SendMessage(Convo.Name, Message);
        }

        public void SendMessage(User User, string Message) {
            SendMessage(User.Nick, Message);
        }

        public void SendNotice(string Target, string Message) {
            Send(new Packet(Packet.Notice, Message, Target));
        }

        public void SendNotice(Conversation Convo, string Message) {
            SendNotice(Convo.Name, Message);
        }

        public void SendNotice(User User, string Message) {
            SendNotice(User.Nick, Message);
        }

        public void SendCTCP(string Target, string Message) {
            SendMessage(
                Target,
                '\x0001' + Message + '\x0001'
            );
        }

        public void SendCTCPReply(string Target, string Message) {
            SendNotice(
                Target,
                '\x0001' + Message + '\x0001'
            );
        }

        public void SendAction(string Target, string Message) {
            SendCTCP(Target, "ACTION " + Message);
        }

        public void SendAction(Conversation Convo, string Message) {
            SendAction(Convo.Name, Message);
        }

        public void SendAction(User User, string Message) {
            SendAction(User.Nick, Message);
        }

        public void SendMode(string Target, string Modes) {
            Send(new Packet(Packet.Mode, Modes, Target));
        }

        public void SendWho(string Search) {
            Send(new Packet(Packet.Who, "", Search));
        }

        public void SendWhois(string Search) {
            Send(new Packet(Packet.Whois, "", Search));
        }

        public void SendWhowas(string Search) {
            Send(new Packet(Packet.Whowas, "", Search));
        }

        public void RegisterConnection() {
            if (!Connected)
                return;

            if (!string.IsNullOrEmpty(Password))
                SendPass(Password);

            SendNick(Nick);
            SendUser(Ident, Realname);
        }
    }
}