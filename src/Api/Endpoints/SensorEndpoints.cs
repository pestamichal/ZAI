using Api.Db;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public record SensorInput(string? Name, int? SeriesId);

public record SensorDto(int Id, string Name, int SeriesId, DateTimeOffset CreatedAt, DateTimeOffset? LastMeasurementAt)
{
    public static SensorDto From(Sensor s) => new(s.Id, s.Name, s.SeriesId, s.CreatedAt, s.LastMeasurementAt);
}

public record SensorCreatedDto(int Id, string Name, int SeriesId, DateTimeOffset CreatedAt, DateTimeOffset? LastMeasurementAt, string ApiKey);

public static class SensorEndpoints
{
    public static void MapSensors(this WebApplication app)
    {
        var sensors = app.MapGroup("/api/sensors").RequireAdmin();

        sensors.MapGet("/", async (AppDbContext db) =>
            (await db.Sensors.OrderBy(s => s.Id).ToListAsync()).Select(SensorDto.From));

        sensors.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Sensors.FindAsync(id) is { } s ? Results.Ok(SensorDto.From(s)) : Results.NotFound());

        sensors.MapPost("/", async (SensorInput input, AppDbContext db) =>
        {
            if (string.IsNullOrWhiteSpace(input.Name) || input.Name.Length > 100)
                return Results.Problem("name is required and must have at most 100 characters", statusCode: 422);
            if (input.SeriesId is not { } seriesId || !await db.Series.AnyAsync(s => s.Id == seriesId))
                return Results.Problem("seriesId must point to an existing series", statusCode: 422);

            var key = AuthEndpoints.RandomToken();
            var sensor = new Sensor
            {
                Name = input.Name,
                SeriesId = seriesId,
                ApiKeyHash = AuthEndpoints.Sha256(key),
                CreatedAt = DateTimeOffset.UtcNow
            };
            db.Sensors.Add(sensor);
            await db.SaveChangesAsync();

            return Results.Created($"/api/sensors/{sensor.Id}",
                new SensorCreatedDto(sensor.Id, sensor.Name, sensor.SeriesId, sensor.CreatedAt, null, key));
        });

        sensors.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
            await db.Sensors.Where(s => s.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound());
    }
}
