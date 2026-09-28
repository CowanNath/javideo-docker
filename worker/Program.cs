using System.Data;
using Dapper;
using Javideo.Worker.Db;
using Javideo.Worker.Endpoints;
using Javideo.Worker.Magnet;
using Javideo.Worker.Services;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http.Features;
using Serilog;

// ---- Logging ----
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console()
    .CreateLogger();

// Dapper type handler: SQL TEXT <-> List<string> (JSON array).
SqlMapper.AddTypeHandler(new StringListHandler());

try
{
    Log.Information("Javideo Worker starting...");

    var builder = WebApplication.CreateBuilder(args);
    builder.Host.UseSerilog();

    // ---- CORS — only the app's own origins ----
    // Same-origin in the Docker build (the worker serves the SPA itself), so
    // CORS only matters for split deployments: the Vite dev server is always
    // allowed, extra origins via Javideo__CorsOrigins (comma-separated).
    var corsOrigins = new List<string> { "http://localhost:1420", "http://127.0.0.1:1420" };
    var extraOrigins = builder.Configuration["Javideo:CorsOrigins"];
    if (!string.IsNullOrWhiteSpace(extraOrigins))
        corsOrigins.AddRange(extraOrigins.Split(',',
            StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
    builder.Services.AddCors(o => o.AddDefaultPolicy(p =>
        p.WithOrigins(corsOrigins.ToArray()).AllowAnyMethod().AllowAnyHeader()));

    // The backup endpoint has its own request size limit. Multipart parsing
    // also has a separate, lower default limit for each uploaded file.
    builder.Services.Configure<FormOptions>(o =>
        o.MultipartBodyLengthLimit = BackupEndpoints.MaxUploadBytes);

    // ---- Singletons ----
    builder.Services.AddSingleton<DbConnectionFactory>();
    builder.Services.AddSingleton<SettingsService>();
    builder.Services.AddSingleton<MetaTubeClient>();
    builder.Services.AddHttpClient<ImageWriter>();
    builder.Services.AddSingleton<MagnetService>();
    builder.Services.AddSingleton<Scanner>();
    builder.Services.AddSingleton<LibraryService>();
    builder.Services.AddSingleton<NfoWriter>();
    builder.Services.AddSingleton<IngestService>();
    builder.Services.AddSingleton<TrailerClient>();
    builder.Services.AddSingleton<BackupService>();
    builder.Services.AddHttpClient<AvatarService>();
    builder.Services.AddHttpClient<PreviewImageService>();
    builder.Services.AddSingleton<TranslationService>();

    // Force camelCase JSON for ALL endpoints (Dapper returns PascalCase property
    // names on anonymous/record results; without this the frontend's camelCase
    // types don't bind, causing the favorites "undefined" bug).
    builder.Services.ConfigureHttpJsonOptions(o =>
    {
        o.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
        o.SerializerOptions.PropertyNameCaseInsensitive = true;
    });

    // Docker: the runtime image provides ASPNETCORE_URLS (we ship
    // http://+:8080) — honor it and bind all interfaces. Bare local run with
    // no URLs configured keeps the sidecar behavior: loopback + OS-assigned
    // port (reported via the stdout handshake below).
    if (string.IsNullOrWhiteSpace(builder.Configuration["urls"]))
    {
        builder.WebHost.ConfigureKestrel(opts =>
        {
            opts.Listen(System.Net.IPAddress.Loopback, 0);
        });
    }

    var app = builder.Build();

    // ---- Migrate / init DB on startup ----
    var dbFactory = app.Services.GetRequiredService<DbConnectionFactory>();
    BackupService.ApplyPendingRestore(dbFactory);
    await DbInitializer.InitializeAsync(dbFactory);

    // ---- CORS: allow the app's own origins ----
    app.UseCors();

    // ---- Static frontend (web build: Vite dist copied into ./wwwroot) ----
    var spaIndex = Path.Combine(app.Environment.ContentRootPath, "wwwroot", "index.html");
    var hasSpa = File.Exists(spaIndex);
    if (hasSpa)
    {
        app.UseDefaultFiles();
        app.UseStaticFiles();
    }

    // ---- Endpoints ----
    app.MapHealthEndpoints();
    app.MapLibraryEndpoints();
    app.MapMovieEndpoints();
    app.MapMetaTubeEndpoints();
    app.MapMagnetEndpoints();
    app.MapSubtitleEndpoints();
    app.MapFavoriteEndpoints();
    app.MapActorEndpoints();
    app.MapTagEndpoints();
    app.MapScanEndpoints();
    app.MapSettingsEndpoints();
    app.MapBackupEndpoints();
    app.MapTranslateEndpoints();

    // SPA fallback: serve index.html for any non-API path (hash routing makes
    // deep links rare, but this keeps direct hits on unknown paths harmless).
    if (hasSpa) app.MapFallbackToFile("index.html");

    // ---- Emit the port handshake on stdout ----
    // Tauri's Rust layer reads this exact line to learn the worker URL.
    app.Lifetime.ApplicationStarted.Register(() =>
    {
        try
        {
            var server = app.Services.GetRequiredService<IServer>();
            var addrs = server.Features.Get<IServerAddressesFeature>()!.Addresses;
            var first = addrs.First();
            if (System.Uri.TryCreate(first, System.UriKind.Absolute, out var u))
            {
                Console.Out.WriteLine($"JAVIDEO_WORKER_PORT={u.Port}");
                Console.Out.Flush();
                Log.Information("Worker listening on {Url}", first);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to emit port handshake");
        }
    });

    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Javideo Worker terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}

internal sealed class StringListHandler : SqlMapper.TypeHandler<List<string>>
{
    public override List<string> Parse(object value) =>
        value is string s && !string.IsNullOrEmpty(s)
            ? System.Text.Json.JsonSerializer.Deserialize<List<string>>(s) ?? new()
            : new();

    public override void SetValue(IDbDataParameter parameter, List<string>? value) =>
        parameter.Value = value?.Any() == true
            ? System.Text.Json.JsonSerializer.Serialize(value)
            : DBNull.Value;
}
