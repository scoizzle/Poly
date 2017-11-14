using System;
using System.Collections.Generic;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace Poly.Data {
    public class MemoryBufferedStream : Stream {
        public const int DefaultBufferSize = 1024 * 16;

        public bool AutoFlush { get; set; }
        public Stream Stream { get; protected set; }

        public override bool CanRead => Stream.CanRead;
        public override bool CanWrite => Stream.CanWrite;
        public override bool CanSeek => Stream.CanSeek;
        public override long Length => Stream.Length;

        public override long Position {
            get => Stream.Position;
            set => Stream.Position = value;
        }

        protected MemoryBuffer In, Out;

        public MemoryBufferedStream(Stream stream, int in_buffer_size = DefaultBufferSize, int out_buffer_size = DefaultBufferSize) {
            Stream = stream;

            In = new MemoryBuffer(in_buffer_size);
            Out = new MemoryBuffer(out_buffer_size);
        }

        public override long Seek(long offset, SeekOrigin origin) =>
            Stream.Seek(offset, origin);

        public override void SetLength(long value) =>
            Stream.SetLength(value);

        public override void Flush() =>
            Out.Write(Stream);

        public bool Write() => 
            Out.Write(Stream);

        public Task<bool> WriteAsync() => 
            Out.WriteAsync(Stream);

        public bool Write(Stream Content) {
            var length = Content.Length;
            while (length > 0) {
                var chunk = Out.Remaining > length ?
                    length :
                    Out.Remaining;

                if (!Out.Read(Content, (int)chunk)) {
                    return false;
                }

                length -= chunk;

                if (Out.Remaining == 0 || AutoFlush == true)
                    if (!Out.Write(Stream)) {
                        return false;
                    }
            }

            return true;
        }

        public async Task<bool> WriteAsync(Stream Content) {
            var length = Content.Length;
            while (length > 0) {
                var chunk = Out.Remaining > length ?
                    length :
                    Out.Remaining;

                if (!await Out.ReadAsync(Content, (int)chunk)) {
                    return false;
                }

                length -= chunk;

                if (Out.Remaining == 0 || AutoFlush == true)
                    if (!await Out.WriteAsync(Stream)) {
                        return false;
                    }
            }

            return true;
        }

        public override void Write(byte[] buffer, int offset, int count) {
            Write_Internal(buffer, offset, count);
        }

        public bool Write_Internal(byte[] content, int index = 0, int length = -1) {
            if (length == -1)
                length = content.Length;

            while (length > 0) {
                var chunk = Out.Remaining > length ?
                    length :
                    Out.Remaining;

                if (!Out.Read(content, index, chunk)) {
                    return false;
                }

                index += chunk;
                length -= chunk;

                if (Out.Remaining == 0 || AutoFlush == true)
                    if (!Out.Write(Stream)) {
                        return false;
                    }
            }

            return true;
        }

        new public async Task<bool> WriteAsync(byte[] content, int index = 0, int length = -1) {
            if (length == -1)
                length = content.Length;

            while (length > 0) {
                var chunk = Out.Remaining > length ?
                    length :
                    Out.Remaining;

                if (!Out.Read(content, index, chunk)) {
                    return false;
                }

                index += chunk;
                length -= chunk;

                if (Out.Remaining == 0 || AutoFlush == true)
                    if (!await Out.WriteAsync(Stream)) {
                        return false;
                    }
            }

            return true;
        }

        public bool Write(string str) {
            return Write_Internal(App.Encoding.GetBytes(str));
        }

        public Task<bool> WriteAsync(string str) {
            return WriteAsync(App.Encoding.GetBytes(str));
        }

        public bool WriteLine(string line) {
            return Write(string.Concat(line, App.NewLine));
        }

        public Task<bool> WriteLineAsync(string line) {
            return WriteAsync(string.Concat(line, App.NewLine));
        }

        public bool Write(MemoryBuffer buffer) {
            return Write_Internal(buffer.Array, buffer.Position, buffer.Available);
        }

        public bool Read() => 
            In.Read(Stream);

        public Task<bool> ReadAsync() => 
            In.ReadAsync(Stream);

        public bool Read(Stream storage, long length) {
            if (length == 0) return true;

            if (In.Available == 0) {
                var recv = Read();

                if (!recv)
                    return false;
            }

            do {
                var chunk = In.Available > length ?
                    length :
                    In.Available;

                var write = In.Write(storage, (int)chunk);
                if (!write)
                    return false;

                length -= chunk;

                var recv = Read();
                if (!recv)
                    return false;
            }
            while (length > 0);

            return true;
        }

        public async Task<bool> ReadAsync(Stream storage, long length) {
            if (length == 0) return true;

            if (In.Available == 0) {
                var recv = ReadAsync();

                if (!await recv)
                    return false;
            }

            do {
                var chunk = In.Available > length ?
                    length :
                    In.Available;

                var write = In.WriteAsync(storage, (int)chunk);
                if (!await write)
                    return false;

                length -= chunk;

                var recv = ReadAsync();
                if (!await recv)
                    return false;
            }
            while (length > 0);

            return true;
        }

        public override int Read(byte[] buffer, int offset, int count) {
            var length = In.Length;

            if (Read_Internal(buffer, offset, count))
                return In.Length - length;

            return -1;
        }

        public bool Read_Internal(byte[] bytes, int index = 0, int length = -1) {
            if (length == -1)
                length = bytes.Length;

            if (In.Available == 0) {
                var recv = Read();

                if (!recv)
                    return false;
            }

            do {
                var chunk = In.Available > length ?
                    length :
                    In.Available;

                var write = In.Write(bytes, index, chunk);
                if (!write)
                    return false;

                length -= chunk;
                index += chunk;

                var recv = Read();
                if (!recv)
                    return false;
            }
            while (length > 0);

            return true;
        }

        new public async Task<bool> ReadAsync(byte[] bytes, int index = 0, int length = -1) {
            if (length == -1)
                length = bytes.Length;

            if (In.Available == 0) {
                var recv = ReadAsync();

                if (!await recv)
                    return false;
            }

            do {
                var chunk = In.Available > length ?
                    length :
                    In.Available;

                var write = In.Write(bytes, index, chunk);
                if (!write)
                    return false;

                length -= chunk;
                index += chunk;

                var recv = ReadAsync();
                if (!await recv)
                    return false;
            }
            while (length > 0);

            return true;
        }

        public bool ReadUntil(Stream storage, byte[] chain, long maxLength = long.MaxValue) {
            if (In.Available == 0) {
                var recv = Read();

                if (!recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;

            var arrayStart = buffer.Position;
            var arrayIndex = arrayStart;
            var arrayLength = buffer.Length;

            var chainIndex = 0;
            var chainLength = chain.Length;

            var lastWasPartial = false;

            do {
                var compare = array.CompareSubByteArray(arrayIndex, chain, chainIndex, chainLength);

                if (compare == true) { // Found chain
                    buffer.Write(storage, arrayIndex - arrayStart);
                    buffer.Consume(chainLength);
                    return true;
                }
                else
                if (compare == false) { // Partial Match
                    if (lastWasPartial) {
                        arrayIndex++;
                    }
                    else {
                        buffer.Write(storage, arrayIndex - arrayStart);
                        buffer.Rebase();

                        Read();

                        arrayIndex = 0;
                        arrayStart = buffer.Position;
                        arrayLength = buffer.Length;

                        lastWasPartial = true;
                    }
                }
                else
                if (compare == null) // No match.
                    arrayIndex++;

                if (arrayIndex == arrayLength) {
                    buffer.Write(storage);
                    Read();

                    arrayIndex = 0;
                    arrayStart = arrayIndex;
                    arrayLength = buffer.Length;
                }
            }
            while (--maxLength > 0);

            return false;
        }

        public async Task<bool> ReadUntilAsync(Stream storage, byte[] chain, long maxLength = long.MaxValue) {
            if (In.Available == 0) {
                var recv = ReadAsync();

                if (!await recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;

            var arrayStart = buffer.Position;
            var arrayIndex = arrayStart;
            var arrayLength = buffer.Length;

            var chainIndex = 0;
            var chainLength = chain.Length;

            var lastWasPartial = false;

            do {
                var compare = array.CompareSubByteArray(arrayIndex, chain, chainIndex, chainLength);

                if (compare == true) { // Found chain
                    await buffer.WriteAsync(storage, arrayIndex - arrayStart);
                    buffer.Consume(chainLength);
                    return true;
                }
                else
                if (compare == false) { // Partial Match
                    if (lastWasPartial) {
                        arrayIndex++;
                    }
                    else {
                        await buffer.WriteAsync(storage, arrayIndex - arrayStart);
                        buffer.Rebase();

                        await ReadAsync();

                        arrayIndex = 0;
                        arrayStart = buffer.Position;
                        arrayLength = buffer.Length;

                        lastWasPartial = true;
                    }
                }
                else
                if (compare == null) // No match.
                    arrayIndex++;

                if (arrayIndex == arrayLength) {
                    await buffer.WriteAsync(storage);
                    await ReadAsync();

                    arrayIndex = 0;
                    arrayStart = arrayIndex;
                    arrayLength = buffer.Length;
                }
            }
            while (--maxLength > 0);

            return false;
        }

        public bool ReadUntilConstrained(Stream storage, byte[] chain, int maxLength = int.MaxValue) {
            if (In.Available == 0) {
                var recv = Read();

                if (!recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;
            var position = buffer.Position;

            var i = array.FindSubByteArray(position, chain);

            if (i == -1 && buffer.Remaining > 0) {
                buffer.Rebase();

                Read();

                position = buffer.Position;
                i = array.FindSubByteArray(position, chain);
            }

            if (i == -1 || i - position > maxLength)
                return false;

            if (!In.Write(storage, i - position))
                return false;

            In.Consume(chain.Length);
            return true;
        }

        public async Task<bool> ReadUntilConstrainedAsync(Stream storage, byte[] chain, int maxLength = int.MaxValue) {
            if (In.Available == 0) {
                var recv = ReadAsync();

                if (!await recv)
                    return false;
            }

            var buffer = In;
            var array = buffer.Array;
            var position = buffer.Position;

            var i = array.FindSubByteArray(position, chain);

            if (i == -1 && buffer.Remaining > 0) {
                buffer.Rebase();

                await ReadAsync();

                position = buffer.Position;
                i = array.FindSubByteArray(position, chain);
            }

            if (i == -1 || i - position > maxLength)
                return false;

            if (!await In.WriteAsync(storage, i - position))
                return false;

            In.Consume(chain.Length);
            return true;
        }

        public string ReadString(int ByteLength) {
            var Storage = new MemoryStream(ByteLength);

            if (Read(Storage, ByteLength))
                return App.Encoding.GetString(Storage.ToArray());

            return null;
        }

        public async Task<string> ReadStringAsync(int ByteLength) {
            var Storage = new MemoryStream(ByteLength);

            if (await ReadAsync(Storage, ByteLength))
                return App.Encoding.GetString(Storage.ToArray());

            return null;
        }

        public string ReadStringUntil(byte[] chain) {
            var buffer = new MemoryStream();

            if (ReadUntil(buffer, chain))
                return App.Encoding.GetString(buffer.ToArray());

            return null;
        }

        public async Task<string> ReadStringUntilAsync(byte[] chain) {
            var buffer = new MemoryStream();

            if (await ReadUntilAsync(buffer, chain))
                return App.Encoding.GetString(buffer.ToArray());

            return null;
        }

        public string ReadStringUntilConstrained(byte[] chain) {
            if (In.Available == 0) {
                var recv = Read();

                if (!recv)
                    return null;
            }

            var buffer = In;
            var array = buffer.Array;
            var position = buffer.Position;

            var i = array.FindSubByteArray(position, chain);

            if (i == -1)
                return null;

            var length = i - position;
            var result = App.Encoding.GetString(array, position, length);

            In.Consume(length + chain.Length);
            return result;
        }

        public async Task<string> ReadStringUntilConstrainedAsync(byte[] chain) {
            if (In.Available == 0) {
                var recv = ReadAsync();

                if (!await recv)
                    return null;
            }

            var buffer = In;
            var array = buffer.Array;
            var position = buffer.Position;

            var i = array.FindSubByteArray(position, chain);

            if (i == -1)
                return null;

            var length = i - position;
            var result = App.Encoding.GetString(array, position, length);

            In.Consume(length + chain.Length);
            return result;
        }

        public string ReadLine() {
            return ReadStringUntil(App.NewLineBytes);
        }

        public Task<string> ReadLineAsync() {
            return ReadStringUntilAsync(App.NewLineBytes);
        }

        public string ReadLineConstrained() {
            return ReadStringUntilConstrained(App.NewLineBytes);
        }

        public Task<string> ReadLineConstrainedAsync() {
            return ReadStringUntilConstrainedAsync(App.NewLineBytes);
        }
    }
}
