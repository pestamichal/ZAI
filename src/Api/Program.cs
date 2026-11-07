using Api.Db;
using Api.Endpoints;
using Microsoft.EntityFrameworkCore;
using Microsoft.Net.Http.Headers;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
    .UseSnakeCaseNamingConvention());
builder.Services.AddProblemDetails();

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

app.UseExceptionHandler();
app.UseStatusCodePages();

var json = new MediaTypeHeaderValue("application/json");
var problemJson = new MediaTypeHeaderValue("application/problem+json");

app.Use(async (ctx, next) =>
{
    var headers = ctx.Response.Headers;
    headers.XContentTypeOptions = "nosniff";
    headers.ContentSecurityPolicy = "default-src 'self'; frame-ancestors 'none'";
    headers["Referrer-Policy"] = "no-referrer";

    if (ctx.Request.Path.StartsWithSegments("/api"))
    {
        var accept = ctx.Request.GetTypedHeaders().Accept;
        if (accept.Count > 0 && !accept.Any(a => json.IsSubsetOf(a) || problemJson.IsSubsetOf(a)))
        {
            ctx.Response.StatusCode = StatusCodes.Status406NotAcceptable;
            return;
        }
    }

    await next();
});

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));
app.MapAuth();

app.Run();
