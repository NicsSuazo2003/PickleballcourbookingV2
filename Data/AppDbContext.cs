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
    public DbSet<Client> Clients => Set<Client>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Lowercase table names only (don't lowercase columns automatically)
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(entity.GetTableName()!.ToLower());
        }

        // ✅ User configuration
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.Id).HasColumnName("id");
            e.Property(u => u.Email).HasColumnName("email");
            e.Property(u => u.Name).HasColumnName("name");
            e.Property(u => u.PasswordHash).HasColumnName("password_hash");
            e.Property(u => u.Role).HasColumnName("role");
            e.Property(u => u.Status).HasColumnName("status");
            e.Property(u => u.CreatedAt).HasColumnName("created_at");
            e.Property(u => u.Phone).HasColumnName("phone");
            e.Property(u => u.Avatar).HasColumnName("avatar");
            e.Property(u => u.BookingsCount).HasColumnName("bookings_count");
        });

        // ✅ Client configuration - FIXED with underscores
        modelBuilder.Entity<Client>(e =>
        {
            e.HasKey(c => c.Id);
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Name).HasColumnName("name");
            e.Property(c => c.Subdomain).HasColumnName("subdomain");
            e.Property(c => c.LogoUrl).HasColumnName("logo_url");
            e.Property(c => c.PrimaryColor).HasColumnName("primary_color");
            e.Property(c => c.AccentColor).HasColumnName("accent_color");
            e.Property(c => c.GcashNumber).HasColumnName("gcash_number");
            e.Property(c => c.GcashAccountName).HasColumnName("gcash_account_name");
            e.Property(c => c.CreatedAt).HasColumnName("created_at");
            e.Property(c => c.Status).HasColumnName("status");

            // Relationships
            e.HasMany(c => c.Courts)
                .WithOne(crt => crt.Client)
                .HasForeignKey(crt => crt.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(c => c.Bookings)
                .WithOne(b => b.Client)
                .HasForeignKey(b => b.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ✅ Court configuration
        modelBuilder.Entity<Court>(e =>
        {
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Name).HasColumnName("name");
            e.Property(c => c.Type).HasColumnName("type");
            e.Property(c => c.Indoor).HasColumnName("indoor");
            e.Property(c => c.PricePerHour).HasColumnName("price_per_hour");
            e.Property(c => c.AmenitiesRaw).HasColumnName("amenities_raw");
            e.Property(c => c.Rating).HasColumnName("rating");
            e.Property(c => c.ImageUrl).HasColumnName("image_url");
            e.Property(c => c.ImagesRaw).HasColumnName("images_raw");
            e.Property(c => c.Status).HasColumnName("status");
            e.Property(c => c.OpenTime).HasColumnName("open_time");
            e.Property(c => c.CloseTime).HasColumnName("close_time");
            e.Property(c => c.Dimensions).HasColumnName("dimensions");
            e.Property(c => c.Surface).HasColumnName("surface");
            e.Property(c => c.ClientId).HasColumnName("client_id");

            e.HasMany(c => c.Bookings)
                .WithOne(b => b.Court)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(c => c.Client)
                .WithMany(cl => cl.Courts)
                .HasForeignKey(c => c.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(c => c.TimeSlots)
                .WithOne(ts => ts.Court)
                .HasForeignKey(ts => ts.CourtId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasMany(c => c.BlockedDates)
                .WithOne(bd => bd.Court)
                .HasForeignKey(bd => bd.CourtId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ✅ Booking configuration
        modelBuilder.Entity<Booking>(e =>
        {
            e.Property(b => b.Id).HasColumnName("id");
            e.Property(b => b.CourtId).HasColumnName("court_id");
            e.Property(b => b.ClientId).HasColumnName("client_id");
            e.Property(b => b.CustomerName).HasColumnName("customer_name");
            e.Property(b => b.CustomerEmail).HasColumnName("customer_email");
            e.Property(b => b.CustomerPhone).HasColumnName("customer_phone");
            e.Property(b => b.ReferenceCode).HasColumnName("reference_code");
            e.Property(b => b.Date).HasColumnName("date");
            e.Property(b => b.TotalAmount).HasColumnName("total_amount");
            e.Property(b => b.Status).HasColumnName("status");
            e.Property(b => b.PaymentMethod).HasColumnName("payment_method");
            e.Property(b => b.CreatedAt).HasColumnName("created_at");
            e.Property(b => b.PaymentExpiresAt).HasColumnName("payment_expires_at");
            e.Property(b => b.PaymentScreenshot).HasColumnName("payment_screenshot");
            e.Property(b => b.Notes).HasColumnName("notes");

            e.HasMany(b => b.Slots)
                .WithOne(s => s.Booking)
                .HasForeignKey(s => s.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(b => b.Court)
                .WithMany(c => c.Bookings)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(b => b.Client)
                .WithMany(cl => cl.Bookings)
                .HasForeignKey(b => b.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ✅ TimeSlot configuration
        modelBuilder.Entity<TimeSlot>(e =>
        {
            e.Property(t => t.Id).HasColumnName("id");
            e.Property(t => t.BookingId).HasColumnName("booking_id");
            e.Property(t => t.CourtId).HasColumnName("court_id");
            e.Property(t => t.Date).HasColumnName("date");
            e.Property(t => t.StartTime).HasColumnName("start_time");
            e.Property(t => t.EndTime).HasColumnName("end_time");

            e.HasOne(ts => ts.Booking)
                .WithMany(b => b.Slots)
                .HasForeignKey(ts => ts.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ts => ts.Court)
                .WithMany(c => c.TimeSlots)
                .HasForeignKey(ts => ts.CourtId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ✅ BlockedDate configuration
        modelBuilder.Entity<BlockedDate>(e =>
        {
            e.Property(b => b.Id).HasColumnName("id");
            e.Property(b => b.CourtId).HasColumnName("court_id");
            e.Property(b => b.ClientId).HasColumnName("client_id");
            e.Property(b => b.Date).HasColumnName("date");
            e.Property(b => b.StartTime).HasColumnName("start_time");
            e.Property(b => b.EndTime).HasColumnName("end_time");
            e.Property(b => b.Reason).HasColumnName("reason");
            e.Property(b => b.CreatedAt).HasColumnName("created_at");

            e.HasOne(bd => bd.Court)
                .WithMany(c => c.BlockedDates)
                .HasForeignKey(bd => bd.CourtId)
                .OnDelete(DeleteBehavior.SetNull);

            e.HasOne(bd => bd.Client)
                .WithMany()
                .HasForeignKey(bd => bd.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ✅ PriceRule configuration
        modelBuilder.Entity<PriceRule>(e =>
        {
            e.Property(p => p.Id).HasColumnName("id");
            e.Property(p => p.Name).HasColumnName("name");
            e.Property(p => p.DayOfWeek).HasColumnName("day_of_week");
            e.Property(p => p.StartTime).HasColumnName("start_time");
            e.Property(p => p.EndTime).HasColumnName("end_time");
            e.Property(p => p.PricePerHour).HasColumnName("price_per_hour");
            e.Property(p => p.IsActive).HasColumnName("is_active");
            e.Property(p => p.Priority).HasColumnName("priority");
            e.Property(p => p.ClientId).HasColumnName("client_id");

            e.HasOne(pr => pr.Client)
                .WithMany()
                .HasForeignKey(pr => pr.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Notification configuration
        modelBuilder.Entity<Notification>(e =>
        {
            e.Property(n => n.Id).HasColumnName("id");
            e.Property(n => n.UserId).HasColumnName("user_id");
            e.Property(n => n.Title).HasColumnName("title");
            e.Property(n => n.Message).HasColumnName("message");
            e.Property(n => n.Type).HasColumnName("type");
            e.Property(n => n.Read).HasColumnName("read");
            e.Property(n => n.CreatedAt).HasColumnName("created_at");

            e.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}