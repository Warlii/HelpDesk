namespace HelpDesk.Api.Models;

public enum Role
{
    User, Admin
}
public enum TicketStatus
{
    Open, InProgress, Resolved, Closed
}
public enum Priority
{
    Low, Medium, High, Critical
}
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public Role Role { get; set; }
}
public class Ticket
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Title { get; set; } = "";
    public string Description { get; set; } = "";
    public TicketStatus Status { get; set; }
    public Priority Priority { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Guid CreatedByUserId { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public uint Version { get; set; }
}
public class Comment
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid TicketId { get; set; }
    public Guid UserId { get; set; }
    public string Content { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
