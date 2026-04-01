using Microsoft.EntityFrameworkCore;
using PwcApi.Models;

namespace PwcApi.Data{
    public class AppDbContext: DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<CoachAttendance> CoachAttendances {get; set;}
    }
}
