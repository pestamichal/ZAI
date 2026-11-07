using System.Globalization;
using Api.Db;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public record MeasurementInput(double? Value, DateTimeOffset? Timestamp);

public record MeasurementDto(long Id, int SeriesId, int? SensorId, double Value, DateTimeOffset Timestamp)
{
    public static MeasurementDto From(Measurement m) => new(m.Id, m.SeriesId, m.SensorId, m.Value, m.Timestamp);
}

public static class MeasurementEndpoints
{
    static bool TryParseTime(string? text, out DateTimeOffset? value)
    {
        value = null;
        if (text is null) return true;
        if (!DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed))
            return false;
        value = parsed.ToUniversalTime();
        return true;
    }

    public static void MapMeasurements(this WebApplication app)
    {
        var measurements = app.MapGroup("/api/measurements");

        measurements.MapGet("/", async (string? series, string? from, string? to, string? sort, string? limit, AppDbContext db) =>
        {
            var query = db.Measurements.AsQueryable();

            if (!string.IsNullOrWhiteSpace(series))
            {
                var ids = new List<int>();
                foreach (var part in series.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
                {
                    if (!int.TryParse(part, out var id))
                        return Results.Problem($"Invalid series id '{part}'", statusCode: 400);
                    ids.Add(id);
                }
                query = query.Where(m => ids.Contains(m.SeriesId));
            }

            if (!TryParseTime(from, out var fromTime))
                return Results.Problem("from must be an ISO 8601 date", statusCode: 400);
            if (!TryParseTime(to, out var toTime))
                return Results.Problem("to must be an ISO 8601 date", statusCode: 400);
            if (fromTime is { } f) query = query.Where(m => m.Timestamp >= f);
            if (toTime is { } t) query = query.Where(m => m.Timestamp <= t);

            if (sort is "-timestamp")
                query = query.OrderByDescending(m => m.Timestamp).ThenByDescending(m => m.Id);
            else if (sort is null or "timestamp")
                query = query.OrderBy(m => m.Timestamp).ThenBy(m => m.Id);
            else
                return Results.Problem("sort must be timestamp or -timestamp", statusCode: 400);

            var take = 1000;
            if (limit is not null && (!int.TryParse(limit, out take) || take < 1 || take > 10000))
                return Results.Problem("limit must be an integer between 1 and 10000", statusCode: 400);

            var list = await query.Take(take).ToListAsync();
            return Results.Ok(list.Select(MeasurementDto.From));
        });

        measurements.MapGet("/{id:long}", async (long id, AppDbContext db) =>
            await db.Measurements.FindAsync(id) is { } m ? Results.Ok(MeasurementDto.From(m)) : Results.NotFound());

        measurements.MapPost("/", async (MeasurementInput input, HttpContext http, AppDbContext db, ILogger<MeasurementInput> log) =>
        {
            var key = http.Request.Headers["X-API-Key"].ToString();
            if (string.IsNullOrEmpty(key))
                return Results.Problem("Missing X-API-Key header", statusCode: 401);

            var hash = AuthEndpoints.Sha256(key);
            var sensor = await db.Sensors.Include(s => s.Series).FirstOrDefaultAsync(s => s.ApiKeyHash == hash);
            if (sensor is null)
                return Results.Problem("Invalid API key", statusCode: 401);

            if (input.Value is not { } value || !double.IsFinite(value))
                return Results.Problem("value is required", statusCode: 422);

            var now = DateTimeOffset.UtcNow;
            var timestamp = (input.Timestamp ?? now).ToUniversalTime();
            if (timestamp > now.AddMinutes(5))
            {
                log.LogWarning("Rejected measurement from sensor {SensorId}: timestamp {Timestamp} is in the future", sensor.Id, timestamp);
                return Results.Problem("timestamp cannot be more than 5 minutes in the future", statusCode: 422);
            }

            var s = sensor.Series;
            if (value < s.MinValue || value > s.MaxValue)
            {
                log.LogWarning("Rejected measurement from sensor {SensorId}: value {Value} outside [{Min}, {Max}] of series {SeriesId}",
                    sensor.Id, value, s.MinValue, s.MaxValue, s.Id);
                return Results.Problem($"value {value} is outside the series range [{s.MinValue}, {s.MaxValue}]", statusCode: 422);
            }

            var measurement = new Measurement { SeriesId = s.Id, SensorId = sensor.Id, Value = value, Timestamp = timestamp };
            db.Measurements.Add(measurement);
            sensor.LastMeasurementAt = now;
            await db.SaveChangesAsync();

            return Results.Created($"/api/measurements/{measurement.Id}", MeasurementDto.From(measurement));
        });
    }
}
