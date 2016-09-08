using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace Poly.Net.Tcp {
    public class BufferedStreamer {
        byte[] Buffer;
        int MaxBufferLength;

        public NetworkStream Stream;        
        public int Position { get; private set; }
        public int Length { get; private set; }      
        public int Available { get { return Length - Position; } }  

        public int CurrentByte {
            get {
                if (Position >= 0 && Position < Length) return Buffer[Position];
                return -1;
            }
        }

        public long TotalBytesConsumed { get; private set; }

        public int BufferSize
        {
            get { return MaxBufferLength; }
            set
            {
                MaxBufferLength = value;
                Array.Resize(ref Buffer, MaxBufferLength);
            }
        }

        public BufferedStreamer(NetworkStream stream) {
            Stream = stream;
            MaxBufferLength = 4096;

            Buffer = new byte[MaxBufferLength];
            Position = Length = 0;
        }
        
        public BufferedStreamer(NetworkStream stream, int BufferSize) {
            Stream = stream;
            MaxBufferLength = BufferSize;

            Buffer = new byte[MaxBufferLength];
            Position = Length = 0;
        }

        public async Task<bool> Send(byte[] bytes) {
            return await Send(bytes, 0, bytes.Length);
        }

        public async Task<bool> Send(byte[] bytes, int index, int len) {
            try {
                await Stream.WriteAsync(bytes, index, len);
                await Stream.FlushAsync();
            }
            catch { return false; }
            return true;
        }

        public async Task<bool> Send(params byte[][] byteGroup) {
            try {
                foreach (var bytes in byteGroup)
                    if (bytes != null) 
                        await Stream.WriteAsync(bytes, 0, bytes.Length);

                await Stream.FlushAsync();
            }
            catch { return false; }
            return true;
        }

        public async Task<bool> Receive(Stream storage, long length) {
            try {
                while (length > 0) {
                    if (Available == 0)
                        await UpdateBuffer();

                    var Len = (int)(Math.Min(length, Available));
                    await storage.WriteAsync(Buffer, 0, Len);

                    length -= Len;
                    Consume(Len);
                }
                await storage.FlushAsync();
            }
            catch (Exception Error) {
                App.Log.Error(Error.ToString());
                return false;
            }
            return true;
        }

        public async Task<bool> ReceiveUntil(Stream storage, byte[] chain) {
            try {
                if (Available == 0)
                    await UpdateBuffer();

                int i = 0,
                    b = -1,
                    startPosition = Position,
                    chainLen = chain.Length;

                while ((b = CurrentByte) >= 0) {
                    Consume(1);

                    if (b == chain[i]) {
                        if (++i == chainLen) {
                            await storage.WriteAsync(Buffer, startPosition, Position - startPosition - chainLen);
                            await storage.FlushAsync();
                            return true;
                        }
                    }
                    else {             
                        i = 0;
                    }
                    
                    if (Available == 0) {
                        await storage.WriteAsync(Buffer, startPosition, Length - startPosition);
                        await storage.FlushAsync();

                        startPosition = 0;
                        await UpdateBuffer();
                    }
                }
            }
            catch (Exception Error) {
                App.Log.Error(Error.ToString());
            }
            return false;
        }

        public async Task<bool> Consume(byte[] chain) {
            try {
                if (Available == 0)
                    await UpdateBuffer();

                var Len = chain.Length;

                for (var i = 0; i < Len; i++) {
                    if (Buffer[Position + i] != chain[i])
                        return false;
                }

                Consume(Len);
            }
            catch { return false; }
            return true;
        }

        private async Task UpdateBuffer() {
            try {
                Length = await Stream.ReadAsync(Buffer, 0, MaxBufferLength);
                Position = 0;
            }
            catch {
                Position = Length = 0;
            }
        }

        public void ResetBuffer() {
            Position = Length = 0;
            Buffer = new byte[BufferSize];
        }

		public void PollBuffer() {
			if (Available == 0)
				UpdateBuffer().Wait();
		}
        
        private void Consume(int len) {
            Position += len;
            TotalBytesConsumed += len;
        }
    }
}
