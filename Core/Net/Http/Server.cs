using System;
using System.Threading;

namespace Poly.Net.Http {
	using Data;

	public partial class Server : Host {
		Tcp.Server Listener;

		int maxConcurrentUsers;
		SemaphoreSlim sem;

        public Server(string hostname) : this(hostname, 80) { }

        public Server(string hostname, int port) : base(hostname) {
			Port = port;
			MaxConcurrentUsers = Environment.ProcessorCount;
		}

        ~Server() { 
            Stop();
		}

		public bool Running { get { return Listener?.Running == true; } }

		public int Port {
			get {
				return Listener?.Port ?? 80;
			}
			set {
				var old = Listener;

				Listener = new Tcp.Server(value);
				Listener.OnClientConnected += OnClientConnected;

				old?.Stop();
			}
		}

		public int MaxConcurrentUsers {
			get {
				return maxConcurrentUsers;
			}
			set {
				maxConcurrentUsers = value;
				sem = new SemaphoreSlim(value);
			}
		}

        public virtual bool Start() {
            Ready();
            return Listener.Start();
        }

        public virtual void Stop() {
            Listener?.Stop();
            Listener = null;
        }

        public virtual void Restart() {
            Stop();
            Start();
        }
    }
}
