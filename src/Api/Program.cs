using Api.Db;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<AppDbContext>(o => o
    .UseNpgsql(builder.Configuration.GetConnectionString("Default"))
    .UseSnakeCaseNamingConvention());

var app = builder.Build();

await DbInitializer.InitializeAsync(app.Services);

app.MapGet("/api/health", () => Results.Ok(new { status = "ok" }));

app.Run();
