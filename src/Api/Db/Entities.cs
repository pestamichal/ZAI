namespace Api.Db;

public class Admin
{
    public int Id { get; set; }
    public required string Username { get; set; }
    public required string PasswordHash { get; set; }
}

public class Session
{
    public int Id { get; set; }
    public int AdminId { get; set; }
    public Admin Admin { get; set; } = null!;
    public required string TokenHash { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
}

public class Series
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public double MinValue { get; set; }
    public double MaxValue { get; set; }
    public required string Color { get; set; }
    public string? Icon { get; set; }
    public string? Unit { get; set; }
}

public class Sensor
{
    public int Id { get; set; }
    public required string Name { get; set; }
    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public required string ApiKeyHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? LastMeasurementAt { get; set; }
}

public class Measurement
{
    public long Id { get; set; }
    public int SeriesId { get; set; }
    public Series Series { get; set; } = null!;
    public int? SensorId { get; set; }
    public Sensor? Sensor { get; set; }
    public double Value { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
