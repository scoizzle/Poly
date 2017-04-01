using System;
using System.Text;
using System.Threading.Tasks;

namespace Poly.Net.Http {
	using Data;
	using Tcp;

	public class Packet {
		public static readonly byte[] DoubleNewLine = Encoding.UTF8.GetBytes("\r\n\r\n");

		public string Method,
					  Version,
					  Target,
					  Query;

		public KeyValueCollection<string> Headers;

		public Packet() {
			Headers = new KeyValueCollection<string>();
		}

		public long ContentLength {
			get {
				return Convert.ToInt64(Headers["Content-Length"]);
			}
		}

		public string Request {
			get {
				return Target + ((Query?.Length > 0) ? '?' + Query : string.Empty);
			}
			set {
				var i = value.IndexOf('?');

				if (i == -1) {
					Target = value;
					Query = string.Empty;
				}
				else {
					Target = value.Substring(0, i);
					Query = value.Substring(i + 1);
				}
			}
		}

		public async Task<bool> Receive(Client client) {
			int x, y;
			string headers, Key, Value;

			if (!await client.ReadyToRead()) return false;

			try { headers = await client.ReceiveStringUntilConstrained(DoubleNewLine, Encoding.UTF8); }
			catch {
				client.Close();
				return false;
			}

			if (headers == null || headers.Length == 0)
				return false;

			y = headers.IndexOf(' ');
			Method = headers.Substring(0, y);
			x = y + 1;

			y = headers.IndexOf(' ', x);
			Request = headers.Substring(x, y - x);
			x = y + 1;

			y = headers.IndexOf('\r', x);
			Version = headers.Substring(x, y - x);
			x = y + 2;

			while (x < headers.Length) {
				y = headers.IndexOf(':', x);
				if (y == -1) break;

				Key = headers.Substring(x, y - x);

				x = y + 2;
				y = headers.IndexOf('\r', x);

				if (y == -1)
					y = headers.Length;

				Value = headers.Substring(x, y - x);
				x = y + 2;

				Headers.Set(Key, Value);
			}

			return true;
		}



		public void Reset() {
			Method = Version = Target = Query = string.Empty;
			Headers.Clear();
		}
	}
}