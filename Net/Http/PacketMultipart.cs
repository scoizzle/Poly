﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Poly.Net.Http {
    using Tcp;
    using Data;

    public class MultipartHandler {
		static readonly byte[] doubleDash = Encoding.UTF8.GetBytes("--");

        public Client Client { get; private set; }
        public Packet Packet { get; private set; }
        public BufferedStreamer Stream { get; private set; }

        public MultipartHandler(Client client, Packet packet) {
            Client = client;
            Packet = packet;
            Stream = client.GetStreamer();
        }

        public bool Receive() {
            var client = Client;
            var stream = Stream;
            var temp = new MemoryStream();
			var endLength = stream.TotalBytesConsumed + Packet.ContentLength;
			var boundary = Encoding.UTF8.GetBytes("--" + Packet.Boundary);

            var line = string.Empty;

			if (!stream.Consume(boundary))
                return false;
            else
                line = client.ReceiveLine();

            boundary = Encoding.UTF8.GetBytes("\r\n--" + Packet.Boundary);
			while (stream.TotalBytesConsumed < endLength && Client.Connected) {
                var postInfo = new jsObject();
                var isFile = false;

                while (!string.IsNullOrEmpty(line = client.ReceiveLine())) {
                    if (line.StartsWith("Content-Disposition", StringComparison.Ordinal)) {
                        var Results = Packet.MultipartHeaderPropertiesMatcher.MatchAll(line.Substring("form-data"));

                        postInfo.Set("Name", Results["name"]);

                        if (Results.ContainsKey("filename")) {
                            postInfo.Set("FileName", Results["filename"]);
                            isFile = true;
                        }
                    }
                    else if (line.StartsWith("Content-Type", StringComparison.Ordinal)) {
                        postInfo.Set("ContentType", line.Substring(": "));
                    }
                }

                if (!postInfo.ContainsKey("Name"))
                    return false;

                if (isFile) {
                    var f = new TempFileUpload(postInfo.Get<string>("FileName"));

                    using (var fstream = f.GetWriteStream())
                        stream.ReceiveUntil(fstream, boundary);

                    f.Info.Refresh();

                    postInfo.Set("Content", f);
                }
                else {
                    temp = new MemoryStream();
					if (!stream.ReceiveUntil(temp, boundary))
                        return false;

                    postInfo.Set("Content", Encoding.UTF8.GetString(temp.ToArray()));
                }

                Packet.Post.Set(postInfo.Get<string>("Name"), postInfo);

				if (stream.Consume(doubleDash))
                    return true;
                else if (!string.IsNullOrEmpty(line = client.ReceiveLine()))
                    return false;
            }
            return false;
        }
    }
}