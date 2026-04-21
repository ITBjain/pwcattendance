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
    }
}