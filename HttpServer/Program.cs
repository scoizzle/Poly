namespace Poly.Http {
    class Program : App {
        public static void Main(string[] args) {
            var Server = new Net.Http.Server("*");

            Commands.Register("-host={0}", js => {
                return Server.Name = js.Get<string>("0");
            });

            Commands.Register("-port={0 -> int}", js => {
                return Server.Port = js.Get<int>("0");
            });

            Commands.Register("-path={0}", js => {
                return Server.Path = js.Get<string>("0");
            });

            Commands.Register("-stop", js => {
                Server.Stop();
                return null;
            });
            
            Commands.Register("-start", js => {
                Server.Start();
                return null;
            });

            Commands.Register("-restart", js => {
                Server.Restart();
                return null;
            });

            Commands.Register("--exit", js => {
                Server.Stop();
                App.Exit(0);
                return null;
            });

            Init(args);
            Server.Start();
            WaitforExit();
        }
    }
}

