using Microsoft.EntityFrameworkCore;
using PickleballBookingSystem.Entities;

namespace PickleballBookingSystem.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users => Set<User>();
    public DbSet<Court> Courts => Set<Court>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<TimeSlot> TimeSlots => Set<TimeSlot>();
    public DbSet<Notification> Notifications => Set<Notification>();
    public DbSet<BlockedDate> BlockedDates => Set<BlockedDate>();
    public DbSet<PriceRule> PriceRules => Set<PriceRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Lowercase table names
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(entity.GetTableName()!.ToLower());
        }

        // User configuration
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
        });

        // ✅ Court - Booking relationship
        modelBuilder.Entity<Court>(e =>
        {
            e.HasMany(c => c.Bookings)
                .WithOne(b => b.Court)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ✅ Court - TimeSlot relationship
        modelBuilder.Entity<Court>(e =>
        {
            e.HasMany(c => c.TimeSlots)
                .WithOne(ts => ts.Court)
                .HasForeignKey(ts => ts.CourtId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ✅ Court - BlockedDate relationship
        modelBuilder.Entity<Court>(e =>
        {
            e.HasMany(c => c.BlockedDates)
                .WithOne(bd => bd.Court)
                .HasForeignKey(bd => bd.CourtId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ✅ Booking - TimeSlot relationship
        modelBuilder.Entity<Booking>(e =>
        {
            e.HasMany(b => b.Slots)
                .WithOne(s => s.Booking)
                .HasForeignKey(s => s.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // ✅ Booking belongs to Court
            e.HasOne(b => b.Court)
                .WithMany(c => c.Bookings)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ✅ TimeSlot configuration
        modelBuilder.Entity<TimeSlot>(e =>
        {
            // TimeSlot belongs to Booking
            e.HasOne(ts => ts.Booking)
                .WithMany(b => b.Slots)
                .HasForeignKey(ts => ts.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            // TimeSlot belongs to Court (optional)
            e.HasOne(ts => ts.Court)
                .WithMany(c => c.TimeSlots)
                .HasForeignKey(ts => ts.CourtId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ✅ BlockedDate configuration
        modelBuilder.Entity<BlockedDate>(e =>
        {
            // BlockedDate belongs to Court (optional)
            e.HasOne(bd => bd.Court)
                .WithMany(c => c.BlockedDates)
                .HasForeignKey(bd => bd.CourtId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // Notification configuration
        modelBuilder.Entity<Notification>(e =>
        {
            e.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}