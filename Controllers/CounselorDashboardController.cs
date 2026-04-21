using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using PwcApi.DTOs;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;

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
            var counselor = await _context.ResourceMasters
                .FirstOrDefaultAsync(r => r.CompanyEmail == request.Email);

            if (counselor == null) 
            {
                return BadRequest(new { message = $"DEBUG: The email '{request.Email}' was not found in the database." });
            }

            if (counselor.Password != request.Password) 
            {
                return BadRequest(new { message = $"DEBUG: Password mismatch. DB has '{counselor.Password}', you sent '{request.Password}'." });
            }

            if (counselor.Role != "Counselor") 
            {
                return BadRequest(new { message = $"DEBUG: Role mismatch. Expected 'Counselor', DB has '{counselor.Role}'." });
            }

            if (counselor.IsActive != 1) 
            {
                return BadRequest(new { message = $"DEBUG: IsActive is {counselor.IsActive}, but we expected 1." });
            }

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
            // Keeps the list view lightweight
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

        // GET: api/counselordashboard/centre/{schoolId}
        // NEW ENDPOINT: Returns complete information for a specific centre
        [HttpGet("centre/{schoolId}")]
        public async Task<IActionResult> GetCentreDetails(string schoolId)
        {
            var s = await _context.SchoolMaster.FirstOrDefaultAsync(x => x.SchoolId == schoolId.Trim());

            if (s == null)
            {
                return NotFound(new { success = false, message = "Centre not found." });
            }

            // Map database model into clean structured JSON response
            var response = new 
            {
                success = true,
                data = new 
                {
                    schoolId = s.SchoolId,
                    schoolName = s.SchoolName,
                    schoolEmail = s.SchoolEmail,
                    schoolPhone = s.SchoolPhone,
                    onBoardingId = s.OnBoardingId,
                    createdDate = s.CreatedDate,
                    billingAddress = s.BillingAddress,
                    contactPersonName = s.ContactPersonName,
                    contactPersonEmail = s.ContactPersonEmail,
                    contactPersonPhone = s.ContactPersonPhone,
                    ownerName = s.OwnerName,
                    ownerEmail = s.OwnerEmail,
                    ownerPhone = s.OwnerPhone,
                    principalName = s.PrincipalName,
                    principalEmail = s.PrincipalEmail,
                    principalPhone = s.PrincipalPhone,
                    schoolAddress = s.SchoolAddress,
                    schoolCity = s.SchoolCity,
                    schoolCountry = s.SchoolCountry,
                    schoolLocation = s.SchoolLocation,
                    schoolPincode = s.SchoolPincode,
                    schoolState = s.SchoolState,
                    shippingAddress = s.ShippingAddress,
                    plan = s.Plan,
                    isEnrolled = s.IsEnrolled,
                    counselorId = s.CounselorId,
                    facilitationCharges = s.FacilitationCharges,
                    salesPersonId = s.SalesPersonId,
                    sessionContacts = new[] 
                    {
                        new { name = s.SessionContact1Name, email = s.SessionContact1Email, phone = s.SessionContact1Phone, position = s.SessionContact1Position },
                        new { name = s.SessionContact2Name, email = s.SessionContact2Email, phone = s.SessionContact2Phone, position = s.SessionContact2Position },
                        new { name = s.SessionContact3Name, email = s.SessionContact3Email, phone = s.SessionContact3Phone, position = s.SessionContact3Position },
                        new { name = s.SessionContact4Name, email = s.SessionContact4Email, phone = s.SessionContact4Phone, position = s.SessionContact4Position }
                    },
                    activityCount = s.ActivityCount,
                    promoCode = s.PromoCode,
                    promoDiscount = s.PromoDiscount
                }
            };

            return Ok(response);
        }

        // GET: api/counselordashboard/sessions/{schoolId}
        [HttpGet("sessions/{schoolId}")]
        public async Task<IActionResult> GetSessionsForSchool(string schoolId)
        {
            try
            {
                var cleanSchoolId = schoolId.Trim();

                var sessions = await _context.SessionMasters
                    .Where(s => s.SchoolId == cleanSchoolId)
                    .Select(s => new 
                    { 
                        sessionId = s.Id,
                        sessionName = s.Id 
                    })
                    .ToListAsync();

                if (!sessions.Any())
                {
                    return NotFound(new { message = "No active sessions found for this school." });
                }

                return Ok(sessions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
            }
        }

      // GET: api/counselordashboard/parents/{schoolId}
[HttpGet("parents/{schoolId}")]
public async Task<IActionResult> GetParents(string schoolId)
{
    try
    {
        var cleanSchoolId = schoolId.Trim();

        // 1. Fetch Complete Potential Parents
        var potentialParents = await _context.Potential_Parents
            .Where(p => p.School_Name != null && p.School_Name.Trim() == cleanSchoolId) 
            .Select(p => new ParentResponse 
            { 
                Id = p.Id,
                Status = "Potential",
                ParentName = p.Parent_Name, 
                ParentEmail = p.Parent_Email,
                ParentPhone = p.Parent_Phone, 
                ChildName = p.Child_Name, 
                ChildDOB = p.Child_DOB,
                ChildSchoolName = p.ChildSchoolName,
                CreatedAt = p.CreatedAt,
                MediaConsent = p.MediaConsent,
                Remark = p.Remark,
                
                // Enrollment fields default to null/empty for Potential parents
                PaymentStatus = "N/A" 
            })
            .ToListAsync();

        // 2. Fetch Complete Enrolled Parents
        var enrolledParents = await _context.ParentsEnrollments
            .Where(p => p.SchoolId != null && p.SchoolId.Trim() == cleanSchoolId)
            .Select(p => new ParentResponse 
            { 
                Id = p.Id,
                Status = "Enrolled",
                ParentName = p.ParentName, 
                ParentEmail = p.ParentEmail,
                ParentPhone = p.ParentPhone, 
                ChildName = p.ChildName, 
                ChildDOB = p.ChildDOB,
                ChildSchoolName = p.ChildSchoolName,
                ChildSchoolCity = p.ChildSchoolCity,
                CreatedAt = p.CreatedAt,
                MediaConsent = p.MediaConsent,
                
                // Payment & Billing
                PaymentStatus = p.PaymentStatus ?? "Pending",
                PaymentDate = p.PaymentDate,
                PaymentAmount = p.PaymentAmount,
                BillingAddress = p.BillingAddress,
                BillingCity = p.BillingCity,
                BillingState = p.BillingState,
                BillingPincode = p.BillingPincode,
                
                // Sessions
                SessionId = p.SessionId,
                SessionName = p.SessionName,
                SessionAgeGroup = p.SessionAgeGroup,
                SessionDays = p.SessionDays,
                SessionFrequency = p.SessionFrequency,
                SessionTimeSlot = p.SessionTimeSlot,
                
                // Discounts
                DiscountAmount = p.DiscountAmount,
                DiscountCode = p.DiscountCode
            })
            .ToListAsync();

        // 3. Combine and sort
        var allParents = potentialParents.Concat(enrolledParents).OrderBy(p => p.Status).ToList();

        return Ok(new { success = true, data = allParents });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { success = false, message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
    }
}
    }
}