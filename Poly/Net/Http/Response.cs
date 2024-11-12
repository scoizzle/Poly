namespace Poly.Net.Http
{
    public struct ResponseHeaderCollection { 

    }
    
    public interface ResponseInterface {
        StringView Version { get; set; }
        Status Status { get; set; }

        ResponseHeaderCollection Headers { get; }
    }
}