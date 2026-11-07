using System.Text.RegularExpressions;
using Api.Db;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public record SeriesInput(string? Name, double? MinValue, double? MaxValue, string? Color, string? Icon, string? Unit);

public record SeriesDto(int Id, string Name, double MinValue, double MaxValue, string Color, string? Icon, string? Unit)
{
    public static SeriesDto From(Series s) => new(s.Id, s.Name, s.MinValue, s.MaxValue, s.Color, s.Icon, s.Unit);
}

public static partial class SeriesEndpoints
{
    [GeneratedRegex(@"^#[0-9A-Fa-f]{6}\z")]
    private static partial Regex ColorRegex();

    static string? Validate(SeriesInput i)
    {
        if (string.IsNullOrWhiteSpace(i.Name) || i.Name.Length > 100)
            return "name is required and must have at most 100 characters";
        if (i.MinValue is null || i.MaxValue is null)
            return "minValue and maxValue are required";
        if (i.MinValue >= i.MaxValue)
            return "minValue must be lower than maxValue";
        if (i.Color is null || !ColorRegex().IsMatch(i.Color))
            return "color must be in #RRGGBB format";
        if (i.Icon?.Length > 30)
            return "icon must have at most 30 characters";
        if (i.Unit?.Length > 20)
            return "unit must have at most 20 characters";
        return null;
    }

    static void Apply(Series s, SeriesInput i)
    {
        s.Name = i.Name!;
        s.MinValue = i.MinValue!.Value;
        s.MaxValue = i.MaxValue!.Value;
        s.Color = i.Color!;
        s.Icon = i.Icon;
        s.Unit = i.Unit;
    }

    public static void MapSeries(this WebApplication app)
    {
        var series = app.MapGroup("/api/series");

        series.MapGet("/", async (AppDbContext db) =>
            (await db.Series.OrderBy(s => s.Id).ToListAsync()).Select(SeriesDto.From));

        series.MapGet("/{id:int}", async (int id, AppDbContext db) =>
            await db.Series.FindAsync(id) is { } s ? Results.Ok(SeriesDto.From(s)) : Results.NotFound());

        series.MapPost("/", async (SeriesInput input, AppDbContext db) =>
        {
            if (Validate(input) is { } error)
                return Results.Problem(error, statusCode: 422);

            var s = new Series { Name = "", Color = "" };
            Apply(s, input);
            db.Series.Add(s);
            await db.SaveChangesAsync();
            return Results.Created($"/api/series/{s.Id}", SeriesDto.From(s));
        }).RequireAdmin();

        series.MapPut("/{id:int}", async (int id, SeriesInput input, AppDbContext db) =>
        {
            if (Validate(input) is { } error)
                return Results.Problem(error, statusCode: 422);

            var s = await db.Series.FindAsync(id);
            if (s is null)
                return Results.NotFound();

            var min = input.MinValue!.Value;
            var max = input.MaxValue!.Value;
            if (await db.Measurements.AnyAsync(m => m.SeriesId == id && (m.Value < min || m.Value > max)))
                return Results.Problem("Existing measurements fall outside the new range", statusCode: 409);

            Apply(s, input);
            await db.SaveChangesAsync();
            return Results.Ok(SeriesDto.From(s));
        }).RequireAdmin();

        series.MapDelete("/{id:int}", async (int id, AppDbContext db) =>
            await db.Series.Where(s => s.Id == id).ExecuteDeleteAsync() > 0 ? Results.NoContent() : Results.NotFound())
            .RequireAdmin();
    }
}
