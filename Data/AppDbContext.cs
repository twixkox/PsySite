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

    protected override void OnModelCreating(ModelBuilder b)
    {
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
