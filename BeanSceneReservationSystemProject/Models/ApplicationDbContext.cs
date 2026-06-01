using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace BeanSceneReservationSystemProject.Models
{
    public class ApplicationDbContext : IdentityDbContext<User>
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options) { }

        //public new DbSet<User> Users { get; set; }
        public DbSet<Member> Members { get; set; }
        public DbSet<Sitting> Sittings { get; set; }
        public DbSet<Area> Areas { get; set; }
        public DbSet<RestaurantTable> RestaurantTables { get; set; }
        public DbSet<Reservation> Reservations { get; set; }
        public DbSet<ReservationTable> ReservationTables { get; set; }
        public DbSet<ReservationStatusHistory> ReservationStatusHistories { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<User>()
                .HasIndex(u => u.Email)
                .IsUnique();

            // A real member account is a one-to-one profile on top of Identity's user record.
            modelBuilder.Entity<User>()
                .HasOne(u => u.MemberProfile)
                .WithOne(m => m.User)
                .HasForeignKey<Member>(m => m.UserId);

            // Table codes like M1 or O4 should stay unique so staff can recognise them quickly.
            modelBuilder.Entity<RestaurantTable>()
                .HasIndex(t => t.TableCode)
                .IsUnique();

            // If a reservation is removed, its table links should go with it.
            modelBuilder.Entity<ReservationTable>()
                .HasOne(rt => rt.Reservation)
                .WithMany(r => r.ReservationTables)
                .HasForeignKey(rt => rt.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<ReservationTable>()
                .HasOne(rt => rt.RestaurantTable)
                .WithMany(t => t.ReservationTables)
                .HasForeignKey(rt => rt.RestaurantTableId)
                .OnDelete(DeleteBehavior.Restrict);

            modelBuilder.Entity<ReservationTable>()
                .HasIndex(rt => new { rt.ReservationId, rt.RestaurantTableId })
                .IsUnique();

            modelBuilder.Entity<ReservationStatusHistory>()
                .HasOne(h => h.Reservation)
                .WithMany(r => r.StatusHistory)
                .HasForeignKey(h => h.ReservationId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<Reservation>()
                .HasOne(r => r.CreatedByUser)
                .WithMany(u => u.CreatedReservations)
                .HasForeignKey(r => r.CreatedByUserId)
                .OnDelete(DeleteBehavior.Restrict);


            // Default restaurant areas and tables
            modelBuilder.Entity<Area>().HasData(
                new Area { AreaId = 1, AreaName = "Main" },
                new Area { AreaId = 2, AreaName = "Outside" },
                new Area { AreaId = 3, AreaName = "Balcony" }
            );

            var tableId = 1;
            var tables = new List<RestaurantTable>();
            foreach (var codePrefix in new[] { (AreaId: 1, Prefix: "M"), (AreaId: 2, Prefix: "O"), (AreaId: 3, Prefix: "B") })
            {
                for (var i = 1; i <= 10; i++)
                {
                    tables.Add(new RestaurantTable
                    {
                        RestaurantTableId = tableId++,
                        TableCode = $"{codePrefix.Prefix}{i}",
                        AreaId = codePrefix.AreaId,
                        Capacity = 4,
                        IsAvailable = true
                    });
                }
            }
            modelBuilder.Entity<RestaurantTable>().HasData(tables);

            // Starter sittings. Can be managed or deleted by an owner/manager
            modelBuilder.Entity<Sitting>().HasData(
                new Sitting { SittingId = 1, SittingType = SittingType.Breakfast, StartDateTime = new DateTime(2026, 6, 1, 7, 0, 0), EndDateTime = new DateTime(2026, 6, 1, 11, 0, 0), Capacity = 40, IsClosed = false },
                new Sitting { SittingId = 2, SittingType = SittingType.Lunch, StartDateTime = new DateTime(2026, 6, 1, 12, 0, 0), EndDateTime = new DateTime(2026, 6, 1, 15, 0, 0), Capacity = 50, IsClosed = false },
                new Sitting { SittingId = 3, SittingType = SittingType.Dinner, StartDateTime = new DateTime(2026, 6, 1, 18, 0, 0), EndDateTime = new DateTime(2026, 6, 1, 23, 0, 0), Capacity = 60, IsClosed = false }
            );
        }
    }
}
