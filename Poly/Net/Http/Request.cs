namespace Poly.Net.Http
{
    public struct RequestHeaderCollection { 

    }
    
    public interface RequestInterface {
        StringView Method { get; set; }

        StringView Path { get; set; }

        StringView Version { get; set; }

        StringView Authority { get; set; }

        RequestHeaderCollection Headers { get; }
    }
}