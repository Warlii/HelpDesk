using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Infrastructure;
using HelpDesk.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

namespace HelpDesk.Api.Services;

public class JwtSettings
{
    public string Key { get; set; } = "";
    public string Issuer { get; set; } = "HelpDesk.Api";
    public string Audience { get; set; } = "HelpDesk.Client";
    public int Minutes { get; set; } = 60;
}
public class AuthService(HelpDeskDbContext db, IPasswordHasher<User> hasher, JwtSettings jwt)
{
    readonly string dummyHash = hasher.HashPassword(new User(), "dummy-password-for-timing");
    static void Validate(RegisterRequest request)
    {
        Validate(new LoginRequest(request.Email, request.Password));
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length < 2 ||
            request.Name.Length > 100 || request.Password.Length < 12)
            throw new ApiException(400, "Invalid registration fields.");
    }

    static void Validate(LoginRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || request.Email.Length > 254 ||
            !new System.ComponentModel.DataAnnotations.EmailAddressAttribute().IsValid(request.Email) ||
            string.IsNullOrWhiteSpace(request.Password) || request.Password.Length > 128)
            throw new ApiException(400, "Invalid authentication fields.");
    }
    static string Normalize(string email) => email.Trim().ToLowerInvariant();
    public async Task<UserResponse> Register(RegisterRequest r, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(r.Name) || r.Name.Trim().Length < 2)
            throw new ApiException(400, "Invalid name.");
        Validate(r);
        var email = Normalize(r.Email);
        if (await db.Users.AnyAsync(x => x.Email == email, ct))
            throw new ApiException(409, "Email already registered.");
        var u = new User { Name = r.Name.Trim(), Email = email, Role = Role.User };
        u.PasswordHash = hasher.HashPassword(u, r.Password);
        db.Users.Add(u);
        await db.SaveChangesAsync(ct);
        return new(u.Id, u.Name, u.Email, u.Role);
    }
    public async Task<AuthResponse> Login(LoginRequest r, CancellationToken ct)
    {
        Validate(r);
        var u = await db.Users.SingleOrDefaultAsync(x => x.Email == Normalize(r.Email), ct);
        // Run the same expensive hash check for unknown accounts to reduce timing differences.
        var candidate = u ?? new User();
        var hash = u?.PasswordHash ?? dummyHash;
        var result = hasher.VerifyHashedPassword(candidate, hash, r.Password);
        if (u is null || result == PasswordVerificationResult.Failed)
            throw new ApiException(401, "Invalid email or password.");
        if (result == PasswordVerificationResult.SuccessRehashNeeded)
        {
            u.PasswordHash = hasher.HashPassword(u, r.Password);
            await db.SaveChangesAsync(ct);
        }
        var expires = DateTimeOffset.UtcNow.AddMinutes(jwt.Minutes);
        var token = new JwtSecurityToken(jwt.Issuer, jwt.Audience,
         [new Claim(ClaimTypes.NameIdentifier, u.Id.ToString()), new Claim(ClaimTypes.Role, u.Role.ToString()), new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString())],
         DateTime.UtcNow, expires.UtcDateTime, new SigningCredentials(new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.Key)), SecurityAlgorithms.HmacSha256));
        return new(new JwtSecurityTokenHandler().WriteToken(token), expires, new(u.Id, u.Name, u.Email, u.Role));
    }
}
