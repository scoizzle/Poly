using System;
using System.Net;
using System.Threading.Tasks;

using Xunit;
using Xunit.Abstractions;

namespace Poly.Net.Tcp
{
    public class ClientServerTests
    {
        readonly TcpServer server;
        readonly TcpClient client;
        readonly ITestOutputHelper output;

        readonly IPEndPoint TestEndPoint = new(IPAddress.Loopback, 5000);
        readonly byte[] TestMessage = new byte[] { 0x13, 0x37, 0xDE, 0xAD, 0xBE, 0xEF };

        public ClientServerTests(ITestOutputHelper outputHelper)
        {
            server = new TcpServer(TestEndPoint) { OnClientConnect = OnClientConnect };
            client = new TcpClient();
            output = outputHelper;
        }

        private async void OnClientConnect(TcpClient remoteClient)
        {
            output.WriteLine("Client connected {0}", remoteClient.RemoteIPEndPoint);

            while (remoteClient.Connected)
            {
                await remoteClient.DataAvailableAsync();

                await remoteClient.WriteAsync(remoteClient.In.ReadableMemory);
                remoteClient.In.Consume(remoteClient.In.Count);

                await remoteClient.FlushAsync();
            }
        }

        [Fact]
        public async Task ConnectReadWrite()
        {
            var listening = server.Start();
            Assert.True(listening, $"Server failed to begin listening on {TestEndPoint}");

            var connected = await client.Connect(TestEndPoint);
            Assert.True(connected, "Client failed to connect to server.");

            await client.WriteAsync(TestMessage);
            await client.FlushAsync();

            await client.DataAvailableAsync(TestMessage.Length);

            Assert.True(client.In.ReadableSpan.SequenceCompareTo(TestMessage) == 0);

            client.Close();
            server.Stop();
        }
    }
}