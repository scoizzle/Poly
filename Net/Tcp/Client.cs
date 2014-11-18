using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Threading.Tasks;
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Tcp {
	public class Client {
        private StreamReader p_sRead = null;
        private StreamWriter p_sWrite = null;
        private Stream p_Stream = null;
        private bool p_bAutoFlush = true;
        private int p_recvTimeout = int.MinValue;

        public Socket Socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        
        public bool Secure = false;

        public bool AutoFlush {
            get {
                if (Writer == null)
                    return p_bAutoFlush;
                if (p_bAutoFlush != Writer.AutoFlush)
                    Writer.AutoFlush = p_bAutoFlush;
                return p_bAutoFlush;                
            }
            set {
                p_bAutoFlush = value;
                if (Writer != null) {
                    Writer.AutoFlush = value;
                }
            }
        }

        public bool Connected {
            get {
                return Socket.Connected;
            }
        }
		
        public Client() {
        }

        public Client(Socket Base) {
            this.Socket = Base;
        }

        public int ReceiveTimeout {
            get {
                if (p_recvTimeout == int.MinValue) {
                    if (Socket.ReceiveTimeout == 0) {
                        return -1;
                    }
                    else {
                        return Socket.ReceiveTimeout;
                    }
                }
                return p_recvTimeout;
            }
            set {
                p_recvTimeout = value;
            }
        }

        public StreamReader Reader {
            get {
                if (p_sRead == null) {
                    p_sRead = new StreamReader(this.GetStream());
                }
                return p_sRead;
            }
        }

        public StreamWriter Writer {
            get {
                if (p_sWrite == null) {
                    try {
                        p_sWrite = new StreamWriter(this.GetStream());
                        p_sWrite.AutoFlush = p_bAutoFlush;
                    }
                    catch { }
                }
                return p_sWrite;
            }
        }

        public Encoding Encoding {
            get {
                if (Writer != null) {
                    return Writer.Encoding;
                }
                return Encoding.Default;
            }
        }

        public Stream GetStream() {
            if (!Connected)
                return (p_Stream = null);

            if (p_Stream == null) {
                Stream Stream = new NetworkStream(Socket);

                if (Secure) {
                    Stream = new SslStream(
                        Stream
                    );
                }

                p_Stream = new BufferedStream(Stream);
            }

            return p_Stream;
        }

        public bool Connect(string ServerName, int Port, int Timeout = 1000) {
            try {
                Socket.BeginConnect(ServerName, Port, (Obj) => {
                    Socket.EndConnect(Obj);
                }, Socket).AsyncWaitHandle.WaitOne(Timeout, false);

                return Socket.Connected;
            }
            catch {
                return false;
            }
        }

        public bool Connect(string SecureServerName, int Port, bool Secure, int Timeout = 1000) {
            if (Connect(SecureServerName, Port, Timeout)) {
                if (this.Secure = Secure) {
                    try {
                        var SStream = GetStream() as SslStream;
                        SStream.AuthenticateAsClient(SecureServerName);
                    }
                    catch (Exception Error) {
                        App.Log.Error(Error.Message);
                        return false;
                    }
                }
            }
            return false;
        }

        public void Close() {
            if (Connected) {
                p_sRead = null;
                p_sWrite = null;
            }
            Socket.Close();
		}

        public bool SendLine(string Packet) {
            if (!Connected)
                return false;

            if (!Writer.BaseStream.CanWrite)
                return false;

            try {
                Writer.WriteLine(Packet);
            }
            catch {
                return false;
            }
            return true;
        }

        public virtual bool SendLine(params string[] Parts) {
            if (!Writer.BaseStream.CanWrite)
                return false;

            try {
                for (int i = 0; Connected && i < Parts.Length; i++) {
                    Writer.Write(Parts[i]);
                }
                Writer.WriteLine();
            }
            catch {
                return false;
            }
            return true;
        }

        public string ReadLine() {
			if (!Connected) {
                return null;
            }

            try {
                var Task = Reader.ReadLineAsync();

                Task.Wait(ReceiveTimeout);

                if (Task.IsCompleted)
                    return Task.Result;                
            }
            catch { }
            return null;
        }

        public virtual bool Send(string Packet) {
            if (!Connected || string.IsNullOrEmpty(Packet))
                return false;

            return Send(Encoding.GetBytes(Packet));
        }

        public virtual void Send(params string[] Parts) {
            for (int i = 0; Connected && i < Parts.Length; i++) {
                Send(Encoding.GetBytes(Parts[i]));
            }
        }

        public virtual bool Send(byte[] Bytes) {
            if (!Connected || Bytes == null)
                return false;

            if (!Writer.BaseStream.CanWrite)
                return false;

            try {
                var Stream = GetStream();
                if (Stream == null)
                    return false;

                Stream.Write(Bytes, 0, Bytes.Length);
            }
            catch { 
                return false; 
            }
            return true;
        }

        public virtual string Receive() {
            return ReadLine();
        }
        
        public virtual char[] Receive(int Length) {

            char[] Buffer = new char[Length];

            try {
                for (int n = 0; n < Length; ) {
                    n += Reader.Read(Buffer, n, Length - n);
                }
            }
            catch {
                return null;
            }

            return Buffer;
        }

        public virtual string Request(string Data) {
            if (!Send(Data))
                return null;
            return Receive();
        }

        private static bool ValidateServerCertificate(object Sender, X509Certificate Cert, X509Chain Chain, SslPolicyErrors Errors) {
            return Errors == SslPolicyErrors.None;
        }

        public static implicit operator Client(Socket Sock) {
            return new Client(Sock);
        }
	}
}


