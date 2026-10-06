using HelpDesk.Api.DTOs;
using HelpDesk.Api.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace HelpDesk.Api.Controllers;

[ApiController, Route("api/auth"), EnableRateLimiting("auth")]
public class AuthController(AuthService service) : ControllerBase
{
    [HttpPost("register")]
    public async Task<ActionResult<UserResponse>> Register(RegisterRequest request, CancellationToken ct)
    {
        var user = await service.Register(request, ct);
        return Created("/api/users/me", user);
    }
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request, CancellationToken ct) => Ok(await service.Login(request, ct));
}
