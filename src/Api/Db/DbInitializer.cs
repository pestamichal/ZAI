using Microsoft.EntityFrameworkCore;

namespace Api.Db;

public static class DbInitializer
{
    public static async Task InitializeAsync(IServiceProvider services)
    {
        using var scope = services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var log = scope.ServiceProvider.GetRequiredService<ILogger<AppDbContext>>();

        await db.Database.MigrateAsync();

        if (await db.Admins.AnyAsync()) return;

        var username = config["Admin:Username"];
        var password = config["Admin:Password"];
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            log.LogWarning("No admin account exists and Admin:Username / Admin:Password are not set");
            return;
        }

        db.Admins.Add(new Admin { Username = username, PasswordHash = BCrypt.Net.BCrypt.HashPassword(password, 12) });
        await db.SaveChangesAsync();
        log.LogInformation("Created admin account {Username}", username);
    }
}
