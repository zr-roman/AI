using Crm.Core.Domain;
using Microsoft.EntityFrameworkCore;

namespace Crm.Core.Data;

public sealed class CrmDbContext(DbContextOptions<CrmDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<Company> Companies => Set<Company>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<Deal> Deals => Set<Deal>();
    public DbSet<Activity> Activities => Set<Activity>();
    public DbSet<CrmTask> Tasks => Set<CrmTask>();
    public DbSet<AuditEntry> AuditLog => Set<AuditEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(e =>
        {
            e.Property(x => x.FullName).HasMaxLength(200);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Role).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.ApiKeyHash).HasMaxLength(64);
            e.HasIndex(x => x.Email).IsUnique();
            e.HasIndex(x => x.ApiKeyHash).IsUnique();
        });

        modelBuilder.Entity<Company>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(200);
            e.Property(x => x.Industry).HasMaxLength(100);
            e.Property(x => x.Website).HasMaxLength(300);
            e.HasIndex(x => x.Name);
        });

        modelBuilder.Entity<Contact>(e =>
        {
            e.Property(x => x.FirstName).HasMaxLength(100);
            e.Property(x => x.LastName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(320);
            e.Property(x => x.Phone).HasMaxLength(50);
            e.Property(x => x.Position).HasMaxLength(150);
            e.HasIndex(x => x.Email);
            e.HasIndex(x => new { x.LastName, x.FirstName });
            e.HasOne(x => x.Company).WithMany(c => c.Contacts).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Deal>(e =>
        {
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Amount).HasPrecision(18, 2);
            // Стадии храним строками: в БД читаемо, а порядок значений enum можно менять
            e.Property(x => x.Stage).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.LostReason).HasMaxLength(500);
            e.HasIndex(x => x.Stage);
            e.HasOne(x => x.Contact).WithMany(c => c.Deals).HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Company).WithMany(c => c.Deals).HasForeignKey(x => x.CompanyId).OnDelete(DeleteBehavior.SetNull);
            e.HasOne(x => x.Owner).WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Activity>(e =>
        {
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(20);
            e.Property(x => x.Subject).HasMaxLength(300);
            e.Property(x => x.Details).HasMaxLength(4000);
            e.HasIndex(x => x.OccurredAt);
            e.HasOne(x => x.Contact).WithMany(c => c.Activities).HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Deal).WithMany(d => d.Activities).HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Author).WithMany().HasForeignKey(x => x.AuthorId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<CrmTask>(e =>
        {
            e.ToTable("Tasks");
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Details).HasMaxLength(4000);
            e.HasIndex(x => new { x.AssigneeId, x.IsCompleted, x.DueDate });
            e.HasOne(x => x.Contact).WithMany(c => c.Tasks).HasForeignKey(x => x.ContactId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Deal).WithMany(d => d.Tasks).HasForeignKey(x => x.DealId).OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Assignee).WithMany().HasForeignKey(x => x.AssigneeId).OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuditEntry>(e =>
        {
            e.ToTable("AuditLog");
            e.Property(x => x.Channel).HasMaxLength(30);
            e.Property(x => x.Operation).HasMaxLength(100);
            e.Property(x => x.Arguments).HasColumnType("jsonb");
            e.Property(x => x.Error).HasMaxLength(2000);
            e.HasIndex(x => x.OccurredAt);
        });
    }
}
