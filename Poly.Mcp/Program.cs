using Poly.Mcp;

// One-release forwarder: the host now lives in PolyMcpHost, which `polyc mcp` also runs.
await PolyMcpHost.RunAsync(args);