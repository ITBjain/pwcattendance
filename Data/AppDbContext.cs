using Microsoft.EntityFrameworkCore;
using PwcApi.Models;

namespace PwcApi.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // REMOVED: public DbSet<CoachAttendance> CoachAttendances { get; set; }
        
        // NEW ATTENDANCE TABLE
        public DbSet<ResourceAttendance> ResourceAttendances { get; set; }
        
        // Existing Dashboard Tables
        public DbSet<ResourceMaster> ResourceMasters { get; set; }
        public DbSet<SchoolMaster> SchoolMaster { get; set; }
        public DbSet<PotentialParent> Potential_Parents { get; set; }
        public DbSet<ParentsEnrollment> ParentsEnrollments { get; set; } 
    }
}