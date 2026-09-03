using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace FridgeManager.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : IdentityDbContext<ApplicationUser>(options)
{
    public DbSet<Refrigerator> Refrigerators => Set<Refrigerator>();
    public DbSet<Shelf> Shelves => Set<Shelf>();
    public DbSet<FoodItem> FoodItems => Set<FoodItem>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<ApplicationUser>(entity =>
        {
            entity.Property(u => u.ItemQuota).HasDefaultValue(5);
            entity.Property(u => u.IsActive).HasDefaultValue(true);
        });

        builder.Entity<Refrigerator>(entity =>
        {
            entity.Property(r => r.Name).HasMaxLength(100).IsRequired();
        });

        builder.Entity<Shelf>(entity =>
        {
            entity.Property(s => s.Name).HasMaxLength(100).IsRequired();
            entity.HasOne(s => s.Refrigerator)
                .WithMany(r => r.Shelves)
                .HasForeignKey(s => s.RefrigeratorId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        builder.Entity<FoodItem>(entity =>
        {
            entity.Property(f => f.Name).HasMaxLength(200).IsRequired();
            entity.Property(f => f.OwnerId).HasMaxLength(450).IsRequired();
            entity.Property(f => f.PositionNote).HasMaxLength(200);
            entity.Property(f => f.Note).HasMaxLength(1000);
            entity.Property(f => f.ImagePath).HasMaxLength(500);
            entity.Property(f => f.Category).HasConversion<string>().HasMaxLength(32);
            entity.Property(f => f.Status).HasConversion<string>().HasMaxLength(32);

            entity.HasIndex(f => f.Status);
            entity.HasIndex(f => f.ShelfId);
            entity.HasIndex(f => f.OwnerId);

            entity.HasOne(f => f.Owner)
                .WithMany()
                .HasForeignKey(f => f.OwnerId)
                .OnDelete(DeleteBehavior.Restrict);

            entity.HasOne(f => f.Shelf)
                .WithMany(s => s.Items)
                .HasForeignKey(f => f.ShelfId)
                .OnDelete(DeleteBehavior.Restrict);
        });
    }
}
