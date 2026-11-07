using Haulout.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

var builder = Host.CreateApplicationBuilder(args);

// Stdout belongs to the MCP transport. Every log line goes to stderr.
builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);

builder.Services.AddSingleton(_ => new GameSession(
    Haulout.Sim.Play.Game.NewFirstFault(),
    Environment.GetEnvironmentVariable("HAULOUT_TRACE")));

builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithTools<HaulTools>();

await builder.Build().RunAsync();
