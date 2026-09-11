using Microsoft.EntityFrameworkCore;
using PropertySearch.Web.Models;

namespace PropertySearch.Web.Data
{
    public class PropertyDbContext : DbContext
    {
        public PropertyDbContext(DbContextOptions<PropertyDbContext> options) : base (options)
        {

        }

        public DbSet<Property> Properties => Set<Property>();
        public DbSet<Suburb> Suburbs => Set<Suburb>();
        public DbSet<Province> Provinces => Set<Province>();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<Province>(b =>
            {
                b.Property(p => p.Name).IsRequired().HasMaxLength(100);
            });

            modelBuilder.Entity<Suburb>(b =>
            {
                b.Property(s => s.Name).IsRequired().HasMaxLength(150);
                b.Property(s => s.PostalCode).HasMaxLength(10);
            });

            modelBuilder.Entity<Property>(b =>
            {
                b.Property(p => p.Title).IsRequired().HasMaxLength(200);
                b.Property(p => p.Description).HasMaxLength(2000);
                b.Property(p => p.Price).HasColumnType("decimal(18,2)");
            });
        }
    }
}
