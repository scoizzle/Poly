using Polyc;
using Polyc.Lsp;

if (args.Length > 0 && args[0] == "lsp")
    return new LspServer(Console.OpenStandardInput(), Console.OpenStandardOutput()).Run();

if (args.Length > 0 && args[0] == "mcp") {
    await Poly.Mcp.PolyMcpHost.RunAsync(args[1..]);
    return 0;
}

return PolycApp.Run(args, Console.In, Console.IsInputRedirected, Console.Out, Console.Error);