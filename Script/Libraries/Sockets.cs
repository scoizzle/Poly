using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace Poly.Script.Libraries {
    using Data;
    using Node;

    public class Sockets : Library {
        public Sockets() {
            RegisterTypeByName("Socket", typeof(Socket), this);

            Add(Socket);
            Add(Bind);
            Add(Send);
            Add(SendLine);
            Add(Read);
            Add(ReadLine);
        }

        public static SystemFunction Socket = new SystemFunction("Socket", (Args) => {
            var Type = Args.Get<string>("Type");

            if (!string.IsNullOrEmpty(Type)) {
                switch (Type.ToUpper()) {
                    case "TCP":
                        return new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

                    case "UDP":
                        return new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp);
                }
            }
            return null;
        }, "Type");

        public static SystemFunction Bind = new SystemFunction("Bind", (Args) => {
            var This = Args.Get<Socket>("this");

            if (This != null) {
                var IP = Args.Get<string>("IP");
                var Port = Args.Get<int>("Port");

                This.Bind(
                    new IPEndPoint(
                        IPAddress.Parse(IP), Port
                    )
                );

                return true;
            }
            return false;
        }, "IP", "Port");

        public static SystemFunction Send = new SystemFunction("Send", (Args) => {
            var This = Args.Get<Socket>("this");

            if (This != null) {
                var Data = Args.Get<string>("Data");
                var Buffer = Encoding.Default.GetBytes(Data);

                for (var Sent = 0;
                         Sent < Buffer.Length;
                         Sent += This.Send(Buffer, Buffer.Length - Sent, SocketFlags.None)
                    ) ;

                return true;
            }

            return false;
        }, "Data");

        public static SystemFunction SendLine = new SystemFunction("SendLine", (Args) => {
            var This = Args.Get<Socket>("this");

            if (This != null) {
                var Data = Args.Get<string>("Data") + Environment.NewLine;
                var Buffer = Encoding.Default.GetBytes(Data);

                for (var Sent = 0;
                         Sent < Buffer.Length;
                         Sent += This.Send(Buffer, Buffer.Length - Sent, SocketFlags.None)
                    ) ;

                return true;
            }

            return false;
        }, "Data");

        public static SystemFunction Read = new SystemFunction("Read", (Args) => {
            var This = Args.Get<Socket>("this");

            if (This == null)
                return false;

            var Length = Args.Get<int>("Length");
            var Buffer = new byte[Length];

            for (var Read = 0;
                Read < Length;
                Read += This.Receive(Buffer, Read, Length - Read, SocketFlags.None)
            ) ;

            return Encoding.Default.GetString(Buffer);
        }, "Length");

        public static SystemFunction ReadLine = new SystemFunction("ReadLine", (Args) => {
            var This = Args.Get<Socket>("this");

            if (This == null)
                return false;

            var Buffer = new byte[1];
            var Output = new StringBuilder();

            while (This.Available == 0) 
                Thread.Sleep(1);

            while (true) {
                if (This.Receive(Buffer, 1, SocketFlags.None) != 1) {
                    return false;
                }
                else {
                    Output.Append(char.ConvertFromUtf32(Buffer[0]));
                }

                if (Buffer[0] == '\n') {
                    return Output.ToString(0, Output.Length - 1);
                }
            }
        });
    }
}
