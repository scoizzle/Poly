using System.Threading.Tasks;

namespace Poly.Net.Irc
{
    public partial class Client {
        public async Task<bool> Connect(string Server, int Port) {
            Disconnect();
            Connection = new Tcp.Client();

            await Connection.Connect(Server, Port);
            return await Connection.IsConnected();
        }

        public void Disconnect() {
            Connection?.Close();
            Connection = null;
        }

        public async Task Start() {
            var Server = Config.Get<string>("Server");
            var Port = Config.Get<int?>("Port") ?? 6667;
            var Password = Config.Get<string>("Password");
            
            if (await Connect(Server, Port)) {
                if (!string.IsNullOrEmpty(Password)) 
                    await SendPass(Password);

                await SendNick(Info.Nick);
                await SendUser(Info.Ident, Info.Realname);

                await HandleBasicConnection();
            }
        }

        public void Stop() {
            Disconnect();
        }

        public Task<bool> Send(Packet Packet) {
            return Packet?.Send(Connection);
        }

        public Task<bool> SendPing(string Message) {
            return Send(
                new Packet(
                    type: Packet.Ping, 
                    msg: Message
            ));
        }


        public Task<bool> SendPong(string Message) {
            return Send(
                new Packet(
                    type: Packet.Pong, 
                    msg: Message
            ));
        }

        public Task<bool> SendUser(string Name, string RealName, bool Hidden = false) {
            return Send(
                new Packet(
                    type: Packet.User,
                    msg: RealName, 
                    args: new []{ 
                        Name, 
                        Hidden ? "8" : "0", 
                        "*" 
            }));
        }

        public Task<bool> SendPass(string Pass) {
            return Send(
                new Packet(
                    type: Packet.Pass, 
                    args: Pass
            ));
        }
        
        public Task<bool> SendNick(string Nick) {
            return Send(
                new Packet(
                    type: Packet.Nick, 
                    args: Nick
            ));
        }

        public Task<bool> SendOper(string Name, string Pass) {
            return Send(
                new Packet(
                    type: Packet.Oper, 
                    args: Name,
                    msg: Pass
            ));
        }

        public Task<bool> SendTopic(string Convo, string Topic) {
            return Send(
                new Packet(
                    type: Packet.Topic, 
                    args: Convo,
                    msg: Topic
            ));
        }

        public Task<bool> JoinChannel(string Channel, string Key) {
            return Send(
                new Packet(
                    type: Packet.Join, 
                    args: Channel,
                    msg: Key
            ));
        }
        
        public Task<bool> PartChannel(string Channel, string Message) {
            return Send(
                new Packet(
                    type: Packet.Part, 
                    args: Channel,
                    msg: Message
            ));
        }

        public Task<bool> QuitServer(string Message) {
            return Send(
                new Packet(
                    type: Packet.Quit, 
                    msg: Message
            ));
        }

        public Task<bool> SendMessage(string Target, string Message) {
            return Send(
                new Packet(
                    type: Packet.Msg,
                    args: Target, 
                    msg: Message
            ));
        }

        public Task<bool> SendMessage(User User, string Message) {
            return SendMessage(User.Nick, Message);
        }

        public Task<bool> SendMessage(Conversation Conv, string Message) {
            return SendMessage(Conv.Name, Message);
        }

        public Task<bool> SendNotice(string Target, string Message) {
            return Send(
                new Packet(
                    type: Packet.Notice,
                    args: Target, 
                    msg: Message
            ));
        }

        public Task<bool> SendNotice(User User, string Message) {
            return SendMessage(User.Nick, Message);
        }

        public Task<bool> SendNotice(Conversation Conv, string Message) {
            return SendMessage(Conv.Name, Message);
        }

        public Task<bool> SendCTCP(string Target, string Message) {
            return Send(
                new Packet(
                    type: Packet.Msg,
                    args: Target, 
                    msg: Message) {
                        IsCTCP = true
                    }
            );
        }

        public Task<bool> SendCTCPReply(string Target, string Message) {
            return Send(
                new Packet(
                    type: Packet.Notice,
                    args: Target, 
                    msg: Message) {
                        IsCTCP = true
                    }
            );
        }

        public Task<bool> SendAction(string Target, string Message) {
            return SendCTCP(Target, "ACTION " + Message);
        }

        public Task<bool> SendAction(User User, string Message) {
            return SendAction(User.Nick, Message);
        }

        public Task<bool> SendAction(Conversation Conv, string Message) {
            return SendAction(Conv.Name, Message);
        }

        public Task<bool> SendMode(string Target, string Modes) {
            return Send(
                new Packet(
                    type: Packet.Mode,
                    args: Target, 
                    msg: Modes
            ));
        }

        public Task<bool> SendWho(string Target) {
            return Send(
                new Packet(
                    type: Packet.Who,
                    args: Target
            ));
        }

        
        public Task<bool> SendWhois(string Target) {
            return Send(
                new Packet(
                    type: Packet.Whois,
                    args: Target
            ));
        }

        
        public Task<bool> SendWhowas(string Target) {
            return Send(
                new Packet(
                    type: Packet.Whowas,
                    args: Target
            ));
        }
    }
}