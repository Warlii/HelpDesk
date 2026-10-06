using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Infrastructure;
using HelpDesk.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Controllers;

[ApiController, Authorize, Route("api/users")]
public class UsersController(HelpDeskDbContext db, ICurrentUser actor) : ControllerBase
{
    [HttpGet("me")]
    public async Task<ActionResult<UserResponse>> Me(CancellationToken ct) => Ok(await db.Users.AsNoTracking().Where(x => x.Id == actor.Id)
     .Select(x => new UserResponse(x.Id, x.Name, x.Email, x.Role)).SingleOrDefaultAsync(ct) ?? throw new ApiException(401, "User no longer exists."));
    [HttpGet, Authorize(Roles = "Admin")]
    public async Task<ActionResult<List<UserResponse>>> List([FromQuery] int page = 1, CancellationToken ct = default)
    {
        if (page is < 1 or > 1000000)
            throw new ApiException(400, "Invalid page.");
        return Ok(await db.Users.AsNoTracking().OrderBy(x => x.Name).ThenBy(x => x.Id).Skip((page - 1) * 50).Take(50)
         .Select(x => new UserResponse(x.Id, x.Name, x.Email, x.Role)).ToListAsync(ct));
    }
}
