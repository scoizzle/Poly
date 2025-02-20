using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Primitives;
using Poly.Management.Configuration.Abstractions;

namespace Poly.Management.Configuration;

class DistributedConfigurationProvider(IDistributedConfigurationClient hubClient) : IConfigurationProvider
{
    public IEnumerable<string> GetChildKeys(IEnumerable<string> earlierKeys, string? parentPath)
    {
        throw new NotImplementedException();
    }

    public IChangeToken GetReloadToken()
    {
        throw new NotImplementedException();
    }

    public void Load()
    {
    }

    public void Set(string key, string? value)
    {
        throw new NotImplementedException();
    }

    public bool TryGet(string key, out string? value)
    {
        throw new NotImplementedException();
    }
}