using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Tcp;
    using Data;

    public static class MultipartHandler_ {
		static readonly byte[] doubleDash = Encoding.UTF8.GetBytes("--");
        static readonly Matcher PropertiesMatcher = new Matcher("{Key}: {Value}(!;? {Key}=\"{Value}\")?");

        public static async Task<bool> Receive(Client client, Packet packet, string Boundary) {
            var bb = Encoding.UTF8.GetBytes("--" + Boundary);
            if (!client.Consume(bb)) return false;

            var l = await client.ReceiveLine();
            bb = Encoding.UTF8.GetBytes("\r\n--" + Boundary);

            var EndLength = packet.ContentLength + client.TotalBytesConsumed;

            while (client.TotalBytesConsumed < EndLength && await client.IsConnected()) {
                var Info = new JSON();

                while (!string.IsNullOrEmpty(l = await client.ReceiveLine())) {
                    var i = PropertiesMatcher.Match(l, Info);
                    if (i == null) break;


                }
            }

            return true;
        }
    }
}