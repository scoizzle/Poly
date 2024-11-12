namespace Poly.Data
{
    public static partial class DynamicBufferExtensions
    {
        public static async ValueTask<bool> ReadAsync(
            this Stream stream,
            DynamicBuffer<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            if (!buffer.EnsureWriteableCapacity())
                return false;

            try
            {
                var read = await stream.ReadAsync(buffer.WriteableMemory, cancellationToken);

                if (read > 0)
                {
                    buffer.Commit(read);
                    return true;
                }
            }
            catch (Exception error)
            {
                Log.Error(error);
            }

            return false;
        }

        public static async ValueTask<bool> ReadAsync(
            this Stream stream,
            DynamicBuffer<byte> buffer,
            int count,
            CancellationToken cancellationToken = default)
        {
            if (!buffer.EnsureWriteableCapacity(count))
                return false;

            try
            {
                do
                {
                    var read = await stream.ReadAsync(buffer.WriteableMemory[..count], cancellationToken);

                    if (read == 0 || !buffer.Commit(read))
                        return false;

                    if ((count -= read) == 0)
                        return true;
                }
                while (!cancellationToken.IsCancellationRequested);
            }
            catch (Exception error)
            { Log.Error(error); }

            return false;
        }

        public static async ValueTask<bool> WriteAsync(
            this Stream stream,
            DynamicBuffer<byte> buffer,
            CancellationToken cancellationToken = default)
        {
            try
            {
                await stream.WriteAsync(buffer.ReadableMemory, cancellationToken);
                buffer.Reset();
                return true;
            }
            catch (Exception error)
            { Log.Error(error); }

            return false;
        }

        public static async ValueTask<bool> WriteAsync(
            this Stream stream,
            DynamicBuffer<byte> buffer,
            int count,
            CancellationToken cancellationToken = default)
        {
            if (buffer.Count < count)
                return false;

            try
            {
                await stream.WriteAsync(buffer.ReadableMemory[..count], cancellationToken);
                return buffer.Consume(count);
            }
            catch (Exception error)
            { Log.Error(error); }

            return false;
        }

        public static async ValueTask<bool> CopyAsync(
            this DynamicBuffer<byte> buffer,
            Stream In,
            Stream Out,
            CancellationToken cancellationToken = default
            )
        {
            do
            {
                if (!await ReadAsync(In, buffer, cancellationToken))
                    return true;

                if (!await WriteAsync(Out, buffer, cancellationToken))
                    break;
            }
            while (!cancellationToken.IsCancellationRequested);
            return false;
        }

        public static async ValueTask<bool> CopyAsync(
            this DynamicBuffer<byte> buffer,
            Stream In,
            Stream Out,
            long length,
            CancellationToken cancellationToken = default
            )
        {
            do
            {
                var to_read = (int)Math.Min(buffer.UnallocatedSize, length);

                if (!await ReadAsync(In, buffer, to_read, cancellationToken))
                    return true;

                if (!await WriteAsync(Out, buffer, cancellationToken))
                    break;

                if ((length -= to_read) == 0)
                    return true;
            }
            while (!cancellationToken.IsCancellationRequested);
            return false;
        }
    }
}