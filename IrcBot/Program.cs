using System.Threading.Tasks;

namespace Poly.Bot {
    using Net.Irc;

    class Program : App {
		public static Task WorkerTask;

        public static void Main(string[] args) {
            Init(args);

            WorkerTask = IRC();

            WaitforExit();
        }

        public static async Task IRC() {
            var client = new Client(@"{
                'Server': 'irc.k108fm.com',
                'User': {
                    'Nick': 'PolyBot',
                    'Ident': 'PolyBot',
                    'Realname': 'PolyBot'
                },
				'Autojoin': {
					'#main': ''
				}
            }");

            client.On(Packet.Msg, ctx => {
                Log.Info("[{0}] <{1}>: {2}", ctx.Conv.Name, ctx.Sender.Nick, ctx.Packet.Message);
            });

            client.On(Packet.Notice, ctx => {
                Log.Info("[{0}] <{1}>: {2}", ctx.Conv.Name, ctx.Sender.Nick, ctx.Packet.Message);
            });

            client.On(Packet.Msg, ctx => { 
                var match = new Matcher(ctx.Packet.Message);
                var result = match | ctx.Client.Config;

				if (result?.Length != null) 
                    ctx.Client.SendMessage(ctx.Conv, result);
            });

			App.Commands.Register("/join {chan}( {key})?", Event.Wrapper((string chan, string key) => {
                client.JoinChannel(chan, key);
            }));

			App.Commands.Register("/part {chan}( {msg})?", Event.Wrapper((string chan, string msg) => {
                client.PartChannel(chan, msg);
            }));

			App.Commands.Register("{target}: {msg}", Event.Wrapper((string target, string msg) => {
                client.SendMessage(target, msg);
            }));

            await client.Start();
        }
    }
}

