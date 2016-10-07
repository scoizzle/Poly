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

        public bool Send(byte[] bytes) {
            return Send(bytes, 0, bytes.Length);
        }
    
        public bool Send(byte[] bytes, int index, int len) {
            try {
                Stream.Write(bytes, index, len);
                Stream.Flush();
            }
            catch {
                return false;
            }
            return true;
        }

        public bool Send(params byte[][] byteGroup) {
            try {
                foreach (var bytes in byteGroup) {
                    if (bytes != null)
                        Stream.Write(bytes, 0, bytes.Length);                   
                }

                Stream.Flush();
            }
            catch {
                return false;
            }
            return true;
        }

        public bool Receive(Stream storage, long length) {
            try {
                while (length > 0) {
                    if (Available == 0)
                        UpdateBuffer();

                    var Len = (int)(Math.Min(length, Available));
                    storage.Write(Buffer, 0, Len);

                    length -= Len;
                    Consume(Len);
                }
                storage.Flush();
            }
            catch (Exception Error) {
                App.Log.Error(Error.ToString());
                return false;
            }
            return true;
        }

        public bool ReceiveUntil(Stream storage, byte[] chain) {
            try {
                if (Available == 0)
                    UpdateBuffer();

                int i = 0,
                    b = -1,
                    startPosition = Position,
                    chainLen = chain.Length;

                while ((b = CurrentByte) >= 0) {
                    Consume(1);

                    if (b == chain[i]) {
                        if (++i == chainLen) {
                            storage.Write(Buffer, startPosition, Position - startPosition - chainLen);
                            storage.Flush();
                            return true;
                        }
                    }
                    else {             
                        i = 0;
                    }
                    
                    if (Available == 0) {
                        storage.Write(Buffer, startPosition, Length - startPosition);
                        storage.Flush();

                        startPosition = 0;
                        UpdateBuffer();
                    }
                }
            }
            catch (Exception Error) {
                App.Log.Error(Error.ToString());
            }
            return false;
        }

        public bool Consume(byte[] chain) {
            try {
                if (Available == 0)
                    UpdateBuffer();

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

        public void UpdateBuffer() {
            try {
                Length = Stream.Read(Buffer, 0, MaxBufferLength);
                Position = 0;
            }
            catch {
                Position = Length = 0;
            }
        }

        public void ResetBuffer() {
            Position = Length = 0;
            Buffer = new byte[MaxBufferLength];
        }
        
        private void Consume(int len) {
            Position += len;
            TotalBytesConsumed += len;
        }
    }
}
