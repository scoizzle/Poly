namespace Poly.Net.Http.V2_ {
    //public class Frame {
    //    public virtual bool Send(Client client) {
    //        byte[] headers = new byte[9];

    //        var len = Length;
    //        var id = StreamId;

    //        headers[0] = (byte)(len >> 16);
    //        headers[1] = (byte)(len >> 8);
    //        headers[2] = (byte)(len);

    //        headers[3] = (byte)(Type);
    //        headers[4] = (byte)(Flags);

    //        headers[5] = (byte)(id >> 24);
    //        headers[6] = (byte)(id >> 16);
    //        headers[7] = (byte)(id >> 8);
    //        headers[8] = (byte)(id);

    //        if (!client.Send(headers))
    //            return false;

    //        if (Length > 0)
    //            return client.Send(Payload);

    //        return true;
    //    }

    //    public virtual bool Receive(Client client) {
    //        byte[] headers = new byte[9];

    //        if (client.Receive(headers)) {
    //            Length =
    //                headers[0] << 16 +
    //                headers[1] << 8  +
    //                headers[2];

    //            Type = (Types)headers[3];
    //            Flags = (Flag)headers[4];

    //            StreamId =
    //                headers[5] << 24 +
    //                headers[6] << 16 +
    //                headers[7] << 8  +
    //                headers[8];

    //            if (Length > StreamSettings.MaxFrameSize)
    //                return false;

    //            return client.Receive(Payload = new byte[Length]);
    //        }

    //        return false;
    //    }
    //}
}