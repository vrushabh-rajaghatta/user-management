using Ligature.Host.Api;
using Ligature.Host.Authentication;
using Ligature.Host.Configuration;
using Ligature.Platform.Application;
using Ligature.Platform.Persistence;
using Scalar.AspNetCore;

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

// Read once, here, so the services registered below and the endpoints mapped
// after Build() cannot disagree about whether documentation is published. A
// malformed value throws out of this call and stops the process.
var apiDocumentationEnabled = ApiDocumentation.IsEnabled(builder.Configuration);

if (apiDocumentationEnabled)
{
    // Only registered when it is going to be used. Off, the host carries no
    // document generator at all rather than one nothing maps.
    builder.Services.AddOpenApi();
}

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
app.MapUserEndpoints();

if (apiDocumentationEnabled)
{
    // Mapped AFTER the API endpoints so the document describes routes that are
    // already registered, and so nothing here can shadow one of them.
    //
    // Both routes are anonymous when enabled: /openapi/v1.json is the document
    // and /scalar is the reference UI that reads it. Section 18 is explicit
    // that this is a surface an operator opts into, not one that authenticates
    // its readers.
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.Run();

/// <summary>
/// Exposed so WebApplicationFactory can name this assembly's entry point.
/// Testing the real pipe — middleware, routing, model binding and all — is the
/// only way to prove the boundary this project exists to be.
/// </summary>
public partial class Program;
