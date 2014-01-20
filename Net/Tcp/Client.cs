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
using System.Text;

using Poly;
using Poly.Data;

namespace Poly.Net.Tcp {
	public class Client : TcpClient {
        private StreamReader p_sRead = null;
        private StreamWriter p_sWrite = null;
        private Stream p_Stream = null;
        private bool p_bAutoFlush = true;
        
        public bool Secure = false;

        public bool autoFlush {
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
		
        public Client()
            : base() {
        }

        public Client(Socket Base) {
            this.Client = Base;
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
                    p_sWrite = new StreamWriter(this.GetStream());
                    p_sWrite.AutoFlush = p_bAutoFlush;
                }
                return p_sWrite;
            }
        }

        public new Stream GetStream() {
            if (p_Stream == null) {
                Stream Stream = base.GetStream();

                if (Secure) {
                    Stream = new SslStream(
                        Stream
                    );
                }

                p_Stream = new BufferedStream(Stream);
            }

            return p_Stream;
        }

        public bool Connect(string SecureServerName, int Port, bool Secure) {
            this.Secure = Secure;
            base.Connect(SecureServerName, Port);

            try {
                if (Secure) {
                    var SStream = new SslStream(base.GetStream(), false, new RemoteCertificateValidationCallback(ValidateServerCertificate), null);
                    SStream.AuthenticateAsClient(SecureServerName);
                    p_Stream = SStream;
                }
            }
            catch (Exception Error) {
                App.Log.Error(Error.Message);
                return false;
            }
            return Connected;
        }

        public new void Close() {
            if (Client.Connected) {
                p_sRead = null;
                p_sWrite = null;
            }
            base.Close();
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

        public string ReadLine() {
            if (!Connected) {
                return null;
            }

            try {
                return Reader.ReadLine();
            }
            catch { 
                return null;
            }
        }

        public virtual bool Send(string Packet) {
            return SendLine(Packet);
        }

        public virtual bool Send(byte[] Bytes) {
            if (!Connected || Bytes == null)
                return false;

            if (!Writer.BaseStream.CanWrite)
                return false;

            try {
                GetStream().Write(Bytes, 0, Bytes.Length);
            }
            catch { 
                return false; 
            }
            return true;
        }

        public virtual string Receive() {
            return ReadLine();
        }

        public virtual byte[] Receive(int Length) {
            byte[] Buffer = new byte[Length];

            try {
                for (int n = 0; n < Length; ) {
                    n += GetStream().Read(Buffer, n, Length - n);
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

