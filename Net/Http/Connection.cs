using System.IO;

namespace Poly.Net.Http {
    using Data;

    public abstract class Connection {
		public Tcp.Client Client;

        protected Connection() { }
		protected Connection(Tcp.Client client) {
			Client = client;
		}

        public abstract void New(out Request request, string method = null, string target = null, KeyValueCollection<string> headers = null, Stream body = null);
        public abstract void New(out Response response, Result status = Result.Invalid, KeyValueCollection<string> headers = null, Stream body = null);
		
		public abstract bool Send(Request request);
		public abstract bool Send(Response response);

		public abstract bool Receive(out Request request);
		public abstract bool Receive(out Response response);
	}
}