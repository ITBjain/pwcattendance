using Microsoft.EntityFrameworkCore;
using PwcApi.Models; 

namespace PwcApi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<ResourceAttendance> ResourceAttendances { get; set; }
        public DbSet<ResourceMaster> ResourceMasters { get; set; }
        public DbSet<SchoolMaster> SchoolMaster { get; set; }
        public DbSet<PotentialParent> Potential_Parents { get; set; }
        public DbSet<ParentsEnrollment> ParentsEnrollments { get; set; } 
        
        // 🔥 THIS IS THE FIX FOR LINE 21:
        public DbSet<SessionMaster> SessionMasters { get; set; } 
        public DbSet<AttendanceLog> CallReport { get; set;}

        public DbSet<InteractionLog> InteractionLogs { get; set;}

// Ensure both of these exist
    public DbSet<GroupVariation> GroupVariations { get; set; }
    public DbSet<ChildAttendance> ChildAttendances { get; set; }
    public DbSet<ChildReport> ChildReports { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Tell EF Core explicitly about the relationship (Optional but recommended)
        modelBuilder.Entity<ChildAttendance>()
            .HasOne(a => a.GroupVariation)
            .WithMany(g => g.ChildAttendances)
            .HasForeignKey(a => a.GroupVariationId)
            .OnDelete(DeleteBehavior.Cascade); // If a batch is deleted, its attendance logs are deleted
    }    }
}