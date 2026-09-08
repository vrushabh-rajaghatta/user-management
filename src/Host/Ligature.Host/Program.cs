using Ligature.Host.Api;
using Ligature.Host.Authentication;
using Ligature.Host.Configuration;
using Ligature.Platform.Application;
using Ligature.Platform.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Configuration is environment-backed and carries no secrets in a file. There
// is no appsettings.json holding a connection string or key material,
// deliberately: a secret in a repository is a secret that has leaked.
var connectionString =
    builder.Configuration[HostConfiguration.ConnectionSetting];

if (string.IsNullOrWhiteSpace(connectionString))
{
    throw new InvalidOperationException(
        $"{HostConfiguration.ConnectionSetting} is not configured. The host "
        + "does NOT fall back to a local default the way the test helpers and "
        + "the EF design-time factory do — those run on a developer's machine, "
        + "and a deployed host that quietly connects to some other database "
        + "would be worse than one that refuses to start.");
}

builder.Services.AddPlatformApplication();
builder.Services.AddPlatformPersistence(connectionString);

// Loaded and validated HERE, at startup, so a missing or undersized signing key
// stops the process rather than surfacing on the first sign-in.
builder.Services.AddSingleton(SigningKeyRing.Load(builder.Configuration));
builder.Services.AddSingleton<AccessCarrier>();

builder.Services.AddScoped<CurrentCarrier>();
builder.Services.AddScoped<CallerMiddleware>();
builder.Services.AddScoped<ProblemMiddleware>();

var app = builder.Build();

// Order matters, in both directions.
//
// ProblemMiddleware is OUTERMOST so that it catches what the pipeline throws
// downstream of it — an AuthenticationFailedException raised by a behaviour
// several layers in still becomes a 401 rather than an unhandled 500.
//
// CallerMiddleware sits inside it and before the endpoints, because the caller
// must be established on the request scope before any command is dispatched
// under it.
//
// No developer exception page is registered in any environment. One would turn
// the deliberate no-detail 500 into a stack trace the moment somebody ran the
// host with ASPNETCORE_ENVIRONMENT=Development.
app.UseMiddleware<ProblemMiddleware>();
app.UseMiddleware<CallerMiddleware>();

app.MapAuthEndpoints();
app.MapAccountEndpoints();

app.Run();

/// <summary>
/// Exposed so WebApplicationFactory can name this assembly's entry point.
/// Testing the real pipe — middleware, routing, model binding and all — is the
/// only way to prove the boundary this project exists to be.
/// </summary>
public partial class Program;
