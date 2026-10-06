using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Infrastructure;
using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Services;

public class TicketService(HelpDeskDbContext db, ICurrentUser actor)
{
    public static void ValidateTransition(TicketStatus current, TicketStatus next)
    {
        if (!Enum.IsDefined(next))
            throw new ApiException(400, "Invalid status.");
        if ((current, next) is not ((TicketStatus.Open, TicketStatus.InProgress) or (TicketStatus.InProgress, TicketStatus.Resolved) or (TicketStatus.Resolved, TicketStatus.Closed)))
            throw new ApiException(409, "Status transition is not allowed.");
    }
    void Admin()
    {
        if (!actor.IsAdmin)
            throw new ApiException(403, "Administrator role required.");
    }
    void Access(Ticket t)
    {
        if (!actor.IsAdmin && t.CreatedByUserId != actor.Id)
            throw new ApiException(403, "Ticket access denied.");
    }
    static void Text(string? value, int min, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Trim().Length < min || value.Length > max)
            throw new ApiException(400, "Invalid text length.");
    }
    public static TicketResponse Map(Ticket t) => new(t.Id, t.Title, t.Description, t.Status, t.Priority, t.CreatedAt, t.UpdatedAt, t.CreatedByUserId, t.AssignedToUserId);
    async Task<Ticket> Find(Guid id, bool tracking, CancellationToken ct)
    {
        var query = tracking ? db.Tickets : db.Tickets.AsNoTracking();
        var ticket = await query.SingleOrDefaultAsync(x => x.Id == id, ct) ?? throw new ApiException(404, "Ticket not found.");
        Access(ticket);
        return ticket;
    }
    public async Task<TicketResponse> Get(Guid id, CancellationToken ct) => Map(await Find(id, false, ct));
    public async Task<List<TicketResponse>> List(TicketFilter f, CancellationToken ct)
    {
        if (f.From > f.To || f.Page < 1 || f.Page > 1000000 || f.PageSize is < 1 or > 100)
            throw new ApiException(400, "Invalid filter or pagination.");
        if (f.Status.HasValue && !Enum.IsDefined(f.Status.Value) || f.Priority.HasValue && !Enum.IsDefined(f.Priority.Value))
            throw new ApiException(400, "Invalid enum.");
        var q = db.Tickets.AsNoTracking();
        if (!actor.IsAdmin)
            q = q.Where(x => x.CreatedByUserId == actor.Id);
        if (f.Status.HasValue)
            q = q.Where(x => x.Status == f.Status);
        if (f.Priority.HasValue)
            q = q.Where(x => x.Priority == f.Priority);
        if (f.AssignedToUserId.HasValue)
            q = q.Where(x => x.AssignedToUserId == f.AssignedToUserId);
        if (f.From.HasValue)
            q = q.Where(x => x.CreatedAt >= f.From.Value.ToUniversalTime());
        if (f.To.HasValue)
            q = q.Where(x => x.CreatedAt <= f.To.Value.ToUniversalTime());
        return (await q.OrderByDescending(x => x.CreatedAt).ThenBy(x => x.Id).Skip((f.Page - 1) * f.PageSize).Take(f.PageSize).ToListAsync(ct)).Select(Map).ToList();
    }
    public async Task<TicketResponse> Create(CreateTicketRequest r, CancellationToken ct)
    {
        Text(r.Title, 3, 200);
        Text(r.Description, 10, 10000);
        if (!Enum.IsDefined(r.Priority))
            throw new ApiException(400, "Invalid priority.");
        if (!await db.Users.AnyAsync(x => x.Id == actor.Id, ct))
            throw new ApiException(401, "User no longer exists.");
        var t = new Ticket { Title = r.Title.Trim(), Description = r.Description.Trim(), Priority = r.Priority, CreatedByUserId = actor.Id };
        db.Tickets.Add(t);
        await db.SaveChangesAsync(ct);
        return Map(t);
    }
    public async Task ChangeStatus(Guid id, TicketStatus status, CancellationToken ct)
    {
        Admin();
        var t = await Find(id, true, ct);
        ValidateTransition(t.Status, status);
        t.Status = status;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    public async Task Assign(Guid id, Guid userId, CancellationToken ct)
    {
        Admin();
        var t = await Find(id, true, ct);
        if (t.Status == TicketStatus.Closed)
            throw new ApiException(409, "Closed tickets cannot be assigned.");
        if (!await db.Users.AnyAsync(x => x.Id == userId, ct))
            throw new ApiException(404, "Assigned user not found.");
        t.AssignedToUserId = userId;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    public async Task ChangePriority(Guid id, Priority priority, CancellationToken ct)
    {
        Admin();
        if (!Enum.IsDefined(priority))
            throw new ApiException(400, "Invalid priority.");
        var t = await Find(id, true, ct);
        if (t.Status == TicketStatus.Closed)
            throw new ApiException(409, "Closed tickets cannot be edited.");
        t.Priority = priority;
        t.UpdatedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(ct);
    }
    public async Task<CommentResponse> AddComment(Guid id, CommentRequest r, CancellationToken ct)
    {
        var t = await Find(id, true, ct);
        Text(r.Content, 1, 5000);
        if (t.Status == TicketStatus.Closed)
            throw new ApiException(409, "Closed tickets cannot receive comments.");
        var c = new Comment { TicketId = id, UserId = actor.Id, Content = r.Content.Trim() };
        db.Comments.Add(c);
        t.UpdatedAt = c.CreatedAt;
        await db.SaveChangesAsync(ct);
        return new(c.Id, c.TicketId, c.UserId, c.Content, c.CreatedAt);
    }
    public async Task<List<CommentResponse>> Comments(Guid id, int page, CancellationToken ct)
    {
        await Find(id, false, ct);
        if (page is < 1 or > 1000000)
            throw new ApiException(400, "Invalid page.");
        return await db.Comments.AsNoTracking().Where(x => x.TicketId == id).OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Skip((page - 1) * 50).Take(50)
         .Select(x => new CommentResponse(x.Id, x.TicketId, x.UserId, x.Content, x.CreatedAt)).ToListAsync(ct);
    }
}
