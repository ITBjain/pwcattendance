using Microsoft.EntityFrameworkCore;
using PwcApi.Models;

namespace PwcApi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<CoachAttendance> CoachAttendances { get; set; } // Existing
        
        // New Tables added for Dashboard
        public DbSet<ResourceMaster> ResourceMasters { get; set; }
        public DbSet<SchoolMaster> SchoolMaster { get; set; }
        public DbSet<PotentialParent> Potential_Parents { get; set; }
        public DbSet<ParentsEnrollment> ParentsEnrollments { get; set; }
    }
}