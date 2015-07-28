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
    public class Client : TcpClient {
        public bool Secure;
        
        public Stream Stream { get; private set; }

        public StreamReader Reader { get; private set; }

        public StreamWriter Writer { get; private set; }

        public Client() : base() { 
            Secure = false;
            NoDelay = true;
        }

        public Client(Socket This) : this() {
            this.Client = This;

            Stream = GetStream();
            Reader = new StreamReader(Stream);
            Writer = new StreamWriter(Stream);

            Writer.AutoFlush = true;
        }

        public Client(TcpClient This) : this(This.Client) { }

        public void Dispose() {
            Writer.Close();
            Reader.Close();

            Stream.Close();

            Close();
        }
        
        public new void Connect(string hostname, int port) {
            try {
                base.Connect(hostname, port);

                if (Connected) {
                    Stream = GetStream();
                    Reader = new StreamReader(Stream);
                    Writer = new StreamWriter(Stream);

                    Writer.AutoFlush = true;
                }
            }
            catch { return; }
        }

        public void SecureConnect(string securehostname, int port) {
            this.Secure = true;

            try {
                base.Connect(securehostname, port);
            }
            catch { return; }

            if (Connected) {
                var SecureStream = new SslStream(GetStream());

                try {
                    SecureStream.AuthenticateAsClient(securehostname);

                    Stream = SecureStream;
                    Reader = new StreamReader(Stream);
                    Writer = new StreamWriter(Stream);

                    Writer.AutoFlush = true;
                }
                catch {
                    Dispose();
                }
            }
        }

        public void Send(string Data) {
            if (Connected) {
                try {
                    Writer.Write(Data);
                    Writer.Flush();
                }
                catch (InvalidOperationException) {
                    Dispose();
                }
                catch { }
            }
        }
        public void SendLine(string Line) {
            if (Connected) {
                try {
                    Writer.WriteLine(Line);
                    Writer.Flush();
                }
                catch (InvalidOperationException) {
                    Dispose();
                }
                catch { }
            }
        }

        public async void SendLineAsync(string Line) {
            if (Connected) {
                try {
                    await Task.Run(() => {
                        Writer.WriteLine(Line);
                        Writer.Flush();
                    });
                }
                catch (InvalidOperationException) {
                    Dispose();
                }
                catch { }
            }
        }

        public byte[] Read(int Length) {
            if (Connected && !Reader.EndOfStream) {
                try {
                    int Remaining = Length;
                    byte[] Buffer = new byte[Length];

                    while (Remaining > 0)
                        Remaining -= Stream.Read(Buffer, Length - Remaining, Remaining);

                    return Buffer;
                }
                catch (InvalidOperationException) {
                    Dispose();
                }
                catch { }
            }

            return null;
        }

        public string ReadLine() {
            if (Connected) {
                try {
                    return Reader.ReadLine();
                }
                catch (InvalidOperationException) {
                    Dispose();
                }
                catch { }
            }

            return null;
        }

        public async Task<string> ReadLineAsync() {
            if (Connected) {
                try {
                    return await Reader.ReadLineAsync();
                }
                catch (InvalidOperationException) {
                    Dispose();
                }
                catch { }
            }

            return null;
        }
    }
}


