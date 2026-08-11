using System.Security.Claims;
using HajjVR.Data;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.EntityFrameworkCore;

namespace HajjVR.Services;

public class AuthService(IDbContextFactory<AppDbContext> dbFactory)
{
    public async Task<AppUser?> ValidateAsync(string userName, string password)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName && u.IsActive);
        if (user is null || !PasswordHasher.Verify(password, user.PasswordHash)) return null;
        return user;
    }

    public static ClaimsPrincipal ToPrincipal(AppUser user) => new(new ClaimsIdentity(
    [
        new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()),
        new Claim(ClaimTypes.Name, user.UserName),
        new Claim("display", user.DisplayName),
        new Claim(ClaimTypes.Email, user.Email),
        new Claim(ClaimTypes.Role, user.Role),
    ], "HajjVRCookie"));

    public async Task<AppUser?> GetUserAsync(int id)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        return await db.Users.Include(u => u.Profile).FirstOrDefaultAsync(u => u.Id == id);
    }

    public async Task<AppUser?> GetCurrentUserAsync(AuthenticationStateProvider provider)
    {
        var state = await provider.GetAuthenticationStateAsync();
        var idClaim = state.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return int.TryParse(idClaim, out var id) ? await GetUserAsync(id) : null;
    }

    // ---------- Reset password (mode demo: token ditampilkan langsung, tanpa email) ----------
    public async Task<string?> CreateResetTokenAsync(string userNameOrEmail)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userNameOrEmail || u.Email == userNameOrEmail);
        if (user is null) return null;
        user.ResetToken = Guid.NewGuid().ToString("N")[..12];
        user.ResetTokenExpires = DateTime.UtcNow.AddHours(1);
        await db.SaveChangesAsync();
        return user.ResetToken;
    }

    public async Task<bool> ResetPasswordAsync(string token, string newPassword)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FirstOrDefaultAsync(u => u.ResetToken == token && u.ResetTokenExpires > DateTime.UtcNow);
        if (user is null) return false;
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        user.ResetToken = null;
        user.ResetTokenExpires = null;
        await db.SaveChangesAsync();
        return true;
    }

    public async Task<bool> ChangePasswordAsync(int userId, string oldPassword, string newPassword)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(userId);
        if (user is null || !PasswordHasher.Verify(oldPassword, user.PasswordHash)) return false;
        user.PasswordHash = PasswordHasher.Hash(newPassword);
        await db.SaveChangesAsync();
        return true;
    }

    public async Task UpdateProfileAsync(AppUser updated)
    {
        await using var db = await dbFactory.CreateDbContextAsync();
        var user = await db.Users.FindAsync(updated.Id);
        if (user is null) return;
        user.DisplayName = updated.DisplayName;
        user.Email = updated.Email;
        user.Language = updated.Language;
        user.AvatarUrl = updated.AvatarUrl;
        await db.SaveChangesAsync();
    }
}
