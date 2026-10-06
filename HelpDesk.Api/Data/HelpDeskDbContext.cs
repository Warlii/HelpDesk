using HelpDesk.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace HelpDesk.Api.Data;

public class HelpDeskDbContext(DbContextOptions<HelpDeskDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Ticket> Tickets => Set<Ticket>();
    public DbSet<Comment> Comments => Set<Comment>();
    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<User>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(100).IsRequired();
            e.Property(x => x.Email).HasMaxLength(254).IsRequired();
            e.Property(x => x.PasswordHash).IsRequired();
            e.HasIndex(x => x.Email).IsUnique();
            e.ToTable(t => t.HasCheckConstraint("CK_User_Role", "\"Role\" IN (0,1)"));
        });
        b.Entity<Ticket>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(200).IsRequired();
            e.Property(x => x.Description).HasMaxLength(10000).IsRequired();
            e.Property(x => x.Version).IsRowVersion();
            e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.AssignedToUserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.CreatedByUserId, x.CreatedAt });
            e.ToTable(t =>
            {
                t.HasCheckConstraint("CK_Ticket_Status", "\"Status\" BETWEEN 0 AND 3");
                t.HasCheckConstraint("CK_Ticket_Priority", "\"Priority\" BETWEEN 0 AND 3");
            });
        });
        b.Entity<Comment>(e =>
        {
            e.Property(x => x.Content).HasMaxLength(5000).IsRequired();
            e.HasOne<Ticket>().WithMany().HasForeignKey(x => x.TicketId).OnDelete(DeleteBehavior.Restrict);
            e.HasOne<User>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Restrict);
            e.HasIndex(x => new { x.TicketId, x.CreatedAt });
        });
    }
}
