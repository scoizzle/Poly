namespace Poly.Net.Http {

    public class Form {
        //    public KeyValueCollection<string> Values;
        //    public KeyValueCollection<TempFileUpload> Files;

        //    public const string MultipartContentType = "multipart/form-data; boundary=";
        //    public const string UrlEncodedContentType = "application/x-www-form-urlencoded";

        //    public static bool Send(Response response, Form form) {
        //        return false;
        //    }

        //    public static bool Receive(Request request, out Form form) =>
        //        Receive_Multipart(request, out form) || Receive_UrlEncoded(request, out form);

        //    static bool Receive_Multipart(Request request, out Form form) {
        //        var content_type = request.ContentType;
        //        var content_length = request.ContentLength;

        //        if (content_type.Compare(0, MultipartContentType, 0, MultipartContentType.Length)) {
        //            var boundary = "--" + content_type.Substring(MultipartContentType.Length);
        //            var boundary_bytes = App.Encoding.GetBytes(boundary);
        //            var double_dash_bytes = App.Encoding.GetBytes("--\r\n");

        //            //var buffer = new MemoryBuffer(Tcp.Client.DefaultBufferSize);
        //            //var stream = request.Body;

        //            //form = new Form();

        //            //while (buffer.TotalConsumed < content_length) {
        //            //    buffer.Read(stream);

        //            //    if (!buffer.Consume(boundary_bytes))
        //            //        return false;

        //            //    if (buffer.Consume(double_dash_bytes))
        //            //        break;

        //            //}
        //        }

        //        form = null;
        //        return false;
        //    }

        //    static bool Receive_UrlEncoded(Request request, out Form form) {
        //        if (request.ContentType.Compare(UrlEncodedContentType)) {
        //        }

        //        form = null;
        //        return false;
        //    }

        //}

        //public static class FormExtensions {
        //    public static bool IsFormSubmission(this Request request) {
        //        var content_type = request.ContentType;

        //        return content_type.Compare(Form.UrlEncodedContentType) ||
        //               content_type.Compare(0, Form.MultipartContentType, 0, Form.MultipartContentType.Length);
        //    }
        //}
    }
}