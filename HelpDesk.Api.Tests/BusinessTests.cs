using HelpDesk.Api.Data;
using HelpDesk.Api.DTOs;
using HelpDesk.Api.Infrastructure;
using HelpDesk.Api.Models;
using HelpDesk.Api.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace HelpDesk.Api.Tests;

public class BusinessTests
{
    sealed class Actor(Guid id, bool admin = false) : ICurrentUser
    {
        public Guid Id => id;
        public bool IsAdmin => admin;
    }
    static HelpDeskDbContext Db() => new(new DbContextOptionsBuilder<HelpDeskDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
    [Theory]
    [InlineData(TicketStatus.Open, TicketStatus.InProgress)]
    [InlineData(TicketStatus.InProgress, TicketStatus.Resolved)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Closed)]
    public void ValidTransitions(TicketStatus from, TicketStatus to) => TicketService.ValidateTransition(from, to);
    [Fact]
    public void EveryOtherTransitionIsConflict()
    {
        foreach (var from in Enum.GetValues<TicketStatus>())
            foreach (var to in Enum.GetValues<TicketStatus>())
                if ((int)to != (int)from + 1 || from == TicketStatus.Closed)
                    Assert.Equal(409, Assert.Throws<ApiException>(() => TicketService.ValidateTransition(from, to)).Status);
        Assert.Equal(400, Assert.Throws<ApiException>(() => TicketService.ValidateTransition(TicketStatus.Open, (TicketStatus)99)).Status);
    }
    [Fact]
    public async Task OwnerCanCreateAndCommentButCannotManage()
    {
        await using var db = Db();
        var u = new User();
        db.Users.Add(u);
        await db.SaveChangesAsync();
        var service = new TicketService(db, new Actor(u.Id));
        var t = await service.Create(new("Printer broken", "The printer is not working.", Priority.High), default);
        Assert.Equal(TicketStatus.Open, t.Status);
        Assert.Equal(u.Id, t.CreatedByUserId);
        Assert.Equal(TimeSpan.Zero, t.CreatedAt.Offset);
        var c = await service.AddComment(t.Id, new("More details"), default);
        Assert.Equal(u.Id, c.UserId);
        Assert.Single(await service.Comments(t.Id, 1, default));
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => service.ChangeStatus(t.Id, TicketStatus.InProgress, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => service.Assign(t.Id, u.Id, default))).Status);
    }
    [Fact]
    public async Task OtherUsersCannotReadOrCommentEvenWhenAssigned()
    {
        await using var db = Db();
        var owner = new User();
        var other = new User();
        db.Users.AddRange(owner, other);
        var t = new Ticket { Title = "Test", CreatedByUserId = owner.Id, AssignedToUserId = other.Id };
        db.Tickets.Add(t);
        await db.SaveChangesAsync();
        var service = new TicketService(db, new Actor(other.Id));
        Assert.Empty(await service.List(new(), default));
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => service.Get(t.Id, default))).Status);
        Assert.Equal(403, (await Assert.ThrowsAsync<ApiException>(() => service.AddComment(t.Id, new("Hi"), default))).Status);
    }
    [Fact]
    public async Task AdminCanAssignAndFollowLifecycle()
    {
        await using var db = Db();
        var u = new User();
        var admin = new User { Role = Role.Admin };
        db.Users.AddRange(u, admin);
        var t = new Ticket { CreatedByUserId = u.Id };
        db.Tickets.Add(t);
        await db.SaveChangesAsync();
        var service = new TicketService(db, new Actor(admin.Id, true));
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => service.Assign(t.Id, Guid.NewGuid(), default))).Status);
        await service.Assign(t.Id, u.Id, default);
        Assert.Equal(u.Id, t.AssignedToUserId);
        await service.ChangePriority(t.Id, Priority.Critical, default);
        Assert.Equal(Priority.Critical, t.Priority);
        await service.ChangeStatus(t.Id, TicketStatus.InProgress, default);
        await service.ChangeStatus(t.Id, TicketStatus.Resolved, default);
        await service.ChangeStatus(t.Id, TicketStatus.Closed, default);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.ChangeStatus(t.Id, TicketStatus.InProgress, default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.AddComment(t.Id, new("Hi"), default))).Status);
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.Assign(t.Id, u.Id, default))).Status);
    }
    [Fact]
    public async Task ValidationAndFilters()
    {
        await using var db = Db();
        var u = new User();
        db.Users.Add(u);
        await db.SaveChangesAsync();
        var service = new TicketService(db, new Actor(u.Id));
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.Create(new(" ", "Too short", Priority.Low), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.Create(new("Valid title", "Valid description", (Priority)99), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.List(new() { From = DateTimeOffset.UtcNow, To = DateTimeOffset.UtcNow.AddDays(-1) }, default))).Status);
        Assert.Equal(404, (await Assert.ThrowsAsync<ApiException>(() => service.Get(Guid.NewGuid(), default))).Status);
        var t = await service.Create(new("Valid title", "Valid description", Priority.High), default);
        Assert.Single(await service.List(new()
        {
            Priority = Priority.High,
            Status = TicketStatus.Open
        }, default));
        Assert.Empty(await service.List(new()
        {
            Priority = Priority.Low
        }, default));
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.AddComment(t.Id, new(" "), default))).Status);
    }
    [Fact]
    public async Task RegistrationHashesPasswordsAndNeverGrantsAdmin()
    {
        await using var db = Db();
        var hasher = new PasswordHasher<User>();
        var service = new AuthService(db, hasher, new()
        {
            Key = new string('x', 64)
        });
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.Register(new("Test User", "invalid-email", "a-long-test-password"), default))).Status);
        Assert.Equal(400, (await Assert.ThrowsAsync<ApiException>(() => service.Register(new("Test User", "user@example.com", "short"), default))).Status);
        var r = await service.Register(new("Test User", " USER@example.com ", "a-long-test-password"), default);
        var u = await db.Users.SingleAsync();
        Assert.Equal(Role.User, r.Role);
        Assert.Equal("user@example.com", u.Email);
        Assert.NotEqual("a-long-test-password", u.PasswordHash);
        Assert.Equal(PasswordVerificationResult.Success, hasher.VerifyHashedPassword(u, u.PasswordHash, "a-long-test-password"));
        Assert.Equal(409, (await Assert.ThrowsAsync<ApiException>(() => service.Register(new("Another", "user@example.com", "a-long-test-password"), default))).Status);
        Assert.Equal(401, (await Assert.ThrowsAsync<ApiException>(() => service.Login(new("user@example.com", "wrong"), default))).Status);
        Assert.NotEmpty((await service.Login(new("user@example.com", "a-long-test-password"), default)).AccessToken);
    }
}




