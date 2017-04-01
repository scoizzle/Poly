namespace Poly.Http {
	using Net.Http;
    using Data;

    class Program : App {
		public static Server WebServer;
		public static void Main(string[] args) {
			Init(args);

			WebServer = new Server("*");

            WebServer.On("/echo/{msg -> UrlDescape}", (request) =>
            {
                return Result.Send(
                    client: request.Client,
                    status: Result.Ok,
                    content: request.Arguments["msg"] as string,
                    headers: new KeyValueCollection<string> {
                        { "Content-Type", "text/html" }
                    }
                );
            });

			WebServer.Start();

            WaitforExit();
        }
    }
}

