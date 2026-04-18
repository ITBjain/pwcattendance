using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using PwcApi.DTOs;
using System.Linq;
using System.Threading.Tasks;

namespace PwcApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CounselorDashboardController : ControllerBase
    {
        private readonly AppDbContext _context;

        public CounselorDashboardController(AppDbContext context)
        {
            _context = context;
        }

        // POST: api/counselordashboard/login
        [HttpPost("login")]
public async Task<IActionResult> Login([FromBody] LoginRequest request)
{
    // Step 1: Just try to find the email. Ignore password and role for a second.
    var counselor = await _context.ResourceMasters
        .FirstOrDefaultAsync(r => r.CompanyEmail == request.Email);

    // If it fails here, the email is wrong or there's a space in the DB email
    if (counselor == null) 
    {
        return BadRequest(new { message = $"DEBUG: The email '{request.Email}' was not found in the database." });
    }

    // Step 2: Check the password
    if (counselor.Password != request.Password) 
    {
        return BadRequest(new { message = $"DEBUG: Password mismatch. DB has '{counselor.Password}', you sent '{request.Password}'." });
    }

    // Step 3: Check the Role
    if (counselor.Role != "Counselor") 
    {
        return BadRequest(new { message = $"DEBUG: Role mismatch. Expected 'Counselor', DB has '{counselor.Role}'." });
    }

    // Step 4: Check IsActive
    if (counselor.IsActive != 1) 
    {
        return BadRequest(new { message = $"DEBUG: IsActive is {counselor.IsActive}, but we expected 1." });
    }

    // If it passes all the above, it's a success!
    return Ok(new LoginResponse { 
        CounselorId = counselor.Id, 
        Name = counselor.Name ?? "Unknown", 
        EmpId = counselor.EmpId ?? "Unknown" 
    });
}

        // GET: api/counselordashboard/centres/{counselorId}
        [HttpGet("centres/{counselorId}")]
        public async Task<IActionResult> GetCentres(int counselorId)
        {
            var schools = await _context.SchoolMaster
                .Where(s => s.CounselorId == counselorId)
                .Select(s => new CentreResponse 
                { 
                    SchoolId = s.SchoolId, 
                    SchoolName = s.SchoolName, 
                    Address = $"{s.SchoolCity}, {s.SchoolAddress}", 
                    ContactName = s.ContactPersonName ?? "N/A", 
                    ContactPhone = s.ContactPersonPhone ?? "N/A"
                })
                .ToListAsync();

            if (!schools.Any())
            {
                return NotFound(new { message = "No centres assigned to this counselor." });
            }

            return Ok(schools);
        }

     // GET: api/counselordashboard/parents/{schoolId}
[HttpGet("parents/{schoolId}")]
public async Task<IActionResult> GetParents(string schoolId)
{
    try
    {
        // 1. Clean the incoming ID to remove any accidental spaces
        var cleanSchoolId = schoolId.Trim();

        // 2. Fetch Potential Parents (Matching exact trimmed ID)
        var potentialParents = await _context.Potential_Parents
            .Where(p => p.School_Name != null && p.School_Name.Trim() == cleanSchoolId) 
            .Select(p => new ParentResponse 
            { 
                ParentName = p.Parent_Name ?? "Unknown", 
                ParentPhone = p.Parent_Phone ?? "N/A", 
                ChildName = p.Child_Name ?? "Unknown", 
                Status = "Potential",
                PaymentStatus = "N/A"
            })
            .ToListAsync();

        // 3. Fetch Enrolled Parents (Matching exact trimmed ID)
        var enrolledParents = await _context.ParentsEnrollments
            .Where(p => p.SchoolId != null && p.SchoolId.Trim() == cleanSchoolId)
            .Select(p => new ParentResponse 
            { 
                ParentName = p.ParentName ?? "Unknown", 
                ParentPhone = p.ParentPhone ?? "N/A", 
                ChildName = p.ChildName ?? "Unknown", 
                Status = "Enrolled",
                PaymentStatus = p.PaymentStatus ?? "Pending"
            })
            .ToListAsync();

        // 4. Combine both lists
        var allParents = potentialParents.Concat(enrolledParents).OrderBy(p => p.Status).ToList();

        return Ok(allParents);
    }
    catch (Exception ex)
    {
        // If Hostinger's Linux MySQL rejects the table name (e.g., case sensitivity issue), 
        // this will catch it and print the exact error instead of failing silently.
        return StatusCode(500, new { message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
    }
}
    }
}