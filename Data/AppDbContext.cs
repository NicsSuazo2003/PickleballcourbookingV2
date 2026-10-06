using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
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
    public DbSet<OpenPlaySession> OpenPlaySessions => Set<OpenPlaySession>();
    public DbSet<OpenPlaySessionCourt> OpenPlaySessionCourts => Set<OpenPlaySessionCourt>();
    public DbSet<PricingRule> PricingRules => Set<PricingRule>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Configure DateTime to use UTC
        ConfigureDateTimeUtc(modelBuilder);

        // Table names genuinely all-lowercase in Supabase
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            entity.SetTableName(entity.GetTableName()!.ToLower());
        }

        // ── User configuration ──
        modelBuilder.Entity<User>(e =>
        {
            e.HasIndex(u => u.Email).IsUnique();
            e.Property(u => u.ClientId).HasColumnName("client_id");

            e.HasOne(u => u.Client)
                .WithMany()
                .HasForeignKey(u => u.ClientId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        // ── Client entity ──
        modelBuilder.Entity<Client>(e =>
        {
            e.Property(c => c.Id).HasColumnName("id");
            e.Property(c => c.Name).HasColumnName("name");
            e.Property(c => c.Subdomain).HasColumnName("subdomain");
            e.Property(c => c.LogoUrl).HasColumnName("logo_url");
            e.Property(c => c.PrimaryColor).HasColumnName("primary_color");
            e.Property(c => c.AccentColor).HasColumnName("accent_color");
            e.Property(c => c.GcashNumber).HasColumnName("gcash_number");
            e.Property(c => c.GcashAccountName).HasColumnName("gcash_account_name");
            e.Property(c => c.PaymentMethods)
                .HasColumnName("payment_methods")
                .HasColumnType("jsonb");

            e.Property(c => c.AvailableAmenitiesJson)
                .HasColumnName("available_amenities_json")
                .HasColumnType("jsonb");

            // ⭐ NEW — max advance booking window
            e.Property(c => c.MaxAdvanceBookingDays)
                .HasColumnName("max_advance_booking_days")
                .HasDefaultValue(90);

            e.Property(c => c.CreatedAt).HasColumnName("created_at");
            e.Property(c => c.Status).HasColumnName("status");

            e.HasMany(c => c.Courts)
                .WithOne(crt => crt.Client)
                .HasForeignKey(crt => crt.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(c => c.Bookings)
                .WithOne(b => b.Client)
                .HasForeignKey(b => b.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── PricingRule ──
        modelBuilder.Entity<PricingRule>(e =>
        {
            e.HasIndex(r => r.CourtId);
            e.HasOne(r => r.Court)
             .WithMany(c => c.PricingRules)
             .HasForeignKey(r => r.CourtId)
             .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Court ──
        modelBuilder.Entity<Court>(e =>
        {
            e.HasMany(c => c.Bookings)
                .WithOne(b => b.Court)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(c => c.ClientId).HasColumnName("client_id");
            e.HasOne(c => c.Client)
                .WithMany(cl => cl.Courts)
                .HasForeignKey(c => c.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<Court>(e =>
        {
            e.HasMany(c => c.TimeSlots)
                .WithOne(ts => ts.Court)
                .HasForeignKey(ts => ts.CourtId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Court>(e =>
        {
            e.HasMany(c => c.BlockedDates)
                .WithOne(bd => bd.Court)
                .HasForeignKey(bd => bd.CourtId)
                .OnDelete(DeleteBehavior.SetNull);
        });

        // ── Booking ──
        modelBuilder.Entity<Booking>(e =>
        {
            e.HasMany(b => b.Slots)
                .WithOne(s => s.Booking)
                .HasForeignKey(s => s.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(b => b.Court)
                .WithMany(c => c.Bookings)
                .HasForeignKey(b => b.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(b => b.ClientId).HasColumnName("client_id");
            e.HasOne(b => b.Client)
                .WithMany(cl => cl.Bookings)
                .HasForeignKey(b => b.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(b => b.OpenPlaySessionId).HasColumnName("OpenPlaySessionId");
            e.HasOne(b => b.OpenPlaySession)
                .WithMany(s => s.Bookings)
                .HasForeignKey(b => b.OpenPlaySessionId)
                .OnDelete(DeleteBehavior.SetNull);

            e.Property(b => b.Date)
                .HasConversion(new DateTimeToUtcConverter());
        });

        // ── TimeSlot ──
        modelBuilder.Entity<TimeSlot>(e =>
        {
            e.HasOne(ts => ts.Booking)
                .WithMany(b => b.Slots)
                .HasForeignKey(ts => ts.BookingId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(ts => ts.Court)
                .WithMany(c => c.TimeSlots)
                .HasForeignKey(ts => ts.CourtId)
                .OnDelete(DeleteBehavior.SetNull);

            e.Property(ts => ts.Date)
                .HasConversion(new DateTimeToUtcConverter());
        });

        // ── BlockedDate ──
        modelBuilder.Entity<BlockedDate>(e =>
        {
            e.HasOne(bd => bd.Court)
                .WithMany(c => c.BlockedDates)
                .HasForeignKey(bd => bd.CourtId)
                .OnDelete(DeleteBehavior.SetNull);

            e.Property(bd => bd.ClientId).HasColumnName("client_id");
            e.HasOne(bd => bd.Client)
                .WithMany()
                .HasForeignKey(bd => bd.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(bd => bd.Date)
                .HasConversion(new DateTimeToUtcConverter());
        });

        // ── PriceRule ──
        modelBuilder.Entity<PriceRule>(e =>
        {
            e.Property(pr => pr.ClientId).HasColumnName("client_id");
            e.HasOne(pr => pr.Client)
                .WithMany()
                .HasForeignKey(pr => pr.ClientId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── Notification ──
        modelBuilder.Entity<Notification>(e =>
        {
            e.HasOne(n => n.User)
                .WithMany(u => u.Notifications)
                .HasForeignKey(n => n.UserId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // ── OpenPlaySession ──
        modelBuilder.Entity<OpenPlaySession>(e =>
        {
            e.Property(s => s.ClientId).HasColumnName("ClientId");
            e.Property(s => s.CourtId).HasColumnName("CourtId");
            e.Property(s => s.Date).HasColumnName("Date");
            e.Property(s => s.StartTime).HasColumnName("StartTime");
            e.Property(s => s.EndTime).HasColumnName("EndTime");
            e.Property(s => s.MaxPlayers).HasColumnName("MaxPlayers");
            e.Property(s => s.CurrentPlayers).HasColumnName("CurrentPlayers");
            e.Property(s => s.PricePerPlayer)
                .HasColumnName("PricePerPlayer")
                .HasColumnType("decimal(10,2)");
            e.Property(s => s.SkillLevel).HasColumnName("SkillLevel");
            e.Property(s => s.HostName).HasColumnName("HostName");
            e.Property(s => s.Title).HasColumnName("Title");
            e.Property(s => s.Description).HasColumnName("Description");
            e.Property(s => s.IsActive).HasColumnName("IsActive");
            e.Property(s => s.CreatedAt).HasColumnName("CreatedAt");

            e.HasOne(s => s.Client)
                .WithMany()
                .HasForeignKey(s => s.ClientId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasOne(s => s.Court)
                .WithMany()
                .HasForeignKey(s => s.CourtId)
                .OnDelete(DeleteBehavior.Cascade);

            e.HasMany(s => s.SessionCourts)
                .WithOne(sc => sc.OpenPlaySession)
                .HasForeignKey(sc => sc.OpenPlaySessionId)
                .OnDelete(DeleteBehavior.Cascade);

            e.Property(s => s.Date)
                .HasConversion(new DateTimeToUtcConverter());
        });

        // ── OpenPlaySessionCourt ──
        modelBuilder.Entity<OpenPlaySessionCourt>(e =>
        {
            e.Property(sc => sc.OpenPlaySessionId).HasColumnName("OpenPlaySessionId");
            e.Property(sc => sc.CourtId).HasColumnName("CourtId");
            e.Property(sc => sc.CreatedAt).HasColumnName("CreatedAt");

            e.HasIndex(sc => new { sc.OpenPlaySessionId, sc.CourtId }).IsUnique();

            e.HasOne(sc => sc.Court)
                .WithMany()
                .HasForeignKey(sc => sc.CourtId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }

    private void ConfigureDateTimeUtc(ModelBuilder modelBuilder)
    {
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == typeof(DateTime) || property.ClrType == typeof(DateTime?))
                {
                    property.SetDefaultValueSql("NOW() AT TIME ZONE 'UTC'");
                }
            }
        }
    }
}

// Converts DateTime to always be UTC on save and read
public class DateTimeToUtcConverter : ValueConverter<DateTime, DateTime>
{
    public DateTimeToUtcConverter()
        : base(
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc),
            v => v.Kind == DateTimeKind.Utc ? v : DateTime.SpecifyKind(v, DateTimeKind.Utc))
    {
    }
}