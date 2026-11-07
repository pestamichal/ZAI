using System.Buffers.Text;
using System.Security.Cryptography;
using System.Text;
using Api.Db;
using Microsoft.EntityFrameworkCore;

namespace Api.Endpoints;

public record LoginRequest(string? Username, string? Password);
public record PasswordChange(string? CurrentPassword, string? NewPassword);

public static class AuthEndpoints
{
    const int TokenLifetimeSeconds = 3600;

    public static string Sha256(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public static string RandomToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static TBuilder RequireAdmin<TBuilder>(this TBuilder builder) where TBuilder : IEndpointConventionBuilder =>
        builder.AddEndpointFilter(async (ctx, next) =>
        {
            var http = ctx.HttpContext;
            var header = http.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                return Results.Unauthorized();

            var hash = Sha256(header[7..].Trim());
            var db = http.RequestServices.GetRequiredService<AppDbContext>();
            var session = await db.Sessions.Include(s => s.Admin)
                .FirstOrDefaultAsync(s => s.TokenHash == hash && s.ExpiresAt > DateTimeOffset.UtcNow);
            if (session is null)
                return Results.Unauthorized();

            http.Items[nameof(Session)] = session;
            return await next(ctx);
        });

    static Session CurrentSession(HttpContext http) => (Session)http.Items[nameof(Session)]!;

    public static void MapAuth(this WebApplication app)
    {
        var auth = app.MapGroup("/api/auth");

        auth.MapPost("/login", async (LoginRequest req, AppDbContext db) =>
        {
            if (string.IsNullOrEmpty(req.Username) || string.IsNullOrEmpty(req.Password))
                return Results.Problem("Username and password are required", statusCode: 400);

            var admin = await db.Admins.FirstOrDefaultAsync(a => a.Username == req.Username);
            if (admin is null || !BCrypt.Net.BCrypt.Verify(req.Password, admin.PasswordHash))
                return Results.Problem("Invalid username or password", statusCode: 401);

            var token = RandomToken();
            db.Sessions.Add(new Session
            {
                AdminId = admin.Id,
                TokenHash = Sha256(token),
                ExpiresAt = DateTimeOffset.UtcNow.AddSeconds(TokenLifetimeSeconds)
            });
            await db.Sessions.Where(s => s.ExpiresAt < DateTimeOffset.UtcNow).ExecuteDeleteAsync();
            await db.SaveChangesAsync();

            return Results.Ok(new { accessToken = token, tokenType = "Bearer", expiresIn = TokenLifetimeSeconds });
        });

        auth.MapPost("/logout", async (HttpContext http, AppDbContext db) =>
        {
            db.Sessions.Remove(CurrentSession(http));
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();

        auth.MapPut("/password", async (PasswordChange req, HttpContext http, AppDbContext db) =>
        {
            if (req.CurrentPassword is null || req.NewPassword is null)
                return Results.Problem("currentPassword and newPassword are required", statusCode: 400);

            var admin = CurrentSession(http).Admin;
            if (!BCrypt.Net.BCrypt.Verify(req.CurrentPassword, admin.PasswordHash))
                return Results.Problem("Current password is incorrect", statusCode: 403);
            if (req.NewPassword.Length < 8)
                return Results.Problem("New password must have at least 8 characters", statusCode: 422);

            admin.PasswordHash = BCrypt.Net.BCrypt.HashPassword(req.NewPassword, 12);
            await db.SaveChangesAsync();
            return Results.NoContent();
        }).RequireAdmin();
    }
}
