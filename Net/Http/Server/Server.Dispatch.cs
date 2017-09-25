using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Poly.Net.Http {
	using Tcp;

	public partial class Server {
		async Task OnConnected(Client client) {
			Connection connection = new V1.Connection(client);

			try {
				while (Running && client.Connected) {
					var receive = connection.Receive(out Request request);
					if (!receive)
						break;

					var process = ProcessRequest(connection, request, out Response response);
                    if (!process)
                        connection.New(out response, Result.InternalError);

					var send = connection.Send(response);
					if (!send)
						break;

					await Task.Yield();
				}
			}
			catch (Exception Error) {
				Log.Debug(Error);
			}

			client.Close();
		}

		bool VerifyHost(Connection connection, Request request, out Response response) {
			if (Config.Host.Matcher.Compare(request.Authority))
				return HandleRequest(connection, request, out response);

			response = null;
			return false;
		}

		bool HandleRequest(Connection connection, Request request, out Response response) {
			var handler_found =
				Handlers.TryGetValue(
					request.Target,
					out Handler handler);

			if (handler_found)
				return handler(connection, request, out response);

            connection.New(out response, Result.NotFound);
			return true;
		}
	}
}