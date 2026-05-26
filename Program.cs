using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.Server;
using OpenClaudeMcp.Configuration;
using OpenClaudeMcp.Logging;
using OpenClaudeMcp.Runner;

var builder = Host.CreateApplicationBuilder(args);

builder.Configuration
    .SetBasePath(AppContext.BaseDirectory)
    .AddJsonFile("appsettings.json", optional: true, reloadOnChange: false);

var options = new OpenClaudeOptions();
builder.Configuration.GetSection("OpenClaude").Bind(options);

FileLog.Initialize(options.LogFilePath);
FileLog.Info("OpenClaudeMcp starting");

builder.Services.AddSingleton(options);
builder.Services.AddSingleton<OpenClaudeRunner>();

// MCP framework: stdio transport, tools auto-discovered from this assembly.
builder.Services
    .AddMcpServer()
    .WithStdioServerTransport()
    .WithToolsFromAssembly();

// Drop the default console logger — anything writing to stdout corrupts the MCP protocol.
builder.Logging.ClearProviders();

try
{
    await builder.Build().RunAsync();
}
catch (Exception ex)
{
    FileLog.Error("OpenClaudeMcp crashed", ex);
    throw;
}
