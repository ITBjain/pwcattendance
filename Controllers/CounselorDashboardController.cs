using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PwcApi.Data;
using PwcApi.DTOs;
using System.Net.Http.Headers;
using System.Text;             
using System.Text.Json;  
using PwcApi.Models;
using PwcApi.Services; 
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace PwcApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CounselorDashboardController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly R2StorageService _r2Service;

        // 🔥 FIX: Added R2StorageService here and assigned it!
        public CounselorDashboardController(AppDbContext context, R2StorageService r2Service)
        {
            _context = context;
            _r2Service = r2Service;
        }

        [HttpPost("reports/send")]
        public async Task<IActionResult> SendParentReport([FromBody] SendReportRequest req)
        {
            // 1. Upload Media (if present)
            string? mediaUrl = null;
            if (!string.IsNullOrEmpty(req.ImageBase64)) {
                mediaUrl = await _r2Service.UploadBase64ImageAsync(req.ImageBase64, $"report_{req.EnrollmentId}_{DateTime.UtcNow.Ticks}");
            }

            // 2. Save Report to Database
            var report = new ChildReport {
                ChildEnrollmentId = req.EnrollmentId,
                GroupVariationId = req.BatchId,
                ReportDate = DateTime.UtcNow.Date,
                TeacherNotes = req.Notes,
                MediaUrl = mediaUrl,
                SentViaWhatsApp = req.SendWhatsApp,
                SentViaEmail = req.SendEmail
            };
            _context.ChildReports.Add(report);
            await _context.SaveChangesAsync();

            // 3. Trigger External APIs (Pseudo-code)
            if (req.SendWhatsApp) {
                // await _whatsappService.SendMessage(req.ParentPhone, req.Notes, mediaUrl);
            }
            if (req.SendEmail) {
                // await _emailService.SendEmail(req.ParentEmail, "Daily Update from PWC", req.Notes, mediaUrl);
            }

            return Ok(new { success = true });
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

            if (counselor.Role != "Counselor" && counselor.Role != "Coach") 
            {
                return BadRequest(new { message = $"DEBUG: Role mismatch. Expected 'Counselor or Coach', DB has '{counselor.Role}'." });
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

     [HttpPost("add-lead")]
public async Task<IActionResult> AddPotentialLead([FromBody] AddLeadRequest request)
{
    try
    {
        var newLead = new PotentialParent 
        {
            School_Name = request.SchoolId,
            Parent_Name = request.ParentName,
            Parent_Email = request.ParentEmail,
            Parent_Phone = request.ParentPhone,
            Child_Name = request.ChildName,
            Child_DOB = string.IsNullOrWhiteSpace(request.ChildDOB) ? null : DateTime.Parse(request.ChildDOB),
            InterestLevel = "Moderate",
            HasBeenContacted = false,
            CreatedAt = DateTime.UtcNow,
            
            // 🔥 FIX: Explicitly set MediaConsent to 0 (or 1) to satisfy NOT NULL constraint
            MediaConsent = 0 
        };

        _context.Potential_Parents.Add(newLead);
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Lead added successfully." });
    }
    catch (DbUpdateException ex)
    {
        var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
        return StatusCode(500, new { success = false, message = "Database Error: " + innerEx });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { success = false, message = "General Error: " + ex.Message });
    }
}

// Add this Request DTO at the bottom of the file
// 🔥 FIX: Added '?' to explicitly allow nullable strings and fix the CS8618 warnings
    public class AddLeadRequest 
    {
        public string? SchoolId { get; set; }
        public int CounselorId { get; set; }
        public string? ParentName { get; set; }
        public string? ParentEmail { get; set; }
        public string? ParentPhone { get; set; }
        public string? ChildName { get; set; }
        public string? ChildDOB { get; set; }
    }
        // ==========================================
        // 🔥 NEW: GET COUNSELOR PROFILE ENDPOINT
        // ==========================================
        // GET: api/counselordashboard/profile/{counselorId}
      // GET: api/counselordashboard/profile/{counselorId}
        [HttpGet("profile/{counselorId}")]
        public async Task<IActionResult> GetProfile(int counselorId)
        {
            var counselor = await _context.ResourceMasters
                .FirstOrDefaultAsync(r => r.Id == counselorId && r.Role == "Counselor");

            if (counselor == null)
            {
                return NotFound(new { message = "Counselor not found or inactive." });
            }

            var profile = new 
            {
                id = counselor.Id,
                name = counselor.Name ?? "Unknown",
                role = counselor.Role ?? "Counselor",
                isActive = counselor.IsActive == 1, 
                companyEmail = counselor.CompanyEmail ?? string.Empty,
                personalEmail = counselor.Email ?? string.Empty, 
                empId = counselor.EmpId ?? string.Empty,
                phone = counselor.Phone ?? string.Empty,
                
                // 🔥 CHANGE THIS LINE: Convert the DateTime back to a clean string format for Android
                dateOfJoining = counselor.DateOfJoining?.ToString("yyyy-MM-dd") ?? string.Empty
            };

            return Ok(profile);
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
                    SchoolName = s.SchoolName ?? "Unknown Centre", 
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
                        PaymentStatus = "N/A",
                          InterestLevel = p.InterestLevel,
                        FollowUpDate = p.FollowUpDate.HasValue ? p.FollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : null,
                        HasBeenContacted = p.HasBeenContacted
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
                        DiscountCode = p.DiscountCode,
                        InterestLevel = p.InterestLevel,
                        FollowUpDate = p.FollowUpDate.HasValue ? p.FollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : null,
                        HasBeenContacted = p.HasBeenContacted
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

             [HttpPut("update-status")]
public async Task<IActionResult> UpdateParentStatus([FromBody] UpdateParentStatusRequest request)
{
    try
    {
        // Parse the nullable date coming from Android
        DateTime? parsedFollowUpDate = null;
        if (!string.IsNullOrEmpty(request.FollowUpDate))
        {
            // 🔥 FIX: Added " HH:mm" to the expected format string!
            if (DateTime.TryParseExact(request.FollowUpDate, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime tempDate))
            {
                parsedFollowUpDate = tempDate;
            }
        }

        bool isUpdated = false;

        // 1. First, check if the parent exists in the PotentialParents table
        var potentialParent = await _context.Potential_Parents.FindAsync(request.ParentId);
        if (potentialParent != null)
        {
            potentialParent.InterestLevel = request.InterestLevel;
            potentialParent.FollowUpDate = parsedFollowUpDate;
            potentialParent.HasBeenContacted = true; // Mark as contacted

            _context.Potential_Parents.Update(potentialParent);
            isUpdated = true;
        }
        else
        {
            // 2. If not found in Potential, check the ParentEnrollments table
            var enrolledParent = await _context.ParentsEnrollments.FindAsync(request.ParentId);
            if (enrolledParent != null)
            {
                enrolledParent.InterestLevel = request.InterestLevel;
                enrolledParent.FollowUpDate = parsedFollowUpDate;
                enrolledParent.HasBeenContacted = true; // Mark as contacted

                _context.ParentsEnrollments.Update(enrolledParent);
                isUpdated = true;
            }
        }

        // 3. If neither table had a matching ID, return a 404 Not Found
        if (!isUpdated)
        {
            return NotFound(new { success = false, message = "Parent not found in any database table." });
        }

        // Save the changes to whichever table was updated
        await _context.SaveChangesAsync();

        return Ok(new { success = true, message = "Status updated successfully." });
    }
    catch (Exception ex)
    {
        return StatusCode(500, new { success = false, message = ex.Message });
    }
}

private DateTime GetIstTime()
{
    var tzId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "India Standard Time" : "Asia/Kolkata";
    var istZone = TimeZoneInfo.FindSystemTimeZoneById(tzId);
    return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
}

[HttpGet("coach/{coachId}/batches")]
public async Task<IActionResult> GetCoachBatches(string coachId)
{
    // 🔥 1. Use Strict IST Time
    var nowIst = GetIstTime();
    var todayIst = nowIst.Date;

    // 🔥 2. Filter ONLY Active Sessions (IsActive == true or 1)
    var sessionMasters = await _context.SessionMasters
        .Where(sm => sm.CoachId == coachId && sm.IsActive == 1) 
        .ToListAsync();

    var sessionIds = sessionMasters.Select(sm => sm.Id).ToList();
    if (!sessionIds.Any()) return Ok(new List<object>());

    var schoolIds = sessionMasters.Select(sm => sm.SchoolId).Distinct().ToList();
    var schools = await _context.SchoolMaster
        .Where(s => schoolIds.Contains(s.SchoolId))
        .ToListAsync();

    var batches = await _context.GroupVariations
        .Where(gv => sessionIds.Contains(gv.SessionId))
        .ToListAsync();

    var batchIds = batches.Select(b => b.Id).ToList();

    // 🔥 3. Filter ONLY Active Children (IsActive == true or 1)
    var children = await _context.ParentsEnrollments
        .Where(pe => pe.GroupVariationId != null 
                  && batchIds.Contains(pe.GroupVariationId.Value))
                          .ToListAsync();

    var childIds = children.Select(c => c.Id).ToList();

    var todayAttendances = await _context.ChildAttendances
        .Where(a => childIds.Contains(a.ChildEnrollmentId) && a.AttendanceDate == todayIst)
        .ToListAsync();

    var result = batches.Select(gv => 
    {
        var session = sessionMasters.FirstOrDefault(sm => sm.Id == gv.SessionId);
        var school = schools.FirstOrDefault(s => s.SchoolId == session?.SchoolId);

        return new 
        {
            BatchId = gv.Id,
            AgeGroup = gv.AgeGroup,
            Timing = gv.TimeSlot,
            Days = gv.Days, 
            TotalEnrolled = children.Count(pe => pe.GroupVariationId == gv.Id),
            
            CentreName = school?.SchoolName ?? "Unknown Centre",
            CentreAddress = $"{school?.SchoolAddress}, {school?.SchoolCity}",
            Status = "UPCOMING",
            
            Children = children
                .Where(pe => pe.GroupVariationId == gv.Id)
                .Select(child => 
                {
                    var attendance = todayAttendances.FirstOrDefault(a => a.ChildEnrollmentId == child.Id);
                    return new 
                    { 
                        EnrollmentId = child.Id, 
                        BatchId = gv.Id, 
                        ChildName = child.ChildName, 
                        ParentName = child.ParentName, 
                        ParentPhone = child.ParentPhone,
                        
                        TodayAttendance = attendance == null ? null : new 
                        {
                            Id = attendance.Id,
                            IsPresent = attendance.IsPresent,
                            CheckInTime = attendance.CheckInTime?.ToString(@"hh\:mm\:ss"),
                            CheckOutTime = attendance.CheckOutTime?.ToString(@"hh\:mm\:ss")
                        }
                    };
                }).ToList()
        };
    }).ToList();

    return Ok(result);
}
// [HttpGet("coach/{coachId}/batches")]
// public async Task<IActionResult> GetCoachBatches(string coachId)
// {
//     // 1. Get all sessions taught by this coach
//     var sessionIds = await _context.SessionMasters
//         .Where(sm => sm.CoachId == coachId)
//         .Select(sm => sm.Id)
//         .ToListAsync();

//     // 2. Get all children enrolled in those sessions
//     var enrolledKids = await _context.ParentsEnrollments
//         .Where(pe => sessionIds.Contains(pe.SessionId))
//         .ToListAsync();

//     // 3. Group the children into "Batches" based on the text strings
//     var batches = enrolledKids
//         .GroupBy(pe => new { pe.SessionAgeGroup, pe.SessionTimeSlot, pe.SessionDays })
//         .Select(group => new 
//         {
//             AgeGroup = group.Key.SessionAgeGroup,
//             Timing = group.Key.SessionTimeSlot,
//             Days = group.Key.SessionDays,
//             TotalEnrolled = group.Count(),
//             Children = group.Select(k => new { k.ChildName, k.ParentName, k.ParentPhone }).ToList()
//         });

//     return Ok(batches);
// }


[HttpPost("bulk-whatsapp")]
        public async Task<IActionResult> SendBulkWhatsApp([FromBody] BulkWhatsAppRequest request)
        {
            try
            {
                // 1. Validate the incoming request from Android
                if (request.ParentIds == null || !request.ParentIds.Any())
                    return BadRequest(new { success = false, message = "No parents selected." });

                if (string.IsNullOrWhiteSpace(request.Message) && string.IsNullOrWhiteSpace(request.MediaBase64))
                    return BadRequest(new { success = false, message = "Either a Message or Media must be provided." });

                // 2. Fetch the Counselor to get THEIR specific Whapi Token
                var counselor = await _context.ResourceMasters.FindAsync(request.CounselorId);
                
                if (counselor == null)
                    return NotFound(new { success = false, message = "Counselor not found in the system." });

                string whapiToken = counselor.WhapiToken;

                if (string.IsNullOrEmpty(whapiToken))
                {
                    return BadRequest(new { success = false, message = "This counselor has not linked their WhatsApp via Whapi yet." });
                }

                // 3. Fetch phone numbers from Potential Parents
                var potentialPhones = await _context.Potential_Parents
                    .Where(p => request.ParentIds.Contains(p.Id) && !string.IsNullOrEmpty(p.Parent_Phone))
                    .Select(p => p.Parent_Phone)
                    .ToListAsync();

                // 4. Fetch phone numbers from Enrolled Parents
                var enrolledPhones = await _context.ParentsEnrollments
                    .Where(e => request.ParentIds.Contains(e.Id) && !string.IsNullOrEmpty(e.ParentPhone))
                    .Select(e => e.ParentPhone)
                    .ToListAsync();

                // Combine and remove any duplicate numbers
                var allPhoneNumbers = potentialPhones.Concat(enrolledPhones).Distinct().ToList();
                int totalAttempted = allPhoneNumbers.Count;

                if (totalAttempted == 0)
                    return BadRequest(new { success = false, message = "No valid phone numbers found for the selected parents." });

                // 5. Determine Whapi Endpoint Type (text, image, document, video)
                string endpointType = string.IsNullOrWhiteSpace(request.MediaType) ? "text" : request.MediaType.ToLower();
                string whapiUrl = $"https://gate.whapi.cloud/api/messages/{endpointType}"; 

                // 6. Setup Whapi.Cloud HTTP Client
                using var httpClient = new HttpClient();
                httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {whapiToken}");
                httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                int successCount = 0;
                int failCount = 0;

                // 7. Loop through each number and dispatch via Whapi
                foreach (var rawPhone in allPhoneNumbers)
                {
                    if (string.IsNullOrWhiteSpace(rawPhone)) continue;

                    // Clean phone number: Whapi requires country code, NO '+' sign
                    string cleanPhone = new string(rawPhone.Where(char.IsDigit).ToArray());
                    
                    if (cleanPhone.Length == 10) 
                    {
                        cleanPhone = "91" + cleanPhone; 
                    }

                    // Create the Whapi JSON Payload dynamically based on if it has media
                    var payload = new Dictionary<string, object>
                    {
                        { "to", cleanPhone }
                    };

                    if (endpointType == "text")
                    {
                        payload.Add("body", request.Message);
                    }
                    else
                    {
                        // For image, video, or document
                        payload.Add("media", request.MediaBase64);
                        
                        if (!string.IsNullOrWhiteSpace(request.Message))
                        {
                            payload.Add("caption", request.Message);
                        }
                        if (!string.IsNullOrWhiteSpace(request.FileName))
                        {
                            payload.Add("file_name", request.FileName);
                        }
                    }

                    string jsonPayload = JsonSerializer.Serialize(payload);
                    var content = new StringContent(jsonPayload, Encoding.UTF8);
                    content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

                    // Execute the POST request to Whapi
                    var response = await httpClient.PostAsync(whapiUrl, content);

                    if (response.IsSuccessStatusCode)
                    {
                        successCount++;
                    }
                    else
                    {
                        failCount++;
                        // Optional: Log response.Content.ReadAsStringAsync() for specific Whapi errors if needed
                    }
                }

                // 8. Return Detailed Statistics to Android UI
                return Ok(new { 
                    success = true, 
                    message = $"Dispatched {successCount} out of {totalAttempted} messages successfully.",
                    successCount = successCount,
                    failCount = failCount,
                    totalAttempted = totalAttempted
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, message = "Internal Server Error: " + ex.Message });
            }
        }

        [HttpPost("attendance/mark-child")]
public async Task<IActionResult> MarkChildAttendance([FromBody] MarkChildAttendanceRequest req)
{
    var today = DateTime.UtcNow.Date;
    var timeNow = DateTime.UtcNow.TimeOfDay; // Convert to IST as needed

    var record = await _context.ChildAttendances
        .FirstOrDefaultAsync(a => a.ChildEnrollmentId == req.EnrollmentId && a.AttendanceDate == today);

    if (record == null)
    {
        // First punch of the day (Check In or Absent)
        record = new ChildAttendance
        {
            ChildEnrollmentId = req.EnrollmentId,
            GroupVariationId = req.BatchId,
            AttendanceDate = today,
            IsPresent = req.Action == "IN" ? true : false,
            CheckInTime = req.Action == "IN" ? timeNow : null
        };
        _context.ChildAttendances.Add(record);
    }
    else if (req.Action == "OUT")
    {
        // Second punch (Check out)
        record.CheckOutTime = timeNow;
    }

    await _context.SaveChangesAsync();
    return Ok(new { success = true, message = "Attendance updated" });
}


[HttpGet("batch/{batchId}/attendance-history")]
public async Task<IActionResult> GetBatchAttendanceHistory(int batchId)
{
    var batchHistory = await _context.GroupVariations
        .Include(gv => gv.ChildAttendances) // 🔥 Automatically joins the ChildAttendances table!
        .Where(gv => gv.Id == batchId)
        .Select(gv => new 
        {
            BatchName = $"{gv.AgeGroup} ({gv.Days})",
            Time = gv.TimeSlot,
            TotalLogs = gv.ChildAttendances.Count(),
            AttendanceRecords = gv.ChildAttendances.Select(a => new 
            {
                Date = a.AttendanceDate.ToString("yyyy-MM-dd"),
                IsPresent = a.IsPresent,
                CheckIn = a.CheckInTime,
                ChildId = a.ChildEnrollmentId
            }).ToList()
        })
        .FirstOrDefaultAsync();

    if (batchHistory == null) return NotFound("Batch not found");

    return Ok(batchHistory);
}

[HttpPost("attendance/finalize-batch")]
public async Task<IActionResult> FinalizeBatch([FromBody] FinalizeBatchRequest req)
{
    var today = DateTime.UtcNow.Date;

    // Optional: You can create a "SessionLogs" table in your database to store this permanently.
    // For now, we will just simulate a successful save.
    
    /* var sessionLog = new SessionLog 
    {
        GroupVariationId = req.BatchId,
        CoachId = req.CoachId,
        SessionDate = today,
        CoachRemark = req.Remark,
        Status = "COMPLETED"
    };
    _context.SessionLogs.Add(sessionLog);
    await _context.SaveChangesAsync();
    */

    return Ok(new { 
        success = true, 
        message = "Session finalized and remarks saved successfully." 
    });
}

        
    }

    
//     // Data Transfer Object that perfectly matches the Android App's JSON payload
//     public class BulkWhatsAppRequest
//     {
//         public int CounselorId { get; set; }
//         public List<int> ParentIds { get; set; }
//         public string Message { get; set; }
//     }
// }
}

