using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;
using HelpDesk.Api.Models;

namespace HelpDesk.Api.DTOs;

public record RegisterRequest(
    [Required, StringLength(100, MinimumLength = 2)] string Name,
    [Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128, MinimumLength = 12)] string Password);
public record LoginRequest([Required, EmailAddress, StringLength(254)] string Email,
    [Required, StringLength(128)] string Password);
public record UserResponse(Guid Id, string Name, string Email, Role Role);
public record AuthResponse(string AccessToken, DateTimeOffset ExpiresAt, UserResponse User);
public record CreateTicketRequest(
    [Required, StringLength(200, MinimumLength = 3)] string Title,
    [Required, StringLength(10000, MinimumLength = 10)] string Description,
    [property: JsonRequired][EnumDataType(typeof(Priority))] Priority Priority);
public record StatusRequest([property: JsonRequired][EnumDataType(typeof(TicketStatus))] TicketStatus Status);
public record PriorityRequest([property: JsonRequired][EnumDataType(typeof(Priority))] Priority Priority);
public record AssignmentRequest([property: JsonRequired] Guid UserId);
public record CommentRequest([Required, StringLength(5000)] string Content);
public record TicketResponse(Guid Id, string Title, string Description, TicketStatus Status, Priority Priority, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt, Guid CreatedByUserId, Guid? AssignedToUserId);
public record CommentResponse(Guid Id, Guid TicketId, Guid UserId, string Content, DateTimeOffset CreatedAt);
public class TicketFilter
{
    [EnumDataType(typeof(TicketStatus))]
    public TicketStatus? Status { get; set; }
    [EnumDataType(typeof(Priority))]
    public Priority? Priority { get; set; }
    public Guid? AssignedToUserId { get; set; }
    public DateTimeOffset? From { get; set; }
    public DateTimeOffset? To { get; set; }
    [Range(1, 1000000)]
    public int Page { get; set; } = 1;
    [Range(1, 100)]
    public int PageSize { get; set; } = 20;
}
