using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Tracker.Data;

public class ApplicationDbContext : IdentityDbContext<IdentityUser>
{
    public ApplicationDbContext(
        DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<SavedResult> SavedResults { get; set; }

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<SavedResult>(entity =>
        {
            entity.ToTable("saved_results");

            entity.HasKey(s => s.Id);

            entity.Property(s => s.Id)
                .HasColumnName("id");

            entity.Property(s => s.UserId)
                .HasColumnName("user_id");

            entity.Property(s => s.ResultId)
                .HasColumnName("result_id");
        });
    }
}