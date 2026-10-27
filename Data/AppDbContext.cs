using Microsoft.EntityFrameworkCore;
using PsySite.Models;

namespace PsySite.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Article> Articles => Set<Article>();
    public DbSet<Page> Pages => Set<Page>();
    public DbSet<ConsultationType> ConsultationTypes => Set<ConsultationType>();
    public DbSet<Slot> Slots => Set<Slot>();
    public DbSet<Booking> Bookings => Set<Booking>();
    public DbSet<Order> Orders => Set<Order>();
    public DbSet<Subscriber> Subscribers => Set<Subscriber>();

    /// <summary>Деньги храним с двумя знаками после запятой во всех базах (иначе SQL Server предупреждает).</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        c.Properties<decimal>().HavePrecision(18, 2);
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        // PostgreSQL по умолчанию требует время в UTC, а сайт хранит московское время (расписание консультаций).
        // Поэтому для PostgreSQL даты хранятся как «время без часового пояса» — ровно то, что ввели.
        if (Database.ProviderName == "Npgsql.EntityFrameworkCore.PostgreSQL")
        {
            foreach (var entity in b.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties().Where(p => p.ClrType == typeof(DateTime) || p.ClrType == typeof(DateTime?)))
                p.SetColumnType("timestamp without time zone");
        }

        b.Entity<Article>().HasIndex(a => new { a.Category, a.Slug }).IsUnique();
        b.Entity<Page>().HasIndex(p => p.Slug).IsUnique();
        b.Entity<Subscriber>().HasIndex(s => s.Email).IsUnique();
        b.Entity<Order>().HasIndex(o => o.PublicId).IsUnique();
        b.Entity<Slot>().HasIndex(s => s.StartsAt);
        b.Entity<Slot>().Ignore(s => s.EndsAt);

        b.Entity<Booking>()
            .HasOne(x => x.Slot).WithMany()
            .HasForeignKey(x => x.SlotId).OnDelete(DeleteBehavior.Restrict);
        b.Entity<Booking>()
            .HasOne(x => x.ConsultationType).WithMany()
            .HasForeignKey(x => x.ConsultationTypeId).OnDelete(DeleteBehavior.Restrict);
    }
}
