using HelpDesk.Api.DTOs;
using HelpDesk.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace HelpDesk.Api.Controllers;

[ApiController, Authorize, Route("api/tickets")]
public class TicketsController(TicketService service) : ControllerBase
{
    [HttpGet] public async Task<ActionResult<List<TicketResponse>>> List([FromQuery] TicketFilter filter, CancellationToken ct) => Ok(await service.List(filter, ct));
    [HttpGet("{id:guid}")] public async Task<ActionResult<TicketResponse>> Get(Guid id, CancellationToken ct) => Ok(await service.Get(id, ct));
    [HttpPost]
    public async Task<ActionResult<TicketResponse>> Create(CreateTicketRequest request, CancellationToken ct)
    {
        var t = await service.Create(request, ct);
        return CreatedAtAction(nameof(Get), new
        {
            id = t.Id
        }, t);
    }
    [Authorize(Roles = "Admin"), HttpPatch("{id:guid}/status")]
    public async Task<IActionResult> Status(Guid id, StatusRequest request, CancellationToken ct)
    {
        await service.ChangeStatus(id, request.Status, ct);
        return NoContent();
    }
    [Authorize(Roles = "Admin"), HttpPatch("{id:guid}/assignment")]
    public async Task<IActionResult> Assign(Guid id, AssignmentRequest request, CancellationToken ct)
    {
        await service.Assign(id, request.UserId, ct);
        return NoContent();
    }
    [Authorize(Roles = "Admin"), HttpPatch("{id:guid}/priority")]
    public async Task<IActionResult> Priority(Guid id, PriorityRequest request, CancellationToken ct)
    {
        await service.ChangePriority(id, request.Priority, ct);
        return NoContent();
    }
    [HttpGet("{id:guid}/comments")]
    public async Task<ActionResult<List<CommentResponse>>> Comments(Guid id, [FromQuery] int page = 1, CancellationToken ct = default) => Ok(await service.Comments(id, page, ct));
    [HttpPost("{id:guid}/comments")]
    public async Task<ActionResult<CommentResponse>> Comment(Guid id, CommentRequest request, CancellationToken ct)
    {
        var c = await service.AddComment(id, request, ct);
        return CreatedAtAction(nameof(Comments), new
        {
            id
        }, c);
    }
}
