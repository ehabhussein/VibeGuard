using VibeGuard.Content;
using VibeGuard.Content.Indexing;
using VibeGuard.Content.Loading;
using VibeGuard.Content.Services;
using VibeGuard.Content.Validation;
using VibeGuard.Mcp;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Configuration;
using Serilog;
using Serilog.Events;

// Resolve the transport mode before building the host. Precedence:
// 1. Environment variable VIBEGUARD_TRANSPORT ("stdio" or "http")
// 2. appsettings.json "VibeGuard:Transport"
// 3. Default: "stdio"
// Using a lightweight pre-parse so the env var takes effect before any
// host builder touches config.
var transport = Environment.GetEnvironmentVariable("VIBEGUARD_TRANSPORT")
    ?? LoadTransportFromConfig()
    ?? "stdio";

return string.Equals(transport, "http", StringComparison.OrdinalIgnoreCase)
    ? await RunHttpAsync(args).ConfigureAwait(false)
    : await RunStdioAsync(args).ConfigureAwait(false);

// ──────────────────────────────────────────────────────────────────────
// Stdio transport — one process per client, talks over stdin/stdout.
// Default for local developer workstations (Claude Code, Cursor, etc.).
// ──────────────────────────────────────────────────────────────────────
static async Task<int> RunStdioAsync(string[] args)
{
    var builder = Host.CreateApplicationBuilder(args);

    // Stdio is reserved for the MCP protocol. Every log event is routed
    // to stderr so nothing pollutes the MCP wire format on stdout.
    ConfigureLogging(builder.Services, stderrOnly: true);

    RegisterVibeGuardServices(builder.Services, builder.Configuration);

    builder.Services
        .AddMcpServer(opts => opts.ServerInstructions = ServerInstructions.Text)
        .WithStdioServerTransport()
        .WithToolsFromAssembly();

    var host = builder.Build();
    return await StartAndRunAsync(host).ConfigureAwait(false);
}

// ──────────────────────────────────────────────────────────────────────
// HTTP transport — Streamable HTTP, one server serving many clients.
// For teams, CI pipelines, and shared deployments.
// ──────────────────────────────────────────────────────────────────────
static async Task<int> RunHttpAsync(string[] args)
{
    var builder = WebApplication.CreateBuilder(args);

    // HTTP mode: logs go to stdout/stderr normally (Kestrel does not
    // use stdout as a protocol channel).
    ConfigureLogging(builder.Services, stderrOnly: false);

    RegisterVibeGuardServices(builder.Services, builder.Configuration);

    // Resolve the port. Precedence:
    // 1. VIBEGUARD_HTTP_PORT env var
    // 2. appsettings.json "VibeGuard:HttpPort"
    // 3. Default: 3001
    var portStr = Environment.GetEnvironmentVariable("VIBEGUARD_HTTP_PORT")
        ?? builder.Configuration["VibeGuard:HttpPort"];
    var port = int.TryParse(portStr, out var p) ? p : 3001;

    builder.WebHost.UseUrls($"http://*:{port}");

    builder.Services
        .AddMcpServer(opts => opts.ServerInstructions = ServerInstructions.Text)
        .WithHttpTransport()
        .WithToolsFromAssembly();

    var app = builder.Build();

    // Force-load the index before accepting connections.
    if (!TryEagerLoadIndex(app.Services, app.Services.GetRequiredService<ILoggerFactory>()))
    {
        await ((IAsyncDisposable)app).DisposeAsync().ConfigureAwait(false);
        return 1;
    }

    app.MapMcp();

    var logger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("VibeGuard.Startup");
    StartupLogging.HttpListening(logger, port);

    await app.RunAsync().ConfigureAwait(false);
    return 0;
}

// ──────────────────────────────────────────────────────────────────────
// Shared helpers
// ──────────────────────────────────────────────────────────────────────

static void ConfigureLogging(IServiceCollection services, bool stderrOnly)
{
    services.AddSerilog(lc =>
    {
        lc.MinimumLevel.Information()
          .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Warning)
          .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
          .Enrich.FromLogContext();

        if (stderrOnly)
            lc.WriteTo.Console(standardErrorFromLevel: LogEventLevel.Verbose);
        else
            lc.WriteTo.Console();
    });
}

static void RegisterVibeGuardServices(IServiceCollection services, IConfiguration configuration)
{
    // Resolve the archetypes root once at startup. Precedence:
    // 1. Environment variable VIBEGUARD_ARCHETYPES_ROOT (absolute or relative to cwd)
    // 2. appsettings.json "VibeGuard:ArchetypesRoot"
    // 3. "archetypes" next to the executable
    var configured = Environment.GetEnvironmentVariable("VIBEGUARD_ARCHETYPES_ROOT")
        ?? configuration["VibeGuard:ArchetypesRoot"]
        ?? "archetypes";
    var archetypesRoot = Path.IsPathRooted(configured)
        ? configured
        : Path.GetFullPath(configured, AppContext.BaseDirectory);

    var includeDrafts = !string.IsNullOrEmpty(
        Environment.GetEnvironmentVariable("VIBEGUARD_INCLUDE_DRAFTS"));

    var supportedLanguages = ResolveSupportedLanguages(configuration);

    services
        .AddSingleton(supportedLanguages)
        .AddSingleton<IArchetypeRepository>(sp => new FileSystemArchetypeRepository(
            archetypesRoot,
            includeDrafts,
            sp.GetRequiredService<SupportedLanguageSet>()))
        .AddSingleton<OnnxEmbeddingGenerator>(_ => OnnxEmbeddingGenerator.Create())
        .AddSingleton<IEmbeddingGenerator<string, Embedding<float>>>(sp =>
            sp.GetRequiredService<OnnxEmbeddingGenerator>())
        .AddSingleton<IArchetypeIndex>(sp =>
        {
            var repo = sp.GetRequiredService<IArchetypeRepository>();
            var archetypes = repo.LoadAll();
            var generator = sp.GetRequiredService<IEmbeddingGenerator<string, Embedding<float>>>();

            var keywordIndex = KeywordArchetypeIndex.Build(archetypes);
            var embeddingIndex = EmbeddingArchetypeIndex.BuildAsync(archetypes, generator)
                .GetAwaiter().GetResult();

            return new HybridSearchService(keywordIndex, embeddingIndex, generator);
        })
        .AddSingleton<IPrepService, PrepService>()
        .AddSingleton<IConsultationService, ConsultationService>();
}

static SupportedLanguageSet ResolveSupportedLanguages(IConfiguration configuration)
{
    var fromEnv = Environment.GetEnvironmentVariable("VIBEGUARD_SUPPORTED_LANGUAGES");
    if (!string.IsNullOrWhiteSpace(fromEnv))
    {
        var entries = fromEnv.Split(
            ',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return new SupportedLanguageSet(entries);
    }

    var fromConfig = configuration.GetSection("VibeGuard:SupportedLanguages").Get<string[]>();
    if (fromConfig is { Length: > 0 })
    {
        return new SupportedLanguageSet(fromConfig);
    }

    return SupportedLanguageSet.Default();
}

static string? LoadTransportFromConfig()
{
    var config = new ConfigurationBuilder()
        .SetBasePath(AppContext.BaseDirectory)
        .AddJsonFile("appsettings.json", optional: true)
        .Build();
    return config["VibeGuard:Transport"];
}

static async Task<int> StartAndRunAsync(IHost host)
{
    if (!TryEagerLoadIndex(host.Services, host.Services.GetRequiredService<ILoggerFactory>()))
    {
        await ((IAsyncDisposable)host).DisposeAsync().ConfigureAwait(false);
        return 1;
    }

    await host.RunAsync().ConfigureAwait(false);
    return 0;
}

static bool TryEagerLoadIndex(IServiceProvider services, ILoggerFactory loggerFactory)
{
    try
    {
        _ = services.GetRequiredService<IArchetypeIndex>();
        return true;
    }
    catch (Exception ex) when (
        ex is IOException
        or UnauthorizedAccessException
        or ArgumentException
        or ArchetypeLoadException
        or FrontmatterParseException
        or ArchetypeValidationException)
    {
        var logger = loggerFactory.CreateLogger("VibeGuard.Startup");
        StartupLogging.CorpusLoadFailed(logger, "archetypes", ex);
        return false;
    }
}
