using System.Security.Claims;
using HelpDesk.Api.Infrastructure;

namespace HelpDesk.Api.Services;

public interface ICurrentUser
{
    Guid Id
    {
        get;
    }
    bool IsAdmin
    {
        get;
    }
}
public class CurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    public Guid Id => Guid.TryParse(accessor.HttpContext?.User.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : throw new ApiException(401, "Authentication required.");
    public bool IsAdmin => accessor.HttpContext?.User.IsInRole("Admin") == true;
}
