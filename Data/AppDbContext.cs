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
    public DbSet<Client> Clients => Set<Client>(); // ✅ NEW

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

        // ✅ Client - Court relationship
        modelBuilder.Entity<Client>(e =>
        {
            e.HasMany(c => c.Courts)
                .WithOne(crt => crt.Client)
                .HasForeignKey(crt => crt.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ✅ Client - Booking relationship
        modelBuilder.Entity<Client>(e =>
        {
            e.HasMany(c => c.Bookings)
                .WithOne(b => b.Client)
                .HasForeignKey(b => b.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ✅ Court - Booking relationship
        modelBuilder.Entity<Court>(e =>
        {
            e.HasMany(c => c.Bookings)
                .WithOne(b => b.Court)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            // ✅ Court belongs to Client
            e.HasOne(c => c.Client)
                .WithMany(cl => cl.Courts)
                .HasForeignKey(c => c.ClientId)
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

            // ✅ Booking belongs to Client
            e.HasOne(b => b.Client)
                .WithMany(cl => cl.Bookings)
                .HasForeignKey(b => b.ClientId)
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

            // ✅ BlockedDate belongs to Client (optional - if you want client-level blocking)
            e.HasOne(bd => bd.Client)
                .WithMany()
                .HasForeignKey(bd => bd.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ✅ PriceRule configuration
        modelBuilder.Entity<PriceRule>(e =>
        {
            // PriceRule belongs to Client
            e.HasOne(pr => pr.Client)
                .WithMany()
                .HasForeignKey(pr => pr.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
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