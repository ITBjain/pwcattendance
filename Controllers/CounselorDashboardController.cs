
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PwcApi.Data;
using PwcApi.DTOs;
using PwcApi.Models;
using PwcApi.Services;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace PwcApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class CounselorDashboardController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly R2StorageService _r2Service;
        private readonly WhapiClient _whapi;
        private readonly ILogger<CounselorDashboardController> _logger;

        public CounselorDashboardController(AppDbContext context, R2StorageService r2Service,
                                            WhapiClient whapi, ILogger<CounselorDashboardController> logger)
        {
            _context = context;
            _r2Service = r2Service;
            _whapi = whapi;
            _logger = logger;
        }

        private ObjectResult ApiError(int statusCode, string code, string message) =>
            StatusCode(statusCode, new { success = false, code, message });

        // =====================================================================
        // AUTH & PROFILE
        // =====================================================================

        // POST: api/counselordashboard/login
        // 🔒 SECURITY FIX: the old version returned the real password from the DB in its error message.
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            const string invalid = "Invalid email or password.";
            var email = request?.Email?.Trim() ?? "";
            if (email.Length == 0 || string.IsNullOrEmpty(request?.Password))
                return ApiError(400, "INVALID_CREDENTIALS", invalid);

            var counselor = await _context.ResourceMasters.FirstOrDefaultAsync(r => r.CompanyEmail == email);

            if (counselor == null || counselor.Password != request!.Password)
                return ApiError(401, "INVALID_CREDENTIALS", invalid);

            if (counselor.Role != "Counselor" && counselor.Role != "Coach")
                return ApiError(403, "ROLE_NOT_ALLOWED", "This account doesn't have access to the app.");

            if (counselor.IsActive != 1)
                return ApiError(403, "ACCOUNT_INACTIVE", "Your account is inactive. Please contact the admin.");

            return Ok(new LoginResponse
            {
                CounselorId = counselor.Id,
                Name = counselor.Name ?? "Unknown",
                EmpId = counselor.EmpId ?? "Unknown",
                Role = counselor.Role ?? ""
            });
        }

        // GET: api/counselordashboard/profile/{counselorId}
        [HttpGet("profile/{counselorId}")]
        public async Task<IActionResult> GetProfile(int counselorId)
        {
            var counselor = await _context.ResourceMasters
                .FirstOrDefaultAsync(r => r.Id == counselorId && (r.Role == "Counselor" || r.Role == "Coach"));

            if (counselor == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Counselor not found or inactive." });

            return Ok(new
            {
                id = counselor.Id,
                name = counselor.Name ?? "Unknown",
                role = counselor.Role ?? "Counselor",
                isActive = counselor.IsActive == 1,
                companyEmail = counselor.CompanyEmail ?? string.Empty,
                personalEmail = counselor.Email ?? string.Empty,
                empId = counselor.EmpId ?? string.Empty,
                phone = counselor.Phone ?? string.Empty,
                dateOfJoining = counselor.DateOfJoining?.ToString("yyyy-MM-dd") ?? string.Empty
            });
        }

        // =====================================================================
        // LEADS
        // =====================================================================

        [HttpPost("add-lead")]
        public async Task<IActionResult> AddPotentialLead([FromBody] AddLeadRequest request)
        {
            try
            {
                DateTime? dob = null;
                if (!string.IsNullOrWhiteSpace(request.ChildDOB) &&
                    DateTime.TryParse(request.ChildDOB, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDob))
                    dob = parsedDob;

                var newLead = new PotentialParent
                {
                    School_Name = request.SchoolId,
                    Parent_Name = request.ParentName,
                    Parent_Email = request.ParentEmail,
                    Parent_Phone = request.ParentPhone,
                    Child_Name = request.ChildName,
                    Child_DOB = dob,
                    InterestLevel = "Moderate",
                    HasBeenContacted = false,
                    CreatedAt = IstClock.Now,
                    MediaConsent = 0
                };

                _context.Potential_Parents.Add(newLead);
                await _context.SaveChangesAsync();

                return Ok(new { success = true, message = "Lead added successfully." });
            }
            catch (DbUpdateException ex)
            {
                var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return StatusCode(500, new { success = false, code = "DB_ERROR", message = "Database Error: " + innerEx });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, code = "SERVER_ERROR", message = "General Error: " + ex.Message });
            }
        }

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

        // =====================================================================
        // CENTRES
        // =====================================================================

        // GET: api/counselordashboard/centres/{userId}
        // Returns ALL centres, assigned ones first (isAssigned = true). Now also returns coordinates for geofencing.
        [HttpGet("centres/{userId}")]
        public async Task<IActionResult> GetCentres(int userId)
        {
            try
            {
                var counselorSchoolIds = await _context.SchoolMaster
                    .Where(s => s.CounselorId == userId)
                    .Select(s => s.SchoolId)
                    .ToListAsync();

                // 🔥 FIX: removed "|| sm.Id == userId" (it compared a SESSION id with a USER id)
                var coachIdStr = userId.ToString();
                var coachSchoolIds = await _context.SessionMasters
                    .Where(sm => sm.CoachId == coachIdStr && sm.IsActive == 1)
                    .Select(sm => sm.SchoolId)
                    .Distinct()
                    .ToListAsync();

                var assigned = new HashSet<string>(
                    counselorSchoolIds.Concat(coachSchoolIds.Where(id => id != null).Select(id => id!)));

                var allSchools = await _context.SchoolMaster
                    .Select(s => new
                    {
                        s.SchoolId,
                        SchoolName = s.SchoolName ?? "Unknown Centre",
                        s.SchoolCity,
                        s.SchoolAddress,
                        ContactPersonName = s.ContactPersonName ?? "N/A",
                        ContactPersonPhone = s.ContactPersonPhone ?? "N/A",
                        s.Coordinates,      // 🔥 NEW: "28.6254,77.3808" – used by the app's geofence
                        s.SchoolLocation
                    })
                    .ToListAsync();

                var ordered = allSchools
                    .Select(s => new
                    {
                        s.SchoolId,
                        s.SchoolName,
                        s.SchoolCity,
                        s.SchoolAddress,
                        s.ContactPersonName,
                        s.ContactPersonPhone,
                        s.Coordinates,
                        s.SchoolLocation,
                        IsAssigned = assigned.Contains(s.SchoolId)
                    })
                    .OrderByDescending(s => s.IsAssigned)
                    .ThenBy(s => s.SchoolName)
                    .ToList();

                return Ok(ordered);
            }
            catch (Exception ex)
            {
                var innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return StatusCode(500, new { success = false, code = "SERVER_ERROR", message = $"Error fetching centres: {innerMsg}" });
            }
        }

        // GET: api/counselordashboard/centre/{schoolId}
        [HttpGet("centre/{schoolId}")]
        public async Task<IActionResult> GetCentreDetails(string schoolId)
        {
            var s = await _context.SchoolMaster.FirstOrDefaultAsync(x => x.SchoolId == schoolId.Trim());

            if (s == null)
                return NotFound(new { success = false, code = "NOT_FOUND", message = "Centre not found." });

            return Ok(new
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
                    coordinates = s.Coordinates,
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
            });
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
                    .Select(s => new { sessionId = s.Id, sessionName = s.SessionName ?? s.Id.ToString() })
                    .ToListAsync();

                if (!sessions.Any())
                    return NotFound(new { success = false, code = "NOT_FOUND", message = "No active sessions found for this school." });

                return Ok(sessions);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, code = "DB_ERROR", message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
            }
        }

        // =====================================================================
        // PARENTS / CRM
        // =====================================================================

        // GET: api/counselordashboard/parents/{schoolId}
        [HttpGet("parents/{schoolId}")]
        public async Task<IActionResult> GetParents(string schoolId)
        {
            try
            {
                var cleanSchoolId = schoolId.Trim();

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
                        PaymentStatus = "N/A",
                        InterestLevel = p.InterestLevel,
                        FollowUpDate = p.FollowUpDate.HasValue ? p.FollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : null,
                        HasBeenContacted = p.HasBeenContacted
                    })
                    .ToListAsync();

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
                        PaymentStatus = p.PaymentStatus ?? "Pending",
                        PaymentDate = p.PaymentDate,
                        PaymentAmount = p.PaymentAmount,
                        BillingAddress = p.BillingAddress,
                        BillingCity = p.BillingCity,
                        BillingState = p.BillingState,
                        BillingPincode = p.BillingPincode,
                        SessionId = p.SessionId,
                        SessionName = p.SessionName,
                        SessionAgeGroup = p.SessionAgeGroup,
                        SessionDays = p.SessionDays,
                        SessionFrequency = p.SessionFrequency,
                        SessionTimeSlot = p.SessionTimeSlot,
                        DiscountAmount = p.DiscountAmount,
                        DiscountCode = p.DiscountCode,
                        InterestLevel = p.InterestLevel,
                        FollowUpDate = p.FollowUpDate.HasValue ? p.FollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : null,
                        HasBeenContacted = p.HasBeenContacted
                    })
                    .ToListAsync();

                var allParents = potentialParents.Concat(enrolledParents).OrderBy(p => p.Status).ToList();
                return Ok(new { success = true, data = allParents });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, code = "DB_ERROR", message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
            }
        }

        // PUT: api/counselordashboard/update-status
        [HttpPut("update-status")]
        public async Task<IActionResult> UpdateParentStatus([FromBody] UpdateParentStatusRequest request)
        {
            try
            {
                // 🔥 FIX: iOS sends "yyyy-MM-dd HH:mm:ss", Android sends "yyyy-MM-dd HH:mm" – accept both
                DateTime? parsedFollowUpDate = null;
                if (!string.IsNullOrEmpty(request.FollowUpDate))
                {
                    var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd" };
                    if (DateTime.TryParseExact(request.FollowUpDate.Trim(), formats, CultureInfo.InvariantCulture,
                                               DateTimeStyles.None, out DateTime tempDate))
                        parsedFollowUpDate = tempDate;
                }

                bool isUpdated = false;

                var potentialParent = await _context.Potential_Parents.FindAsync(request.ParentId);
                if (potentialParent != null)
                {
                    potentialParent.InterestLevel = request.InterestLevel;
                    potentialParent.FollowUpDate = parsedFollowUpDate;
                    potentialParent.HasBeenContacted = true;
                    isUpdated = true;
                }
                else
                {
                    var enrolledParent = await _context.ParentsEnrollments.FindAsync(request.ParentId);
                    if (enrolledParent != null)
                    {
                        enrolledParent.InterestLevel = request.InterestLevel;
                        enrolledParent.FollowUpDate = parsedFollowUpDate;
                        enrolledParent.HasBeenContacted = true;
                        isUpdated = true;
                    }
                }

                if (!isUpdated)
                    return NotFound(new { success = false, code = "NOT_FOUND", message = "Parent not found in any database table." });

                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Status updated successfully." });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, code = "SERVER_ERROR", message = ex.Message });
            }
        }

        // POST: api/counselordashboard/bulk-whatsapp   (counselor → leads)
        // 🔥 FIX: correct Whapi endpoint is https://gate.whapi.cloud/messages/{type} (the old URL had an extra "/api")
        [HttpPost("bulk-whatsapp")]
        public async Task<IActionResult> SendBulkWhatsApp([FromBody] BulkWhatsAppRequest request)
        {
            try
            {
                if (request.ParentIds == null || !request.ParentIds.Any())
                    return BadRequest(new { success = false, code = "NO_RECIPIENTS", message = "No parents selected." });

                if (string.IsNullOrWhiteSpace(request.Message) && string.IsNullOrWhiteSpace(request.MediaBase64))
                    return BadRequest(new { success = false, code = "EMPTY_MESSAGE", message = "Either a Message or Media must be provided." });

                var counselor = await _context.ResourceMasters.FindAsync(request.CounselorId);
                if (counselor == null)
                    return NotFound(new { success = false, code = "NOT_FOUND", message = "Counselor not found in the system." });

                var token = _whapi.ResolveToken(counselor);
                if (token == null)
                    return BadRequest(new { success = false, code = "WHATSAPP_NOT_CONNECTED", message = "This counselor has not linked their WhatsApp via Whapi yet." });

                var potentialPhones = await _context.Potential_Parents
                    .Where(p => request.ParentIds.Contains(p.Id) && !string.IsNullOrEmpty(p.Parent_Phone))
                    .Select(p => p.Parent_Phone)
                    .ToListAsync();

                var enrolledPhones = await _context.ParentsEnrollments
                    .Where(e => request.ParentIds.Contains(e.Id) && !string.IsNullOrEmpty(e.ParentPhone))
                    .Select(e => e.ParentPhone)
                    .ToListAsync();

                var numbers = potentialPhones.Concat(enrolledPhones)
                    .Select(PhoneUtil.ToWhatsAppNumber)
                    .Where(n => n != null)
                    .Select(n => n!)
                    .Distinct()
                    .ToList();

                int totalAttempted = numbers.Count;
                if (totalAttempted == 0)
                    return BadRequest(new { success = false, code = "NO_RECIPIENTS", message = "No valid phone numbers found for the selected parents." });

                string mediaType = string.IsNullOrWhiteSpace(request.MediaType) ? "text" : request.MediaType.ToLowerInvariant();
                string? media = null;
                if (mediaType != "text")
                {
                    var fallbackMime = mediaType == "document" ? "application/pdf" : mediaType == "video" ? "video/mp4" : "image/jpeg";
                    media = WhapiClient.ToDataUri(request.MediaBase64, fallbackMime);
                    if (media == null)
                        return BadRequest(new { success = false, code = "INVALID_MEDIA", message = "The attached file could not be read." });
                }

                int successCount = 0, failCount = 0;
                foreach (var phone in numbers)
                {
                    var result = mediaType == "text"
                        ? await _whapi.SendTextAsync(token, phone, request.Message ?? "")
                        : await _whapi.SendMediaAsync(token, mediaType, phone, media!, request.Message, request.FileName);

                    if (result.Success) successCount++;
                    else
                    {
                        failCount++;
                        if (result.IsAuthError)
                        {
                            failCount += numbers.Count - successCount - failCount;
                            break;
                        }
                    }
                    await Task.Delay(400);
                }

                return Ok(new
                {
                    success = true,
                    message = $"Dispatched {successCount} out of {totalAttempted} messages successfully.",
                    successCount,
                    failCount,
                    totalAttempted
                });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { success = false, code = "SERVER_ERROR", message = "Internal Server Error: " + ex.Message });
            }
        }

        [HttpPost("reports/send")]
        public async Task<IActionResult> SendParentReport([FromBody] SendReportRequest req)
        {
            string? mediaUrl = null;
            if (!string.IsNullOrEmpty(req.ImageBase64))
                mediaUrl = await _r2Service.UploadBase64ImageAsync(req.ImageBase64, $"report_{req.EnrollmentId}_{DateTime.UtcNow.Ticks}");

            var report = new ChildReport
            {
                ChildEnrollmentId = req.EnrollmentId,
                GroupVariationId = req.BatchId,
                ReportDate = IstClock.Today,
                TeacherNotes = req.Notes,
                MediaUrl = mediaUrl,
                SentViaWhatsApp = req.SendWhatsApp,
                SentViaEmail = req.SendEmail
            };
            _context.ChildReports.Add(report);
            await _context.SaveChangesAsync();

            return Ok(new { success = true });
        }

        // =====================================================================
        // COACH BATCHES & CHILD ATTENDANCE
        // =====================================================================

        // GET: api/counselordashboard/coach/{coachId}/batches?mineOnly=false
        // Returns all active batches; the coach's own batches first (isAssigned = true).
        // 🔥 NEW fields: dayCodes (["MON","WED"]), schoolId, sessionName, coachName, capacity,
        //               presentToday, markedToday, messagedToday.
        [HttpGet("coach/{coachId}/batches")]
        public async Task<IActionResult> GetCoachBatches(string coachId, [FromQuery] bool mineOnly = false)
        {
            try
            {
                var todayIst = IstClock.Today;

                var sessionMasters = await _context.SessionMasters
                    .Where(sm => sm.IsActive == 1)
                    .ToListAsync();

                if (mineOnly) sessionMasters = sessionMasters.Where(sm => sm.CoachId == coachId).ToList();

                var sessionIds = sessionMasters.Select(sm => sm.Id).ToList();
                if (!sessionIds.Any()) return Ok(new List<object>());

                var schoolIds = sessionMasters.Select(sm => sm.SchoolId).Where(s => s != null).Distinct().ToList();
                var schools = await _context.SchoolMaster
                    .Where(s => schoolIds.Contains(s.SchoolId))
                    .ToListAsync();

                var batches = await _context.GroupVariations
                    .Where(gv => sessionIds.Contains(gv.SessionId))
                    .OrderBy(gv => gv.Id)
                    .ToListAsync();

                var batchIds = batches.Select(b => b.Id).ToList();
                var batchAgeGroups = batches.Select(b => b.AgeGroup).Distinct().ToList();

                var relevantKits = await _context.Set<KitMaster>()
                    .Include(k => k.KitItems)
                        .ThenInclude(ki => ki.Item)
                    .Where(k => k.AgeGroup != null && batchAgeGroups.Contains(k.AgeGroup))
                    .ToListAsync();

                var children = await _context.ParentsEnrollments
                    .Where(pe => pe.GroupVariationId != null && batchIds.Contains(pe.GroupVariationId.Value) && pe.PaymentStatus == "Paid")
                    .ToListAsync();

                var childIds = children.Select(c => c.Id).ToList();

                var todayAttendances = await _context.ChildAttendances
                    .Where(a => childIds.Contains(a.ChildEnrollmentId) && a.AttendanceDate == todayIst)
                    .ToListAsync();

                // Which batches already got a WhatsApp update today (table may not exist before the SQL script runs)
                var messagedToday = new HashSet<int>();
                try
                {
                    var ids = await _context.BatchBroadcasts
                        .Where(b => batchIds.Contains(b.BatchId) && b.CreatedAt >= todayIst && b.Status != "Failed")
                        .Select(b => b.BatchId)
                        .Distinct()
                        .ToListAsync();
                    messagedToday = new HashSet<int>(ids);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "BatchBroadcasts lookup failed (run the SQL script)");
                }

                var result = batches.Select(gv =>
                {
                    var session = sessionMasters.FirstOrDefault(sm => sm.Id == gv.SessionId);
                    var school = schools.FirstOrDefault(s => s.SchoolId == session?.SchoolId);

                    var sessionBatches = batches.Where(b => b.SessionId == gv.SessionId).OrderBy(b => b.Id).ToList();
                    int batchIndex = sessionBatches.IndexOf(gv);

                    var orderedPdfs = relevantKits
                        .Where(k => k.AgeGroup == gv.AgeGroup)
                        .SelectMany(k => k.KitItems)
                        .Where(ki => ki.Item != null && !string.IsNullOrWhiteSpace(ki.Item.LessonPlanPdf))
                        .Select(ki => ki.Item!)
                        .OrderBy(item => item.Sequence)
                        .Select(item => item.LessonPlanPdf)
                        .Distinct()
                        .ToList();

                    string? mappedPdf = (batchIndex >= 0 && batchIndex < orderedPdfs.Count) ? orderedPdfs[batchIndex] : null;

                    var batchChildren = children.Where(pe => pe.GroupVariationId == gv.Id).ToList();
                    var batchAttendance = todayAttendances.Where(a => a.GroupVariationId == gv.Id).ToList();

                    var address = string.Join(", ", new[] { school?.SchoolAddress, school?.SchoolCity }
                        .Where(x => !string.IsNullOrWhiteSpace(x)));

                    return new
                    {
                        BatchId = gv.Id,
                        SessionId = gv.SessionId,
                        SessionName = session?.SessionName,
                        SchoolId = session?.SchoolId,
                        AgeGroup = gv.AgeGroup,
                        Timing = gv.TimeSlot,
                        Days = gv.Days,
                        DayCodes = DayParser.Parse(gv.Days),
                        TotalEnrolled = batchChildren.Count,
                        Capacity = gv.Capacity,
                        CentreName = school?.SchoolName ?? "Unknown Centre",
                        CentreAddress = address,
                        Status = "UPCOMING",
                        IsAssigned = session?.CoachId == coachId,
                        CoachName = session?.CoachName,
                        LessonPlanPdf = mappedPdf,
                        PresentToday = batchAttendance.Count(a => a.IsPresent),
                        MarkedToday = batchAttendance.Count,
                        MessagedToday = messagedToday.Contains(gv.Id),
                        Children = batchChildren.Select(child =>
                        {
                            var attendance = batchAttendance.FirstOrDefault(a => a.ChildEnrollmentId == child.Id);
                            return new
                            {
                                EnrollmentId = child.Id,
                                BatchId = gv.Id,
                                ChildName = child.ChildName ?? "Unknown",
                                ParentName = child.ParentName,
                                ParentPhone = child.ParentPhone ?? "",
                                ParentEmail = child.ParentEmail,
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
                })
                .OrderByDescending(b => b.IsAssigned)
                .ThenBy(b => b.CentreName)
                .ThenBy(b => b.Timing)
                .ToList();

                return Ok(result);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "GetCoachBatches failed for CoachId={CoachId}", coachId);
                return ApiError(500, "SERVER_ERROR", "Couldn't load batches. Please try again.");
            }
        }

        // POST: api/counselordashboard/mark-child
        // 🔥 FIX: the iOS app calls "attendance/mark-child" – both routes now work.
        [HttpPost("mark-child")]
        [HttpPost("attendance/mark-child")]
        public async Task<IActionResult> MarkChildAttendance([FromBody] MarkChildAttendanceDto request)
        {
            try
            {
                if (request == null || string.IsNullOrWhiteSpace(request.Action))
                    return ApiError(400, "INVALID_PAYLOAD", "Invalid request.");

                var action = request.Action.Trim().ToUpperInvariant();
                if (action != "IN" && action != "OUT" && action != "ABSENT")
                    return ApiError(400, "INVALID_ACTION", "Unknown action. Use IN, OUT or ABSENT.");

                var nowIst = IstClock.Now;
                var todayIst = nowIst.Date;

                var enrollment = await _context.ParentsEnrollments.FirstOrDefaultAsync(pe => pe.Id == request.EnrollmentId);
                if (enrollment == null)
                    return ApiError(404, "CHILD_NOT_FOUND", "This child's enrollment was not found.");

                var batchExists = await _context.GroupVariations.AnyAsync(gv => gv.Id == request.BatchId);
                if (!batchExists)
                    return ApiError(404, "BATCH_NOT_FOUND", "This batch no longer exists.");

                var record = await _context.ChildAttendances
                    .FirstOrDefaultAsync(ca => ca.ChildEnrollmentId == request.EnrollmentId
                                            && ca.GroupVariationId == request.BatchId
                                            && ca.AttendanceDate == todayIst);

                switch (action)
                {
                    case "ABSENT":
                        if (record == null)
                        {
                            record = new ChildAttendance
                            {
                                ChildEnrollmentId = request.EnrollmentId,
                                GroupVariationId = request.BatchId,
                                AttendanceDate = todayIst
                            };
                            _context.ChildAttendances.Add(record);
                        }
                        record.IsPresent = false;
                        record.CheckInTime = null;
                        record.CheckOutTime = null;
                        break;

                    case "IN":
                        if (record == null)
                        {
                            record = new ChildAttendance
                            {
                                ChildEnrollmentId = request.EnrollmentId,
                                GroupVariationId = request.BatchId,
                                AttendanceDate = todayIst
                            };
                            _context.ChildAttendances.Add(record);
                        }
                        record.IsPresent = true;
                        record.CheckInTime ??= nowIst.TimeOfDay; // keep the original arrival time if re-marked
                        record.CheckOutTime = null;             // IN after OUT = child came back
                        break;

                    case "OUT":
                        if (record == null || !record.IsPresent)
                            return ApiError(400, "NOT_CHECKED_IN", "Mark the child IN before marking OUT.");
                        record.CheckOutTime = nowIst.TimeOfDay;
                        break;
                }

                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = $"Marked {action}",
                    attendance = new
                    {
                        id = record!.Id,
                        isPresent = record.IsPresent,
                        checkInTime = record.CheckInTime?.ToString(@"hh\:mm\:ss"),
                        checkOutTime = record.CheckOutTime?.ToString(@"hh\:mm\:ss")
                    }
                });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "MarkChildAttendance failed for Enrollment={EnrollmentId}", request?.EnrollmentId);
                var actualError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
                return ApiError(500, "SERVER_ERROR", $"Attendance could not be saved: {actualError}");
            }
        }

        public class MarkChildAttendanceDto
        {
            public int EnrollmentId { get; set; }
            public int BatchId { get; set; }
            public string Action { get; set; } = string.Empty; // "IN", "OUT", "ABSENT"
        }

        [HttpGet("batch/{batchId}/attendance-history")]
        public async Task<IActionResult> GetBatchAttendanceHistory(int batchId)
        {
            var batchHistory = await _context.GroupVariations
                .Include(gv => gv.ChildAttendances)
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

            if (batchHistory == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Batch not found" });
            return Ok(batchHistory);
        }

        [HttpPost("attendance/finalize-batch")]
        public IActionResult FinalizeBatch([FromBody] FinalizeBatchRequest req)
        {
            // Session remarks are now stored with the WhatsApp update (BatchBroadcasts).
            return Ok(new { success = true, message = "Session finalized." });
        }
    }
}




// using Microsoft.AspNetCore.Mvc;
// using Microsoft.EntityFrameworkCore;
// using Microsoft.Extensions.Logging;
// using PwcApi.Data;
// using PwcApi.DTOs;
// using PwcApi.Models;
// using PwcApi.Services;
// using System;
// using System.Collections.Generic;
// using System.Globalization;
// using System.Linq;
// using System.Threading.Tasks;

// namespace PwcApi.Controllers
// {
//     [ApiController]
//     [Route("api/[controller]")]
//     public class CounselorDashboardController : ControllerBase
//     {
//         private readonly AppDbContext _context;
//         private readonly R2StorageService _r2Service;
//         private readonly WhapiClient _whapi;
//         private readonly ILogger<CounselorDashboardController> _logger;

//         public CounselorDashboardController(AppDbContext context, R2StorageService r2Service,
//                                             WhapiClient whapi, ILogger<CounselorDashboardController> logger)
//         {
//             _context = context;
//             _r2Service = r2Service;
//             _whapi = whapi;
//             _logger = logger;
//         }

//         private ObjectResult ApiError(int statusCode, string code, string message) =>
//             StatusCode(statusCode, new { success = false, code, message });

//         // =====================================================================
//         // AUTH & PROFILE
//         // =====================================================================

//         // POST: api/counselordashboard/login
//         // 🔒 SECURITY FIX: the old version returned the real password from the DB in its error message.
//         [HttpPost("login")]
//         public async Task<IActionResult> Login([FromBody] LoginRequest request)
//         {
//             const string invalid = "Invalid email or password.";
//             var email = request?.Email?.Trim() ?? "";
//             if (email.Length == 0 || string.IsNullOrEmpty(request?.Password))
//                 return ApiError(400, "INVALID_CREDENTIALS", invalid);

//             var counselor = await _context.ResourceMasters.FirstOrDefaultAsync(r => r.CompanyEmail == email);

//             if (counselor == null || counselor.Password != request!.Password)
//                 return ApiError(401, "INVALID_CREDENTIALS", invalid);

//             if (counselor.Role != "Counselor" && counselor.Role != "Coach")
//                 return ApiError(403, "ROLE_NOT_ALLOWED", "This account doesn't have access to the app.");

//             if (counselor.IsActive != 1)
//                 return ApiError(403, "ACCOUNT_INACTIVE", "Your account is inactive. Please contact the admin.");

//             return Ok(new LoginResponse
//             {
//                 CounselorId = counselor.Id,
//                 Name = counselor.Name ?? "Unknown",
//                 EmpId = counselor.EmpId ?? "Unknown",
//                 Role = counselor.Role ?? ""
//             });
//         }

//         // GET: api/counselordashboard/profile/{counselorId}
//         [HttpGet("profile/{counselorId}")]
//         public async Task<IActionResult> GetProfile(int counselorId)
//         {
//             var counselor = await _context.ResourceMasters
//                 .FirstOrDefaultAsync(r => r.Id == counselorId && (r.Role == "Counselor" || r.Role == "Coach"));

//             if (counselor == null)
//                 return NotFound(new { success = false, code = "NOT_FOUND", message = "Counselor not found or inactive." });

//             return Ok(new
//             {
//                 id = counselor.Id,
//                 name = counselor.Name ?? "Unknown",
//                 role = counselor.Role ?? "Counselor",
//                 isActive = counselor.IsActive == 1,
//                 companyEmail = counselor.CompanyEmail ?? string.Empty,
//                 personalEmail = counselor.Email ?? string.Empty,
//                 empId = counselor.EmpId ?? string.Empty,
//                 phone = counselor.Phone ?? string.Empty,
//                 dateOfJoining = counselor.DateOfJoining?.ToString("yyyy-MM-dd") ?? string.Empty
//             });
//         }

//         // =====================================================================
//         // LEADS
//         // =====================================================================

//         [HttpPost("add-lead")]
//         public async Task<IActionResult> AddPotentialLead([FromBody] AddLeadRequest request)
//         {
//             try
//             {
//                 DateTime? dob = null;
//                 if (!string.IsNullOrWhiteSpace(request.ChildDOB) &&
//                     DateTime.TryParse(request.ChildDOB, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDob))
//                     dob = parsedDob;

//                 var newLead = new PotentialParent
//                 {
//                     School_Name = request.SchoolId,
//                     Parent_Name = request.ParentName,
//                     Parent_Email = request.ParentEmail,
//                     Parent_Phone = request.ParentPhone,
//                     Child_Name = request.ChildName,
//                     Child_DOB = dob,
//                     InterestLevel = "Moderate",
//                     HasBeenContacted = false,
//                     CreatedAt = IstClock.Now,
//                     MediaConsent = 0
//                 };

//                 _context.Potential_Parents.Add(newLead);
//                 await _context.SaveChangesAsync();

//                 return Ok(new { success = true, message = "Lead added successfully." });
//             }
//             catch (DbUpdateException ex)
//             {
//                 var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
//                 return StatusCode(500, new { success = false, code = "DB_ERROR", message = "Database Error: " + innerEx });
//             }
//             catch (Exception ex)
//             {
//                 return StatusCode(500, new { success = false, code = "SERVER_ERROR", message = "General Error: " + ex.Message });
//             }
//         }

//         public class AddLeadRequest
//         {
//             public string? SchoolId { get; set; }
//             public int CounselorId { get; set; }
//             public string? ParentName { get; set; }
//             public string? ParentEmail { get; set; }
//             public string? ParentPhone { get; set; }
//             public string? ChildName { get; set; }
//             public string? ChildDOB { get; set; }
//         }

//         // =====================================================================
//         // CENTRES
//         // =====================================================================

//         // GET: api/counselordashboard/centres/{userId}
//         // Returns ALL centres, assigned ones first (isAssigned = true). Now also returns coordinates for geofencing.
//         [HttpGet("centres/{userId}")]
//         public async Task<IActionResult> GetCentres(int userId)
//         {
//             try
//             {
//                 var counselorSchoolIds = await _context.SchoolMaster
//                     .Where(s => s.CounselorId == userId)
//                     .Select(s => s.SchoolId)
//                     .ToListAsync();

//                 // 🔥 FIX: removed "|| sm.Id == userId" (it compared a SESSION id with a USER id)
//                 var coachIdStr = userId.ToString();
//                 var coachSchoolIds = await _context.SessionMasters
//                     .Where(sm => sm.CoachId == coachIdStr && sm.IsActive == 1)
//                     .Select(sm => sm.SchoolId)
//                     .Distinct()
//                     .ToListAsync();

//                 var assigned = new HashSet<string>(
//                     counselorSchoolIds.Concat(coachSchoolIds.Where(id => id != null).Select(id => id!)));

//                 var allSchools = await _context.SchoolMaster
//                     .Select(s => new
//                     {
//                         s.SchoolId,
//                         SchoolName = s.SchoolName ?? "Unknown Centre",
//                         s.SchoolCity,
//                         s.SchoolAddress,
//                         ContactPersonName = s.ContactPersonName ?? "N/A",
//                         ContactPersonPhone = s.ContactPersonPhone ?? "N/A",
//                         s.Coordinates,      // 🔥 NEW: "28.6254,77.3808" – used by the app's geofence
//                         s.SchoolLocation
//                     })
//                     .ToListAsync();

//                 var ordered = allSchools
//                     .Select(s => new
//                     {
//                         s.SchoolId,
//                         s.SchoolName,
//                         s.SchoolCity,
//                         s.SchoolAddress,
//                         s.ContactPersonName,
//                         s.ContactPersonPhone,
//                         s.Coordinates,
//                         s.SchoolLocation,
//                         IsAssigned = assigned.Contains(s.SchoolId)
//                     })
//                     .OrderByDescending(s => s.IsAssigned)
//                     .ThenBy(s => s.SchoolName)
//                     .ToList();

//                 return Ok(ordered);
//             }
//             catch (Exception ex)
//             {
//                 var innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
//                 return StatusCode(500, new { success = false, code = "SERVER_ERROR", message = $"Error fetching centres: {innerMsg}" });
//             }
//         }

//         // GET: api/counselordashboard/centre/{schoolId}
//         [HttpGet("centre/{schoolId}")]
//         public async Task<IActionResult> GetCentreDetails(string schoolId)
//         {
//             var s = await _context.SchoolMaster.FirstOrDefaultAsync(x => x.SchoolId == schoolId.Trim());

//             if (s == null)
//                 return NotFound(new { success = false, code = "NOT_FOUND", message = "Centre not found." });

//             return Ok(new
//             {
//                 success = true,
//                 data = new
//                 {
//                     schoolId = s.SchoolId,
//                     schoolName = s.SchoolName,
//                     schoolEmail = s.SchoolEmail,
//                     schoolPhone = s.SchoolPhone,
//                     onBoardingId = s.OnBoardingId,
//                     createdDate = s.CreatedDate,
//                     billingAddress = s.BillingAddress,
//                     contactPersonName = s.ContactPersonName,
//                     contactPersonEmail = s.ContactPersonEmail,
//                     contactPersonPhone = s.ContactPersonPhone,
//                     ownerName = s.OwnerName,
//                     ownerEmail = s.OwnerEmail,
//                     ownerPhone = s.OwnerPhone,
//                     principalName = s.PrincipalName,
//                     principalEmail = s.PrincipalEmail,
//                     principalPhone = s.PrincipalPhone,
//                     schoolAddress = s.SchoolAddress,
//                     schoolCity = s.SchoolCity,
//                     schoolCountry = s.SchoolCountry,
//                     schoolLocation = s.SchoolLocation,
//                     schoolPincode = s.SchoolPincode,
//                     schoolState = s.SchoolState,
//                     shippingAddress = s.ShippingAddress,
//                     plan = s.Plan,
//                     isEnrolled = s.IsEnrolled,
//                     counselorId = s.CounselorId,
//                     facilitationCharges = s.FacilitationCharges,
//                     salesPersonId = s.SalesPersonId,
//                     coordinates = s.Coordinates,
//                     sessionContacts = new[]
//                     {
//                         new { name = s.SessionContact1Name, email = s.SessionContact1Email, phone = s.SessionContact1Phone, position = s.SessionContact1Position },
//                         new { name = s.SessionContact2Name, email = s.SessionContact2Email, phone = s.SessionContact2Phone, position = s.SessionContact2Position },
//                         new { name = s.SessionContact3Name, email = s.SessionContact3Email, phone = s.SessionContact3Phone, position = s.SessionContact3Position },
//                         new { name = s.SessionContact4Name, email = s.SessionContact4Email, phone = s.SessionContact4Phone, position = s.SessionContact4Position }
//                     },
//                     activityCount = s.ActivityCount,
//                     promoCode = s.PromoCode,
//                     promoDiscount = s.PromoDiscount
//                 }
//             });
//         }

//         // GET: api/counselordashboard/sessions/{schoolId}
//         [HttpGet("sessions/{schoolId}")]
//         public async Task<IActionResult> GetSessionsForSchool(string schoolId)
//         {
//             try
//             {
//                 var cleanSchoolId = schoolId.Trim();
//                 var sessions = await _context.SessionMasters
//                     .Where(s => s.SchoolId == cleanSchoolId)
//                     .Select(s => new { sessionId = s.Id, sessionName = s.SessionName ?? s.Id.ToString() })
//                     .ToListAsync();

//                 if (!sessions.Any())
//                     return NotFound(new { success = false, code = "NOT_FOUND", message = "No active sessions found for this school." });

//                 return Ok(sessions);
//             }
//             catch (Exception ex)
//             {
//                 return StatusCode(500, new { success = false, code = "DB_ERROR", message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
//             }
//         }

//         // =====================================================================
//         // PARENTS / CRM
//         // =====================================================================

//         // GET: api/counselordashboard/parents/{schoolId}
//         [HttpGet("parents/{schoolId}")]
//         public async Task<IActionResult> GetParents(string schoolId)
//         {
//             try
//             {
//                 var cleanSchoolId = schoolId.Trim();

//                 var potentialParents = await _context.Potential_Parents
//                     .Where(p => p.School_Name != null && p.School_Name.Trim() == cleanSchoolId)
//                     .Select(p => new ParentResponse
//                     {
//                         Id = p.Id,
//                         Status = "Potential",
//                         ParentName = p.Parent_Name,
//                         ParentEmail = p.Parent_Email,
//                         ParentPhone = p.Parent_Phone,
//                         ChildName = p.Child_Name,
//                         ChildDOB = p.Child_DOB,
//                         ChildSchoolName = p.ChildSchoolName,
//                         CreatedAt = p.CreatedAt,
//                         MediaConsent = p.MediaConsent,
//                         Remark = p.Remark,
//                         PaymentStatus = "N/A",
//                         InterestLevel = p.InterestLevel,
//                         FollowUpDate = p.FollowUpDate.HasValue ? p.FollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : null,
//                         HasBeenContacted = p.HasBeenContacted
//                     })
//                     .ToListAsync();

//                 var enrolledParents = await _context.ParentsEnrollments
//                     .Where(p => p.SchoolId != null && p.SchoolId.Trim() == cleanSchoolId)
//                     .Select(p => new ParentResponse
//                     {
//                         Id = p.Id,
//                         Status = "Enrolled",
//                         ParentName = p.ParentName,
//                         ParentEmail = p.ParentEmail,
//                         ParentPhone = p.ParentPhone,
//                         ChildName = p.ChildName,
//                         ChildDOB = p.ChildDOB,
//                         ChildSchoolName = p.ChildSchoolName,
//                         ChildSchoolCity = p.ChildSchoolCity,
//                         CreatedAt = p.CreatedAt,
//                         MediaConsent = p.MediaConsent,
//                         PaymentStatus = p.PaymentStatus ?? "Pending",
//                         PaymentDate = p.PaymentDate,
//                         PaymentAmount = p.PaymentAmount,
//                         BillingAddress = p.BillingAddress,
//                         BillingCity = p.BillingCity,
//                         BillingState = p.BillingState,
//                         BillingPincode = p.BillingPincode,
//                         SessionId = p.SessionId,
//                         SessionName = p.SessionName,
//                         SessionAgeGroup = p.SessionAgeGroup,
//                         SessionDays = p.SessionDays,
//                         SessionFrequency = p.SessionFrequency,
//                         SessionTimeSlot = p.SessionTimeSlot,
//                         DiscountAmount = p.DiscountAmount,
//                         DiscountCode = p.DiscountCode,
//                         InterestLevel = p.InterestLevel,
//                         FollowUpDate = p.FollowUpDate.HasValue ? p.FollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : null,
//                         HasBeenContacted = p.HasBeenContacted
//                     })
//                     .ToListAsync();

//                 var allParents = potentialParents.Concat(enrolledParents).OrderBy(p => p.Status).ToList();
//                 return Ok(new { success = true, data = allParents });
//             }
//             catch (Exception ex)
//             {
//                 return StatusCode(500, new { success = false, code = "DB_ERROR", message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
//             }
//         }

//         // PUT: api/counselordashboard/update-status
//         [HttpPut("update-status")]
//         public async Task<IActionResult> UpdateParentStatus([FromBody] UpdateParentStatusRequest request)
//         {
//             try
//             {
//                 // 🔥 FIX: iOS sends "yyyy-MM-dd HH:mm:ss", Android sends "yyyy-MM-dd HH:mm" – accept both
//                 DateTime? parsedFollowUpDate = null;
//                 if (!string.IsNullOrEmpty(request.FollowUpDate))
//                 {
//                     var formats = new[] { "yyyy-MM-dd HH:mm:ss", "yyyy-MM-dd HH:mm", "yyyy-MM-dd'T'HH:mm:ss", "yyyy-MM-dd" };
//                     if (DateTime.TryParseExact(request.FollowUpDate.Trim(), formats, CultureInfo.InvariantCulture,
//                                                DateTimeStyles.None, out DateTime tempDate))
//                         parsedFollowUpDate = tempDate;
//                 }

//                 bool isUpdated = false;

//                 var potentialParent = await _context.Potential_Parents.FindAsync(request.ParentId);
//                 if (potentialParent != null)
//                 {
//                     potentialParent.InterestLevel = request.InterestLevel;
//                     potentialParent.FollowUpDate = parsedFollowUpDate;
//                     potentialParent.HasBeenContacted = true;
//                     isUpdated = true;
//                 }
//                 else
//                 {
//                     var enrolledParent = await _context.ParentsEnrollments.FindAsync(request.ParentId);
//                     if (enrolledParent != null)
//                     {
//                         enrolledParent.InterestLevel = request.InterestLevel;
//                         enrolledParent.FollowUpDate = parsedFollowUpDate;
//                         enrolledParent.HasBeenContacted = true;
//                         isUpdated = true;
//                     }
//                 }

//                 if (!isUpdated)
//                     return NotFound(new { success = false, code = "NOT_FOUND", message = "Parent not found in any database table." });

//                 await _context.SaveChangesAsync();
//                 return Ok(new { success = true, message = "Status updated successfully." });
//             }
//             catch (Exception ex)
//             {
//                 return StatusCode(500, new { success = false, code = "SERVER_ERROR", message = ex.Message });
//             }
//         }

//         // POST: api/counselordashboard/bulk-whatsapp   (counselor → leads)
//         // 🔥 FIX: correct Whapi endpoint is https://gate.whapi.cloud/messages/{type} (the old URL had an extra "/api")
//         [HttpPost("bulk-whatsapp")]
//         public async Task<IActionResult> SendBulkWhatsApp([FromBody] BulkWhatsAppRequest request)
//         {
//             try
//             {
//                 if (request.ParentIds == null || !request.ParentIds.Any())
//                     return BadRequest(new { success = false, code = "NO_RECIPIENTS", message = "No parents selected." });

//                 if (string.IsNullOrWhiteSpace(request.Message) && string.IsNullOrWhiteSpace(request.MediaBase64))
//                     return BadRequest(new { success = false, code = "EMPTY_MESSAGE", message = "Either a Message or Media must be provided." });

//                 var counselor = await _context.ResourceMasters.FindAsync(request.CounselorId);
//                 if (counselor == null)
//                     return NotFound(new { success = false, code = "NOT_FOUND", message = "Counselor not found in the system." });

//                 var token = _whapi.ResolveToken(counselor);
//                 if (token == null)
//                     return BadRequest(new { success = false, code = "WHATSAPP_NOT_CONNECTED", message = "This counselor has not linked their WhatsApp via Whapi yet." });

//                 var potentialPhones = await _context.Potential_Parents
//                     .Where(p => request.ParentIds.Contains(p.Id) && !string.IsNullOrEmpty(p.Parent_Phone))
//                     .Select(p => p.Parent_Phone)
//                     .ToListAsync();

//                 var enrolledPhones = await _context.ParentsEnrollments
//                     .Where(e => request.ParentIds.Contains(e.Id) && !string.IsNullOrEmpty(e.ParentPhone))
//                     .Select(e => e.ParentPhone)
//                     .ToListAsync();

//                 var numbers = potentialPhones.Concat(enrolledPhones)
//                     .Select(PhoneUtil.ToWhatsAppNumber)
//                     .Where(n => n != null)
//                     .Select(n => n!)
//                     .Distinct()
//                     .ToList();

//                 int totalAttempted = numbers.Count;
//                 if (totalAttempted == 0)
//                     return BadRequest(new { success = false, code = "NO_RECIPIENTS", message = "No valid phone numbers found for the selected parents." });

//                 string mediaType = string.IsNullOrWhiteSpace(request.MediaType) ? "text" : request.MediaType.ToLowerInvariant();
//                 string? media = null;
//                 if (mediaType != "text")
//                 {
//                     var fallbackMime = mediaType == "document" ? "application/pdf" : mediaType == "video" ? "video/mp4" : "image/jpeg";
//                     media = WhapiClient.ToDataUri(request.MediaBase64, fallbackMime);
//                     if (media == null)
//                         return BadRequest(new { success = false, code = "INVALID_MEDIA", message = "The attached file could not be read." });
//                 }

//                 int successCount = 0, failCount = 0;
//                 foreach (var phone in numbers)
//                 {
//                     var result = mediaType == "text"
//                         ? await _whapi.SendTextAsync(token, phone, request.Message ?? "")
//                         : await _whapi.SendMediaAsync(token, mediaType, phone, media!, request.Message, request.FileName);

//                     if (result.Success) successCount++;
//                     else
//                     {
//                         failCount++;
//                         if (result.IsAuthError)
//                         {
//                             failCount += numbers.Count - successCount - failCount;
//                             break;
//                         }
//                     }
//                     await Task.Delay(400);
//                 }

//                 return Ok(new
//                 {
//                     success = true,
//                     message = $"Dispatched {successCount} out of {totalAttempted} messages successfully.",
//                     successCount,
//                     failCount,
//                     totalAttempted
//                 });
//             }
//             catch (Exception ex)
//             {
//                 return StatusCode(500, new { success = false, code = "SERVER_ERROR", message = "Internal Server Error: " + ex.Message });
//             }
//         }

//         [HttpPost("reports/send")]
//         public async Task<IActionResult> SendParentReport([FromBody] SendReportRequest req)
//         {
//             string? mediaUrl = null;
//             if (!string.IsNullOrEmpty(req.ImageBase64))
//                 mediaUrl = await _r2Service.UploadBase64ImageAsync(req.ImageBase64, $"report_{req.EnrollmentId}_{DateTime.UtcNow.Ticks}");

//             var report = new ChildReport
//             {
//                 ChildEnrollmentId = req.EnrollmentId,
//                 GroupVariationId = req.BatchId,
//                 ReportDate = IstClock.Today,
//                 TeacherNotes = req.Notes,
//                 MediaUrl = mediaUrl,
//                 SentViaWhatsApp = req.SendWhatsApp,
//                 SentViaEmail = req.SendEmail
//             };
//             _context.ChildReports.Add(report);
//             await _context.SaveChangesAsync();

//             return Ok(new { success = true });
//         }

//         // =====================================================================
//         // COACH BATCHES & CHILD ATTENDANCE
//         // =====================================================================

//         // GET: api/counselordashboard/coach/{coachId}/batches?mineOnly=false
//         // Returns all active batches; the coach's own batches first (isAssigned = true).
//         // 🔥 NEW fields: dayCodes (["MON","WED"]), schoolId, sessionName, coachName, capacity,
//         //               presentToday, markedToday, messagedToday.
//         [HttpGet("coach/{coachId}/batches")]
//         public async Task<IActionResult> GetCoachBatches(string coachId, [FromQuery] bool mineOnly = false)
//         {
//             try
//             {
//                 var todayIst = IstClock.Today;

//                 var sessionMasters = await _context.SessionMasters
//                     .Where(sm => sm.IsActive == 1)
//                     .ToListAsync();

//                 if (mineOnly) sessionMasters = sessionMasters.Where(sm => sm.CoachId == coachId).ToList();

//                 var sessionIds = sessionMasters.Select(sm => sm.Id).ToList();
//                 if (!sessionIds.Any()) return Ok(new List<object>());

//                 var schoolIds = sessionMasters.Select(sm => sm.SchoolId).Where(s => s != null).Distinct().ToList();
//                 var schools = await _context.SchoolMaster
//                     .Where(s => schoolIds.Contains(s.SchoolId))
//                     .ToListAsync();

//                 var batches = await _context.GroupVariations
//                     .Where(gv => sessionIds.Contains(gv.SessionId))
//                     .OrderBy(gv => gv.Id)
//                     .ToListAsync();

//                 var batchIds = batches.Select(b => b.Id).ToList();
//                 var batchAgeGroups = batches.Select(b => b.AgeGroup).Distinct().ToList();

//                 var relevantKits = await _context.Set<KitMaster>()
//                     .Include(k => k.KitItems)
//                         .ThenInclude(ki => ki.Item)
//                     .Where(k => k.AgeGroup != null && batchAgeGroups.Contains(k.AgeGroup))
//                     .ToListAsync();

//                 var children = await _context.ParentsEnrollments
//                     .Where(pe => pe.GroupVariationId != null && batchIds.Contains(pe.GroupVariationId.Value) && pe.PaymentStatus == "Paid")
//                     .ToListAsync();

//                 var childIds = children.Select(c => c.Id).ToList();

//                 var todayAttendances = await _context.ChildAttendances
//                     .Where(a => childIds.Contains(a.ChildEnrollmentId) && a.AttendanceDate == todayIst)
//                     .ToListAsync();

//                 // Which batches already got a WhatsApp update today (table may not exist before the SQL script runs)
//                 var messagedToday = new HashSet<int>();
//                 try
//                 {
//                     var ids = await _context.BatchBroadcasts
//                         .Where(b => batchIds.Contains(b.BatchId) && b.CreatedAt >= todayIst && b.Status != "Failed")
//                         .Select(b => b.BatchId)
//                         .Distinct()
//                         .ToListAsync();
//                     messagedToday = new HashSet<int>(ids);
//                 }
//                 catch (Exception ex)
//                 {
//                     _logger.LogWarning(ex, "BatchBroadcasts lookup failed (run the SQL script)");
//                 }

//                 var result = batches.Select(gv =>
//                 {
//                     var session = sessionMasters.FirstOrDefault(sm => sm.Id == gv.SessionId);
//                     var school = schools.FirstOrDefault(s => s.SchoolId == session?.SchoolId);

//                     var sessionBatches = batches.Where(b => b.SessionId == gv.SessionId).OrderBy(b => b.Id).ToList();
//                     int batchIndex = sessionBatches.IndexOf(gv);

//                     var orderedPdfs = relevantKits
//                         .Where(k => k.AgeGroup == gv.AgeGroup)
//                         .SelectMany(k => k.KitItems)
//                         .Where(ki => ki.Item != null && !string.IsNullOrWhiteSpace(ki.Item.LessonPlanPdf))
//                         .Select(ki => ki.Item!)
//                         .OrderBy(item => item.Sequence)
//                         .Select(item => item.LessonPlanPdf)
//                         .Distinct()
//                         .ToList();

//                     string? mappedPdf = (batchIndex >= 0 && batchIndex < orderedPdfs.Count) ? orderedPdfs[batchIndex] : null;

//                     var batchChildren = children.Where(pe => pe.GroupVariationId == gv.Id).ToList();
//                     var batchAttendance = todayAttendances.Where(a => a.GroupVariationId == gv.Id).ToList();

//                     var address = string.Join(", ", new[] { school?.SchoolAddress, school?.SchoolCity }
//                         .Where(x => !string.IsNullOrWhiteSpace(x)));

//                     return new
//                     {
//                         BatchId = gv.Id,
//                         SessionId = gv.SessionId,
//                         SessionName = session?.SessionName,
//                         SchoolId = session?.SchoolId,
//                         AgeGroup = gv.AgeGroup,
//                         Timing = gv.TimeSlot,
//                         Days = gv.Days,
//                         DayCodes = DayParser.Parse(gv.Days),
//                         TotalEnrolled = batchChildren.Count,
//                         Capacity = gv.Capacity,
//                         CentreName = school?.SchoolName ?? "Unknown Centre",
//                         CentreAddress = address,
//                         Status = "UPCOMING",
//                         IsAssigned = session?.CoachId == coachId,
//                         CoachName = session?.CoachName,
//                         LessonPlanPdf = mappedPdf,
//                         PresentToday = batchAttendance.Count(a => a.IsPresent),
//                         MarkedToday = batchAttendance.Count,
//                         MessagedToday = messagedToday.Contains(gv.Id),
//                         Children = batchChildren.Select(child =>
//                         {
//                             var attendance = batchAttendance.FirstOrDefault(a => a.ChildEnrollmentId == child.Id);
//                             return new
//                             {
//                                 EnrollmentId = child.Id,
//                                 BatchId = gv.Id,
//                                 ChildName = child.ChildName ?? "Unknown",
//                                 ParentName = child.ParentName,
//                                 ParentPhone = child.ParentPhone ?? "",
//                                 ParentEmail = child.ParentEmail,
//                                 TodayAttendance = attendance == null ? null : new
//                                 {
//                                     Id = attendance.Id,
//                                     IsPresent = attendance.IsPresent,
//                                     CheckInTime = attendance.CheckInTime?.ToString(@"hh\:mm\:ss"),
//                                     CheckOutTime = attendance.CheckOutTime?.ToString(@"hh\:mm\:ss")
//                                 }
//                             };
//                         }).ToList()
//                     };
//                 })
//                 .OrderByDescending(b => b.IsAssigned)
//                 .ThenBy(b => b.CentreName)
//                 .ThenBy(b => b.Timing)
//                 .ToList();

//                 return Ok(result);
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogError(ex, "GetCoachBatches failed for CoachId={CoachId}", coachId);
//                 return ApiError(500, "SERVER_ERROR", "Couldn't load batches. Please try again.");
//             }
//         }

//         // POST: api/counselordashboard/mark-child
//         // 🔥 FIX: the iOS app calls "attendance/mark-child" – both routes now work.
//         [HttpPost("mark-child")]
//         [HttpPost("attendance/mark-child")]
//         public async Task<IActionResult> MarkChildAttendance([FromBody] MarkChildAttendanceDto request)
//         {
//             try
//             {
//                 if (request == null || string.IsNullOrWhiteSpace(request.Action))
//                     return ApiError(400, "INVALID_PAYLOAD", "Invalid request.");

//                 var action = request.Action.Trim().ToUpperInvariant();
//                 if (action != "IN" && action != "OUT" && action != "ABSENT")
//                     return ApiError(400, "INVALID_ACTION", "Unknown action. Use IN, OUT or ABSENT.");

//                 var nowIst = IstClock.Now;
//                 var todayIst = nowIst.Date;

//                 var enrollment = await _context.ParentsEnrollments.FirstOrDefaultAsync(pe => pe.Id == request.EnrollmentId);
//                 if (enrollment == null)
//                     return ApiError(404, "CHILD_NOT_FOUND", "This child's enrollment was not found.");

//                 var batchExists = await _context.GroupVariations.AnyAsync(gv => gv.Id == request.BatchId);
//                 if (!batchExists)
//                     return ApiError(404, "BATCH_NOT_FOUND", "This batch no longer exists.");

//                 var record = await _context.ChildAttendances
//                     .FirstOrDefaultAsync(ca => ca.ChildEnrollmentId == request.EnrollmentId
//                                             && ca.GroupVariationId == request.BatchId
//                                             && ca.AttendanceDate == todayIst);

//                 switch (action)
//                 {
//                     case "ABSENT":
//                         if (record == null)
//                         {
//                             record = new ChildAttendance
//                             {
//                                 ChildEnrollmentId = request.EnrollmentId,
//                                 GroupVariationId = request.BatchId,
//                                 AttendanceDate = todayIst
//                             };
//                             _context.ChildAttendances.Add(record);
//                         }
//                         record.IsPresent = false;
//                         record.CheckInTime = null;
//                         record.CheckOutTime = null;
//                         break;

//                     case "IN":
//                         if (record == null)
//                         {
//                             record = new ChildAttendance
//                             {
//                                 ChildEnrollmentId = request.EnrollmentId,
//                                 GroupVariationId = request.BatchId,
//                                 AttendanceDate = todayIst
//                             };
//                             _context.ChildAttendances.Add(record);
//                         }
//                         record.IsPresent = true;
//                         record.CheckInTime ??= nowIst.TimeOfDay; // keep the original arrival time if re-marked
//                         record.CheckOutTime = null;             // IN after OUT = child came back
//                         break;

//                     case "OUT":
//                         if (record == null || !record.IsPresent)
//                             return ApiError(400, "NOT_CHECKED_IN", "Mark the child IN before marking OUT.");
//                         record.CheckOutTime = nowIst.TimeOfDay;
//                         break;
//                 }

//                 await _context.SaveChangesAsync();

//                 return Ok(new
//                 {
//                     success = true,
//                     message = $"Marked {action}",
//                     attendance = new
//                     {
//                         id = record!.Id,
//                         isPresent = record.IsPresent,
//                         checkInTime = record.CheckInTime?.ToString(@"hh\:mm\:ss"),
//                         checkOutTime = record.CheckOutTime?.ToString(@"hh\:mm\:ss")
//                     }
//                 });
//             }
//             catch (Exception ex)
//             {
//                 _logger.LogError(ex, "MarkChildAttendance failed for Enrollment={EnrollmentId}", request?.EnrollmentId);
//                 var actualError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
//                 return ApiError(500, "SERVER_ERROR", $"Attendance could not be saved: {actualError}");
//             }
//         }

//         public class MarkChildAttendanceDto
//         {
//             public int EnrollmentId { get; set; }
//             public int BatchId { get; set; }
//             public string Action { get; set; } = string.Empty; // "IN", "OUT", "ABSENT"
//         }

//         [HttpGet("batch/{batchId}/attendance-history")]
//         public async Task<IActionResult> GetBatchAttendanceHistory(int batchId)
//         {
//             var batchHistory = await _context.GroupVariations
//                 .Include(gv => gv.ChildAttendances)
//                 .Where(gv => gv.Id == batchId)
//                 .Select(gv => new
//                 {
//                     BatchName = $"{gv.AgeGroup} ({gv.Days})",
//                     Time = gv.TimeSlot,
//                     TotalLogs = gv.ChildAttendances.Count(),
//                     AttendanceRecords = gv.ChildAttendances.Select(a => new
//                     {
//                         Date = a.AttendanceDate.ToString("yyyy-MM-dd"),
//                         IsPresent = a.IsPresent,
//                         CheckIn = a.CheckInTime,
//                         ChildId = a.ChildEnrollmentId
//                     }).ToList()
//                 })
//                 .FirstOrDefaultAsync();

//             if (batchHistory == null) return NotFound(new { success = false, code = "NOT_FOUND", message = "Batch not found" });
//             return Ok(batchHistory);
//         }

//         [HttpPost("attendance/finalize-batch")]
//         public IActionResult FinalizeBatch([FromBody] FinalizeBatchRequest req)
//         {
//             // Session remarks are now stored with the WhatsApp update (BatchBroadcasts).
//             return Ok(new { success = true, message = "Session finalized." });
//         }
//     }
// }





// // using Microsoft.AspNetCore.Mvc;
// // using Microsoft.EntityFrameworkCore;
// // using PwcApi.Data;
// // using PwcApi.DTOs;
// // using System.Net.Http.Headers;
// // using System.Text;             
// // using System.Text.Json;  
// // using PwcApi.Models;
// // using PwcApi.Services; 
// // using System;
// // using System.Linq;
// // using System.Threading.Tasks;
// // using System.Collections.Generic;
// // using System.Runtime.InteropServices;

// // namespace PwcApi.Controllers
// // {
// //     [ApiController]
// //     [Route("api/[controller]")]
// //     public class CounselorDashboardController : ControllerBase
// //     {
// //         private readonly AppDbContext _context;
// //         private readonly R2StorageService _r2Service;

// //         // 🔥 FIX: Added R2StorageService here and assigned it!
// //         public CounselorDashboardController(AppDbContext context, R2StorageService r2Service)
// //         {
// //             _context = context;
// //             _r2Service = r2Service;
// //         }

// //         [HttpPost("reports/send")]
// //         public async Task<IActionResult> SendParentReport([FromBody] SendReportRequest req)
// //         {
// //             // 1. Upload Media (if present)
// //             string? mediaUrl = null;
// //             if (!string.IsNullOrEmpty(req.ImageBase64)) {
// //                 mediaUrl = await _r2Service.UploadBase64ImageAsync(req.ImageBase64, $"report_{req.EnrollmentId}_{DateTime.UtcNow.Ticks}");
// //             }

// //             // 2. Save Report to Database
// //             var report = new ChildReport {
// //                 ChildEnrollmentId = req.EnrollmentId,
// //                 GroupVariationId = req.BatchId,
// //                 ReportDate = DateTime.UtcNow.Date,
// //                 TeacherNotes = req.Notes,
// //                 MediaUrl = mediaUrl,
// //                 SentViaWhatsApp = req.SendWhatsApp,
// //                 SentViaEmail = req.SendEmail
// //             };
// //             _context.ChildReports.Add(report);
// //             await _context.SaveChangesAsync();
            

// //             // 3. Trigger External APIs (Pseudo-code)
// //             if (req.SendWhatsApp) {
// //                 // await _whatsappService.SendMessage(req.ParentPhone, req.Notes, mediaUrl);
// //             }
// //             if (req.SendEmail) {
// //                 // await _emailService.SendEmail(req.ParentEmail, "Daily Update from PWC", req.Notes, mediaUrl);
// //             }

// //             return Ok(new { success = true });
// //         }
    

// //         // POST: api/counselordashboard/login
// //         [HttpPost("login")]
// //         public async Task<IActionResult> Login([FromBody] LoginRequest request)
// //         {
// //             var counselor = await _context.ResourceMasters
// //                 .FirstOrDefaultAsync(r => r.CompanyEmail == request.Email);

// //             if (counselor == null) 
// //             {
// //                 return BadRequest(new { message = $"DEBUG: The email '{request.Email}' was not found in the database." });
// //             }

// //             if (counselor.Password != request.Password) 
// //             {
// //                 return BadRequest(new { message = $"DEBUG: Password mismatch. DB has '{counselor.Password}', you sent '{request.Password}'." });
// //             }

// //             if (counselor.Role != "Counselor" && counselor.Role != "Coach") 
// //             {
// //                 return BadRequest(new { message = $"DEBUG: Role mismatch. Expected 'Counselor or Coach', DB has '{counselor.Role}'." });
// //             }

// //             if (counselor.IsActive != 1) 
// //             {
// //                 return BadRequest(new { message = $"DEBUG: IsActive is {counselor.IsActive}, but we expected 1." });
// //             }

// //             return Ok(new LoginResponse { 
// //                 CounselorId = counselor.Id, 
// //                 Name = counselor.Name ?? "Unknown", 
// //                 EmpId = counselor.EmpId ?? "Unknown" 
// //             });
// //         }

// //      [HttpPost("add-lead")]
// // public async Task<IActionResult> AddPotentialLead([FromBody] AddLeadRequest request)
// // {
// //     try
// //     {
// //         var newLead = new PotentialParent 
// //         {
// //             School_Name = request.SchoolId,
// //             Parent_Name = request.ParentName,
// //             Parent_Email = request.ParentEmail,
// //             Parent_Phone = request.ParentPhone,
// //             Child_Name = request.ChildName,
// //             Child_DOB = string.IsNullOrWhiteSpace(request.ChildDOB) ? null : DateTime.Parse(request.ChildDOB),
// //             InterestLevel = "Moderate",
// //             HasBeenContacted = false,
// //             CreatedAt = DateTime.UtcNow,
            
// //             // 🔥 FIX: Explicitly set MediaConsent to 0 (or 1) to satisfy NOT NULL constraint
// //             MediaConsent = 0 
// //         };

// //         _context.Potential_Parents.Add(newLead);
// //         await _context.SaveChangesAsync();

// //         return Ok(new { success = true, message = "Lead added successfully." });
// //     }
// //     catch (DbUpdateException ex)
// //     {
// //         var innerEx = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
// //         return StatusCode(500, new { success = false, message = "Database Error: " + innerEx });
// //     }
// //     catch (Exception ex)
// //     {
// //         return StatusCode(500, new { success = false, message = "General Error: " + ex.Message });
// //     }
// // }

// // // Add this Request DTO at the bottom of the file
// // // 🔥 FIX: Added '?' to explicitly allow nullable strings and fix the CS8618 warnings
// //     public class AddLeadRequest 
// //     {
// //         public string? SchoolId { get; set; }
// //         public int CounselorId { get; set; }
// //         public string? ParentName { get; set; }
// //         public string? ParentEmail { get; set; }
// //         public string? ParentPhone { get; set; }
// //         public string? ChildName { get; set; }
// //         public string? ChildDOB { get; set; }
// //     }
// //         // ==========================================
// //         // 🔥 NEW: GET COUNSELOR PROFILE ENDPOINT
// //         // ==========================================
// //         // GET: api/counselordashboard/profile/{counselorId}
// //       // GET: api/counselordashboard/profile/{counselorId}
// //         [HttpGet("profile/{counselorId}")]
// //         public async Task<IActionResult> GetProfile(int counselorId)
// //         {
// //             var counselor = await _context.ResourceMasters
// //                 .FirstOrDefaultAsync(r => r.Id == counselorId && (r.Role == "Counselor" || r.Role == "Coach"));

// //             if (counselor == null)
// //             {
// //                 return NotFound(new { message = "Counselor not found or inactive." });
// //             }

// //             var profile = new 
// //             {
// //                 id = counselor.Id,
// //                 name = counselor.Name ?? "Unknown",
// //                 role = counselor.Role ?? "Counselor",
// //                 isActive = counselor.IsActive == 1, 
// //                 companyEmail = counselor.CompanyEmail ?? string.Empty,
// //                 personalEmail = counselor.Email ?? string.Empty, 
// //                 empId = counselor.EmpId ?? string.Empty,
// //                 phone = counselor.Phone ?? string.Empty,
                
// //                 // 🔥 CHANGE THIS LINE: Convert the DateTime back to a clean string format for Android
// //                 dateOfJoining = counselor.DateOfJoining?.ToString("yyyy-MM-dd") ?? string.Empty
// //             };

// //             return Ok(profile);
// //         }
// //         // GET: api/counselordashboard/centres/{counselorId}
// //         // [HttpGet("centres/{counselorId}")]
// //         // public async Task<IActionResult> GetCentres(int counselorId)
// //         // {
// //         //     // Keeps the list view lightweight
// //         //     var schools = await _context.SchoolMaster
// //         //         .Where(s => s.CounselorId == counselorId)
// //         //         .Select(s => new CentreResponse  
// //         //         { 
// //         //             SchoolId = s.SchoolId, 
// //         //             SchoolName = s.SchoolName ?? "Unknown Centre", 
// //         //             Address = $"{s.SchoolCity}, {s.SchoolAddress}", 
// //         //             ContactName = s.ContactPersonName ?? "N/A", 
// //         //             ContactPhone = s.ContactPersonPhone ?? "N/A"
// //         //         })
// //         //         .ToListAsync();

// //         //     if (!schools.Any())
// //         //     {
// //         //         return NotFound(new { message = "No centres assigned to this counselor." });
// //         //     }

// //         //     return Ok(schools);
// //         // }

// //     [HttpGet("centres/{userId}")]
// // public async Task<IActionResult> GetCentres(int userId)
// // {
// //     try
// //     {
// //         // 1. Find school IDs assigned directly via CounselorId
// //         var counselorSchoolIds = await _context.SchoolMaster
// //             .Where(s => s.CounselorId == userId)
// //             .Select(s => s.SchoolId)
// //             .ToListAsync();

// //         // 2. Find school IDs assigned via SessionMasters (for the Coach role)
// //         var coachSchoolIds = await _context.SessionMasters
// //             .Where(sm => (sm.CoachId == userId.ToString() || sm.Id == userId) && sm.IsActive == 1)
// //             .Select(sm => sm.SchoolId)
// //             .Distinct()
// //             .ToListAsync();

// //         // 3. Combine both lists to get all unique assigned School IDs
// //         var allAssignedSchoolIds = counselorSchoolIds
// //             .Union(coachSchoolIds)
// //             .Distinct()
// //             .ToList();

// //         // 4. Fetch ALL schools (Removed the .Where filter)
// //         var allSchools = await _context.SchoolMaster
// //             .Select(s => new
// //             {
// //                 SchoolId = s.SchoolId,
// //                 SchoolName = s.SchoolName ?? "Unknown Centre",
// //                 SchoolCity = s.SchoolCity,
// //                 SchoolAddress = s.SchoolAddress,
// //                 ContactPersonName = s.ContactPersonName ?? "N/A",
// //                 ContactPersonPhone = s.ContactPersonPhone ?? "N/A",
                
// //                 // 🔥 NEW: Check if this school belongs to the coach/counselor
// //                 IsAssigned = allAssignedSchoolIds.Contains(s.SchoolId)
// //             })
// //             .ToListAsync();

// //         // 5. Order the list: Assigned centres at the top (true comes before false in descending), 
// //         // then order alphabetically by School Name
// //         var orderedSchools = allSchools
// //             .OrderByDescending(s => s.IsAssigned)
// //             .ThenBy(s => s.SchoolName)
// //             .ToList();

// //         return Ok(orderedSchools);
// //     }
// //     catch (Exception ex)
// //     {
// //         var innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
// //         return StatusCode(500, new { message = $"Error fetching centres: {innerMsg}" });
// //     }
// // }


// // //         [HttpGet("centres/{userId}")]
// // // public async Task<IActionResult> GetCentres(int userId)
// // // {
// // //     try
// // //     {
// // //         // 1. Find school IDs assigned directly via CounselorId
// // //         var counselorSchoolIds = await _context.SchoolMaster
// // //             .Where(s => s.CounselorId == userId)
// // //             .Select(s => s.SchoolId)
// // //             .ToListAsync();

// // //         // 2. Find school IDs assigned via SessionMasters (for the Coach role)
// // //         var coachSchoolIds = await _context.SessionMasters
// // //             .Where(sm => (sm.CoachId == userId.ToString() || sm.Id == userId) && sm.IsActive == 1)
// // //             .Select(sm => sm.SchoolId)
// // //             .Distinct()
// // //             .ToListAsync();

// // //         // 3. Combine both lists to get all unique assigned School IDs
// // //         var allAssignedSchoolIds = counselorSchoolIds
// // //             .Union(coachSchoolIds)
// // //             .Distinct()
// // //             .ToList();

// // //         if (!allAssignedSchoolIds.Any())
// // //         {
// // //             return Ok(new List<object>()); // Return empty list instead of throwing an error
// // //         }

// // //         // 4. Fetch the complete school details for all mapped IDs matching your SQL logs criteria
// // //         var schools = await _context.SchoolMaster
// // //             .Where(s => allAssignedSchoolIds.Contains(s.SchoolId))
// // //             .Select(s => new
// // //             {
// // //                 SchoolId = s.SchoolId,
// // //                 SchoolName = s.SchoolName ?? "Unknown Centre",
// // //                 SchoolCity = s.SchoolCity,
// // //                 SchoolAddress = s.SchoolAddress,
// // //                 ContactPersonName = s.ContactPersonName ?? "N/A",
// // //                 ContactPersonPhone = s.ContactPersonPhone ?? "N/A"
// // //             })
// // //             .ToListAsync();

// // //         return Ok(schools);
// // //     }
// // //     catch (Exception ex)
// // //     {
// // //         var innerMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
// // //         return StatusCode(500, new { message = $"Error fetching assigned centres: {innerMsg}" });
// // //     }
// // // }

// //         // GET: api/counselordashboard/centre/{schoolId}
// //         // NEW ENDPOINT: Returns complete information for a specific centre
// //         [HttpGet("centre/{schoolId}")]
// //         public async Task<IActionResult> GetCentreDetails(string schoolId)
// //         {
// //             var s = await _context.SchoolMaster.FirstOrDefaultAsync(x => x.SchoolId == schoolId.Trim());

// //             if (s == null)
// //             {
// //                 return NotFound(new { success = false, message = "Centre not found." });
// //             }

// //             // Map database model into clean structured JSON response
// //             var response = new 
// //             {
// //                 success = true,
// //                 data = new 
// //                 {
// //                     schoolId = s.SchoolId,
// //                     schoolName = s.SchoolName,
// //                     schoolEmail = s.SchoolEmail,
// //                     schoolPhone = s.SchoolPhone,
// //                     onBoardingId = s.OnBoardingId,
// //                     createdDate = s.CreatedDate,
// //                     billingAddress = s.BillingAddress,
// //                     contactPersonName = s.ContactPersonName,
// //                     contactPersonEmail = s.ContactPersonEmail,
// //                     contactPersonPhone = s.ContactPersonPhone,
// //                     ownerName = s.OwnerName,
// //                     ownerEmail = s.OwnerEmail,
// //                     ownerPhone = s.OwnerPhone,
// //                     principalName = s.PrincipalName,
// //                     principalEmail = s.PrincipalEmail,
// //                     principalPhone = s.PrincipalPhone,
// //                     schoolAddress = s.SchoolAddress,
// //                     schoolCity = s.SchoolCity,
// //                     schoolCountry = s.SchoolCountry,
// //                     schoolLocation = s.SchoolLocation,
// //                     schoolPincode = s.SchoolPincode,
// //                     schoolState = s.SchoolState,
// //                     shippingAddress = s.ShippingAddress,
// //                     plan = s.Plan,
// //                     isEnrolled = s.IsEnrolled,
// //                     counselorId = s.CounselorId,
// //                     facilitationCharges = s.FacilitationCharges,
// //                     salesPersonId = s.SalesPersonId,
// //                     sessionContacts = new[] 
// //                     {
// //                         new { name = s.SessionContact1Name, email = s.SessionContact1Email, phone = s.SessionContact1Phone, position = s.SessionContact1Position },
// //                         new { name = s.SessionContact2Name, email = s.SessionContact2Email, phone = s.SessionContact2Phone, position = s.SessionContact2Position },
// //                         new { name = s.SessionContact3Name, email = s.SessionContact3Email, phone = s.SessionContact3Phone, position = s.SessionContact3Position },
// //                         new { name = s.SessionContact4Name, email = s.SessionContact4Email, phone = s.SessionContact4Phone, position = s.SessionContact4Position }
// //                     },
// //                     activityCount = s.ActivityCount,
// //                     promoCode = s.PromoCode,
// //                     promoDiscount = s.PromoDiscount
// //                 }
// //             };

// //             return Ok(response);
// //         }

// //         // GET: api/counselordashboard/sessions/{schoolId}
// //         [HttpGet("sessions/{schoolId}")]
// //         public async Task<IActionResult> GetSessionsForSchool(string schoolId)
// //         {
// //             try
// //             {
// //                 var cleanSchoolId = schoolId.Trim();

// //                 var sessions = await _context.SessionMasters
// //                     .Where(s => s.SchoolId == cleanSchoolId)
// //                     .Select(s => new 
// //                     { 
// //                         sessionId = s.Id,
// //                         sessionName = s.Id 
// //                     })
// //                     .ToListAsync();

// //                 if (!sessions.Any())
// //                 {
// //                     return NotFound(new { message = "No active sessions found for this school." });
// //                 }

// //                 return Ok(sessions);
// //             }
// //             catch (Exception ex)
// //             {
// //                 return StatusCode(500, new { message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
// //             }
// //         }

// //         // GET: api/counselordashboard/parents/{schoolId}
// //         [HttpGet("parents/{schoolId}")]
// //         public async Task<IActionResult> GetParents(string schoolId)
// //         {
// //             try
// //             {
// //                 var cleanSchoolId = schoolId.Trim();

// //                 // 1. Fetch Complete Potential Parents
// //                 var potentialParents = await _context.Potential_Parents
// //                     .Where(p => p.School_Name != null && p.School_Name.Trim() == cleanSchoolId) 
// //                     .Select(p => new ParentResponse 
// //                     { 
// //                         Id = p.Id,
// //                         Status = "Potential",
// //                         ParentName = p.Parent_Name, 
// //                         ParentEmail = p.Parent_Email,
// //                         ParentPhone = p.Parent_Phone, 
// //                         ChildName = p.Child_Name, 
// //                         ChildDOB = p.Child_DOB,
// //                         ChildSchoolName = p.ChildSchoolName,
// //                         CreatedAt = p.CreatedAt,
// //                         MediaConsent = p.MediaConsent,
// //                         Remark = p.Remark,
                        
// //                         // Enrollment fields default to null/empty for Potential parents
// //                         PaymentStatus = "N/A",
// //                           InterestLevel = p.InterestLevel,
// //                         FollowUpDate = p.FollowUpDate.HasValue ? p.FollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : null,
// //                         HasBeenContacted = p.HasBeenContacted
// //                     })
// //                     .ToListAsync();

// //                 // 2. Fetch Complete Enrolled Parents
// //                 var enrolledParents = await _context.ParentsEnrollments
// //                     .Where(p => p.SchoolId != null && p.SchoolId.Trim() == cleanSchoolId)
// //                     .Select(p => new ParentResponse 
// //                     { 
// //                         Id = p.Id,
// //                         Status = "Enrolled",
// //                         ParentName = p.ParentName, 
// //                         ParentEmail = p.ParentEmail,
// //                         ParentPhone = p.ParentPhone, 
// //                         ChildName = p.ChildName, 
// //                         ChildDOB = p.ChildDOB,
// //                         ChildSchoolName = p.ChildSchoolName,
// //                         ChildSchoolCity = p.ChildSchoolCity,
// //                         CreatedAt = p.CreatedAt,
// //                         MediaConsent = p.MediaConsent,
                        
// //                         // Payment & Billing
// //                         PaymentStatus = p.PaymentStatus ?? "Pending",
// //                         PaymentDate = p.PaymentDate,
// //                         PaymentAmount = p.PaymentAmount,
// //                         BillingAddress = p.BillingAddress,
// //                         BillingCity = p.BillingCity,
// //                         BillingState = p.BillingState,
// //                         BillingPincode = p.BillingPincode,
                        
// //                         // Sessions
// //                         SessionId = p.SessionId,
// //                         SessionName = p.SessionName,
// //                         SessionAgeGroup = p.SessionAgeGroup,
// //                         SessionDays = p.SessionDays,
// //                         SessionFrequency = p.SessionFrequency,
// //                         SessionTimeSlot = p.SessionTimeSlot,
                        
// //                         // Discounts
// //                         DiscountAmount = p.DiscountAmount,
// //                         DiscountCode = p.DiscountCode,
// //                         InterestLevel = p.InterestLevel,
// //                         FollowUpDate = p.FollowUpDate.HasValue ? p.FollowUpDate.Value.ToString("yyyy-MM-dd HH:mm") : null,
// //                         HasBeenContacted = p.HasBeenContacted
// //                     })
// //                     .ToListAsync();

// //                 // 3. Combine and sort
// //                 var allParents = potentialParents.Concat(enrolledParents).OrderBy(p => p.Status).ToList();

// //                 return Ok(new { success = true, data = allParents });
// //             }
// //             catch (Exception ex)
// //             {
// //                 return StatusCode(500, new { success = false, message = $"DB ERROR: {ex.InnerException?.Message ?? ex.Message}" });
// //             }
// //         }

// //              [HttpPut("update-status")]
// // public async Task<IActionResult> UpdateParentStatus([FromBody] UpdateParentStatusRequest request)
// // {
// //     try
// //     {
// //         // Parse the nullable date coming from Android
// //         DateTime? parsedFollowUpDate = null;
// //         if (!string.IsNullOrEmpty(request.FollowUpDate))
// //         {
// //             // 🔥 FIX: Added " HH:mm" to the expected format string!
// //             if (DateTime.TryParseExact(request.FollowUpDate, "yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime tempDate))
// //             {
// //                 parsedFollowUpDate = tempDate;
// //             }
// //         }

// //         bool isUpdated = false;

// //         // 1. First, check if the parent exists in the PotentialParents table
// //         var potentialParent = await _context.Potential_Parents.FindAsync(request.ParentId);
// //         if (potentialParent != null)
// //         {
// //             potentialParent.InterestLevel = request.InterestLevel;
// //             potentialParent.FollowUpDate = parsedFollowUpDate;
// //             potentialParent.HasBeenContacted = true; // Mark as contacted

// //             _context.Potential_Parents.Update(potentialParent);
// //             isUpdated = true;
// //         }
// //         else
// //         {
// //             // 2. If not found in Potential, check the ParentEnrollments table
// //             var enrolledParent = await _context.ParentsEnrollments.FindAsync(request.ParentId);
// //             if (enrolledParent != null)
// //             {
// //                 enrolledParent.InterestLevel = request.InterestLevel;
// //                 enrolledParent.FollowUpDate = parsedFollowUpDate;
// //                 enrolledParent.HasBeenContacted = true; // Mark as contacted

// //                 _context.ParentsEnrollments.Update(enrolledParent);
// //                 isUpdated = true;
// //             }
// //         }

// //         // 3. If neither table had a matching ID, return a 404 Not Found
// //         if (!isUpdated)
// //         {
// //             return NotFound(new { success = false, message = "Parent not found in any database table." });
// //         }

// //         // Save the changes to whichever table was updated
// //         await _context.SaveChangesAsync();

// //         return Ok(new { success = true, message = "Status updated successfully." });
// //     }
// //     catch (Exception ex)
// //     {
// //         return StatusCode(500, new { success = false, message = ex.Message });
// //     }
// // }

// // private DateTime GetIstTime()
// // {
// //     var tzId = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? "India Standard Time" : "Asia/Kolkata";
// //     var istZone = TimeZoneInfo.FindSystemTimeZoneById(tzId);
// //     return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, istZone);
// // }

// // [HttpGet("coach/{coachId}/batches")]
// // public async Task<IActionResult> GetCoachBatches(string coachId)
// // {
// //     var nowIst = GetIstTime();
// //     var todayIst = nowIst.Date;

// //     // 🔥 1. Fetch ALL Active Sessions (Removed the CoachId filter)
// //     var sessionMasters = await _context.SessionMasters
// //         .Where(sm => sm.IsActive == 1 ) 
// //         .ToListAsync();

// //     var sessionIds = sessionMasters.Select(sm => sm.Id).ToList();
// //     if (!sessionIds.Any()) return Ok(new List<object>());

// //     var schoolIds = sessionMasters.Select(sm => sm.SchoolId).Distinct().ToList();
// //     var schools = await _context.SchoolMaster
// //         .Where(s => schoolIds.Contains(s.SchoolId))
// //         .ToListAsync();

// //     var batches = await _context.GroupVariations
// //         .Where(gv => sessionIds.Contains(gv.SessionId))
// //         .OrderBy(gv => gv.Id) 
// //         .ToListAsync();

// //     var batchIds = batches.Select(b => b.Id).ToList();

// //     var batchAgeGroups = batches.Select(b => b.AgeGroup).Distinct().ToList();
    
// //     var relevantKits = await _context.Set<PwcApi.Models.KitMaster>()  
// //         .Include(k => k.KitItems)
// //             .ThenInclude(ki => ki.Item)
// //         .Where(k => batchAgeGroups.Contains(k.AgeGroup))
// //         .ToListAsync();

// //     var children = await _context.ParentsEnrollments
// //         .Where(pe => pe.GroupVariationId != null && batchIds.Contains(pe.GroupVariationId.Value) && pe.PaymentStatus == "Paid")
// //         .ToListAsync();

// //     var childIds = children.Select(c => c.Id).ToList();

// //     var todayAttendances = await _context.ChildAttendances
// //         .Where(a => childIds.Contains(a.ChildEnrollmentId) && a.AttendanceDate == todayIst)
// //         .ToListAsync();

// //     var unsortedResult = batches.Select(gv => 
// //     {
// //         var session = sessionMasters.FirstOrDefault(sm => sm.Id == gv.SessionId);
// //         var school = schools.FirstOrDefault(s => s.SchoolId == session?.SchoolId);

// //         var sessionBatches = batches.Where(b => b.SessionId == gv.SessionId).OrderBy(b => b.Id).ToList();
// //         int batchIndex = sessionBatches.IndexOf(gv); 

// //         var matchedKits = relevantKits.Where(k => k.AgeGroup == gv.AgeGroup).ToList();

// //         var orderedPdfs = matchedKits
// //             .SelectMany(k => k.KitItems)
// //             .Where(ki => ki.Item != null && !string.IsNullOrWhiteSpace(ki.Item.LessonPlanPdf))
// //             .Select(ki => ki.Item)
// //             .OrderBy(item => item.Sequence) 
// //             .Select(item => item.LessonPlanPdf)
// //             .Distinct()
// //             .ToList();

// //         string mappedPdf = null;
// //         if (batchIndex >= 0 && batchIndex < orderedPdfs.Count)
// //         {
// //             mappedPdf = orderedPdfs[batchIndex]; 
// //         }

// //         // 🔥 2. Determine if this batch belongs to the coach requesting the API
// //         bool isAssigned = session?.CoachId == coachId;

// //         return new 
// //         {
// //             BatchId = gv.Id,
// //             AgeGroup = gv.AgeGroup,
// //             Timing = gv.TimeSlot,
// //             Days = gv.Days, 
// //             TotalEnrolled = children.Count(pe => pe.GroupVariationId == gv.Id),
            
// //             CentreName = school?.SchoolName ?? "Unknown Centre",
// //             CentreAddress = $"{school?.SchoolAddress}, {school?.SchoolCity}",
// //             Status = "UPCOMING",

// //             // 🔥 3. Pass the flag to Android
// //             IsAssigned = isAssigned, 

// //             LessonPlanPdf = mappedPdf,
            
// //             Children = children
// //                 .Where(pe => pe.GroupVariationId == gv.Id)
// //                 .Select(child => 
// //                 {
// //                     var attendance = todayAttendances.FirstOrDefault(a => a.ChildEnrollmentId == child.Id);
// //                     return new 
// //                     { 
// //                         EnrollmentId = child.Id, 
// //                         BatchId = gv.Id, 
// //                         ChildName = child.ChildName, 
// //                         ParentName = child.ParentName, 
// //                         ParentPhone = child.ParentPhone,
// //                         parentEmail = child.ParentEmail, // Keeping this if you added it earlier
                        
// //                         TodayAttendance = attendance == null ? null : new 
// //                         {
// //                             Id = attendance.Id,
// //                             IsPresent = attendance.IsPresent,
// //                             CheckInTime = attendance.CheckInTime?.ToString(@"hh\:mm\:ss"),
// //                             CheckOutTime = attendance.CheckOutTime?.ToString(@"hh\:mm\:ss")
// //                         }
// //                     };
// //                 }).ToList()
// //         };
// //     });

// //     // 🔥 4. Sort: Assigned batches at the top, then alphabetically by Centre
// //     var result = unsortedResult
// //         .OrderByDescending(b => b.IsAssigned)
// //         .ThenBy(b => b.CentreName)
// //         .ToList();

// //     return Ok(result);
// // }


// // // [HttpGet("coach/{coachId}/batches")]
// // // public async Task<IActionResult> GetCoachBatches(string coachId)
// // // {
// // //     var nowIst = GetIstTime();
// // //     var todayIst = nowIst.Date;

// // //     //     // 🔥 2. Filter ONLY Active Sessions (IsActive == true or 1)
// // //     var sessionMasters = await _context.SessionMasters
// // //         .Where(sm => sm.CoachId == coachId && sm.IsActive == 1 ) 
// // //         .ToListAsync();

// // //     var sessionIds = sessionMasters.Select(sm => sm.Id).ToList();
// // //     if (!sessionIds.Any()) return Ok(new List<object>());

// // //     // 1. Fetch Sessions (🔥 REMOVED the crashing SessionKits Include)
// // //     // var sessionMasters = await _context.SessionMasters
// // //     //     // Note: Keep your existing IsActive check here (whether it's == 1 or == true based on your local code)
// // //     //     .Where(sm => sm.CoachId == coachId) 
// // //     //     .ToListAsync();

// // //     // var sessionIds = sessionMasters.Select(sm => sm.Id).ToList();
// // //     // if (!sessionIds.Any()) return Ok(new List<object>());

// // //     var schoolIds = sessionMasters.Select(sm => sm.SchoolId).Distinct().ToList();
// // //     var schools = await _context.SchoolMaster
// // //         .Where(s => schoolIds.Contains(s.SchoolId))
// // //         .ToListAsync();

// // //     var batches = await _context.GroupVariations
// // //         .Where(gv => sessionIds.Contains(gv.SessionId))
// // //         .OrderBy(gv => gv.Id) // Ensure deterministic order for sequences
// // //         .ToListAsync();

// // //     var batchIds = batches.Select(b => b.Id).ToList();

// // //     // 🔥 2. NEW MAPPING: Fetch Kits by AgeGroup! 
// // //     // We find the unique AgeGroups from the Coach's batches
// // //     var batchAgeGroups = batches.Select(b => b.AgeGroup).Distinct().ToList();
    
// // //     // We fetch the Kits that belong to those AgeGroups, including the Items & PDFs
// // // var relevantKits = await _context.Set<PwcApi.Models.KitMaster>()  
// // //       .Include(k => k.KitItems)
// // //             .ThenInclude(ki => ki.Item)
// // //         .Where(k => batchAgeGroups.Contains(k.AgeGroup))
// // //         .ToListAsync();

// // //     var children = await _context.ParentsEnrollments
// // //         .Where(pe => pe.GroupVariationId != null && batchIds.Contains(pe.GroupVariationId.Value) && pe.PaymentStatus == "Paid")
// // //         .ToListAsync();

// // //     var childIds = children.Select(c => c.Id).ToList();

// // //     var todayAttendances = await _context.ChildAttendances
// // //         .Where(a => childIds.Contains(a.ChildEnrollmentId) && a.AttendanceDate == todayIst)
// // //         .ToListAsync();

// // //     var result = batches.Select(gv => 
// // //     {
// // //         var session = sessionMasters.FirstOrDefault(sm => sm.Id == gv.SessionId);
// // //         var school = schools.FirstOrDefault(s => s.SchoolId == session?.SchoolId);

// // //         // STEP A: Find the sequence index of this specific batch within the session (0 to 7)
// // //         var sessionBatches = batches.Where(b => b.SessionId == gv.SessionId).OrderBy(b => b.Id).ToList();
// // //         int batchIndex = sessionBatches.IndexOf(gv); 

// // //         // 🔥 STEP B: Get PDFs mapped by matching the Kit's AgeGroup to the Batch's AgeGroup
// // //         var matchedKits = relevantKits.Where(k => k.AgeGroup == gv.AgeGroup).ToList();

// // //         var orderedPdfs = matchedKits
// // //             .SelectMany(k => k.KitItems)
// // //             .Where(ki => ki.Item != null && !string.IsNullOrWhiteSpace(ki.Item.LessonPlanPdf))
// // //             .Select(ki => ki.Item)
// // //             .OrderBy(item => item.Sequence) // Order them properly
// // //             .Select(item => item.LessonPlanPdf)
// // //             .Distinct()
// // //             .ToList();

// // //         // STEP C: 1-to-1 Mapping! Give this batch its exact matching PDF
// // //         string mappedPdf = null;
// // //         if (batchIndex >= 0 && batchIndex < orderedPdfs.Count)
// // //         {
// // //             mappedPdf = orderedPdfs[batchIndex]; 
// // //         }

// // //         return new 
// // //         {
// // //             BatchId = gv.Id,
// // //             AgeGroup = gv.AgeGroup,
// // //             Timing = gv.TimeSlot,
// // //             Days = gv.Days, 
// // //             TotalEnrolled = children.Count(pe => pe.GroupVariationId == gv.Id),
            
// // //             CentreName = school?.SchoolName ?? "Unknown Centre",
// // //             CentreAddress = $"{school?.SchoolAddress}, {school?.SchoolCity}",
// // //             Status = "UPCOMING",

// // //             // 🔥 Output the single, correct PDF mapped exactly to this batch's sequence!
// // //             LessonPlanPdf = mappedPdf,
            
// // //             Children = children
// // //                 .Where(pe => pe.GroupVariationId == gv.Id)
// // //                 .Select(child => 
// // //                 {
// // //                     var attendance = todayAttendances.FirstOrDefault(a => a.ChildEnrollmentId == child.Id);
// // //                     return new 
// // //                     { 
// // //                         EnrollmentId = child.Id, 
// // //                         BatchId = gv.Id, 
// // //                         ChildName = child.ChildName, 
// // //                         ParentName = child.ParentName, 
// // //                         ParentPhone = child.ParentPhone,
                        
// // //                         TodayAttendance = attendance == null ? null : new 
// // //                         {
// // //                             Id = attendance.Id,
// // //                             IsPresent = attendance.IsPresent,
// // //                             CheckInTime = attendance.CheckInTime?.ToString(@"hh\:mm\:ss"),
// // //                             CheckOutTime = attendance.CheckOutTime?.ToString(@"hh\:mm\:ss")
// // //                         }
// // //                     };
// // //                 }).ToList()
// // //         };
// // //     }).ToList();

// // //     return Ok(result);
// // // }

// // // [HttpGet("coach/{coachId}/batches")]
// // // public async Task<IActionResult> GetCoachBatches(string coachId)
// // // {
// // //     // 🔥 1. Use Strict IST Time
// // //     var nowIst = GetIstTime();
// // //     var todayIst = nowIst.Date;

// // //     // 🔥 2. Filter ONLY Active Sessions (IsActive == true or 1)
// // //     var sessionMasters = await _context.SessionMasters
// // //         .Where(sm => sm.CoachId == coachId && sm.IsActive == 1) 
// // //         .ToListAsync();

// // //     var sessionIds = sessionMasters.Select(sm => sm.Id).ToList();
// // //     if (!sessionIds.Any()) return Ok(new List<object>());

// // //     var schoolIds = sessionMasters.Select(sm => sm.SchoolId).Distinct().ToList();
// // //     var schools = await _context.SchoolMaster
// // //         .Where(s => schoolIds.Contains(s.SchoolId))
// // //         .ToListAsync();

// // //     var batches = await _context.GroupVariations
// // //         .Where(gv => sessionIds.Contains(gv.SessionId))
// // //         .ToListAsync();

// // //     var batchIds = batches.Select(b => b.Id).ToList();

// // //     // 🔥 3. Filter ONLY Active Children (IsActive == true or 1)
// // //     var children = await _context.ParentsEnrollments
// // //         .Where(pe => pe.GroupVariationId != null 
// // //                   && batchIds.Contains(pe.GroupVariationId.Value))
// // //                           .ToListAsync();

// // //     var childIds = children.Select(c => c.Id).ToList();

// // //     var todayAttendances = await _context.ChildAttendances
// // //         .Where(a => childIds.Contains(a.ChildEnrollmentId) && a.AttendanceDate == todayIst)
// // //         .ToListAsync();

// // //     var result = batches.Select(gv => 
// // //     {
// // //         var session = sessionMasters.FirstOrDefault(sm => sm.Id == gv.SessionId);
// // //         var school = schools.FirstOrDefault(s => s.SchoolId == session?.SchoolId);

// // //         return new 
// // //         {
// // //             BatchId = gv.Id,
// // //             AgeGroup = gv.AgeGroup,
// // //             Timing = gv.TimeSlot,
// // //             Days = gv.Days, 
// // //             TotalEnrolled = children.Count(pe => pe.GroupVariationId == gv.Id),
            
// // //             CentreName = school?.SchoolName ?? "Unknown Centre",
// // //             CentreAddress = $"{school?.SchoolAddress}, {school?.SchoolCity}",
// // //             Status = "UPCOMING",
            
// // //             Children = children
// // //                 .Where(pe => pe.GroupVariationId == gv.Id)
// // //                 .Select(child => 
// // //                 {
// // //                     var attendance = todayAttendances.FirstOrDefault(a => a.ChildEnrollmentId == child.Id);
// // //                     return new 
// // //                     { 
// // //                         EnrollmentId = child.Id, 
// // //                         BatchId = gv.Id, 
// // //                         ChildName = child.ChildName, 
// // //                         ParentName = child.ParentName, 
// // //                         ParentPhone = child.ParentPhone,
                        
// // //                         TodayAttendance = attendance == null ? null : new 
// // //                         {
// // //                             Id = attendance.Id,
// // //                             IsPresent = attendance.IsPresent,
// // //                             CheckInTime = attendance.CheckInTime?.ToString(@"hh\:mm\:ss"),
// // //                             CheckOutTime = attendance.CheckOutTime?.ToString(@"hh\:mm\:ss")
// // //                         }
// // //                     };
// // //                 }).ToList()
// // //         };
// // //     }).ToList();

// // //     return Ok(result);
// // // }
// // // [HttpGet("coach/{coachId}/batches")]
// // // public async Task<IActionResult> GetCoachBatches(string coachId)
// // // {
// // //     // 1. Get all sessions taught by this coach
// // //     var sessionIds = await _context.SessionMasters
// // //         .Where(sm => sm.CoachId == coachId)
// // //         .Select(sm => sm.Id)
// // //         .ToListAsync();

// // //     // 2. Get all children enrolled in those sessions
// // //     var enrolledKids = await _context.ParentsEnrollments
// // //         .Where(pe => sessionIds.Contains(pe.SessionId))
// // //         .ToListAsync();

// // //     // 3. Group the children into "Batches" based on the text strings
// // //     var batches = enrolledKids
// // //         .GroupBy(pe => new { pe.SessionAgeGroup, pe.SessionTimeSlot, pe.SessionDays })
// // //         .Select(group => new 
// // //         {
// // //             AgeGroup = group.Key.SessionAgeGroup,
// // //             Timing = group.Key.SessionTimeSlot,
// // //             Days = group.Key.SessionDays,
// // //             TotalEnrolled = group.Count(),
// // //             Children = group.Select(k => new { k.ChildName, k.ParentName, k.ParentPhone }).ToList()
// // //         });

// // //     return Ok(batches);
// // // }


// // [HttpPost("bulk-whatsapp")]
// //         public async Task<IActionResult> SendBulkWhatsApp([FromBody] BulkWhatsAppRequest request)
// //         {
// //             try
// //             {
// //                 // 1. Validate the incoming request from Android
// //                 if (request.ParentIds == null || !request.ParentIds.Any())
// //                     return BadRequest(new { success = false, message = "No parents selected." });

// //                 if (string.IsNullOrWhiteSpace(request.Message) && string.IsNullOrWhiteSpace(request.MediaBase64))
// //                     return BadRequest(new { success = false, message = "Either a Message or Media must be provided." });

// //                 // 2. Fetch the Counselor to get THEIR specific Whapi Token
// //                 var counselor = await _context.ResourceMasters.FindAsync(request.CounselorId);
                
// //                 if (counselor == null)
// //                     return NotFound(new { success = false, message = "Counselor not found in the system." });

// //                 string whapiToken = counselor.WhapiToken;

// //                 if (string.IsNullOrEmpty(whapiToken))
// //                 {
// //                     return BadRequest(new { success = false, message = "This counselor has not linked their WhatsApp via Whapi yet." });
// //                 }

// //                 // 3. Fetch phone numbers from Potential Parents
// //                 var potentialPhones = await _context.Potential_Parents
// //                     .Where(p => request.ParentIds.Contains(p.Id) && !string.IsNullOrEmpty(p.Parent_Phone))
// //                     .Select(p => p.Parent_Phone)
// //                     .ToListAsync();

// //                 // 4. Fetch phone numbers from Enrolled Parents
// //                 var enrolledPhones = await _context.ParentsEnrollments
// //                     .Where(e => request.ParentIds.Contains(e.Id) && !string.IsNullOrEmpty(e.ParentPhone))
// //                     .Select(e => e.ParentPhone)
// //                     .ToListAsync();

// //                 // Combine and remove any duplicate numbers
// //                 var allPhoneNumbers = potentialPhones.Concat(enrolledPhones).Distinct().ToList();
// //                 int totalAttempted = allPhoneNumbers.Count;

// //                 if (totalAttempted == 0)
// //                     return BadRequest(new { success = false, message = "No valid phone numbers found for the selected parents." });

// //                 // 5. Determine Whapi Endpoint Type (text, image, document, video)
// //                 string endpointType = string.IsNullOrWhiteSpace(request.MediaType) ? "text" : request.MediaType.ToLower();
// //                 string whapiUrl = $"https://gate.whapi.cloud/api/messages/{endpointType}"; 

// //                 // 6. Setup Whapi.Cloud HTTP Client
// //                 using var httpClient = new HttpClient();
// //                 httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {whapiToken}");
// //                 httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

// //                 int successCount = 0;
// //                 int failCount = 0;

// //                 // 7. Loop through each number and dispatch via Whapi
// //                 foreach (var rawPhone in allPhoneNumbers)
// //                 {
// //                     if (string.IsNullOrWhiteSpace(rawPhone)) continue;

// //                     // Clean phone number: Whapi requires country code, NO '+' sign
// //                     string cleanPhone = new string(rawPhone.Where(char.IsDigit).ToArray());
                    
// //                     if (cleanPhone.Length == 10) 
// //                     {
// //                         cleanPhone = "91" + cleanPhone; 
// //                     }

// //                     // Create the Whapi JSON Payload dynamically based on if it has media
// //                     var payload = new Dictionary<string, object>
// //                     {
// //                         { "to", cleanPhone }
// //                     };

// //                     if (endpointType == "text")
// //                     {
// //                         payload.Add("body", request.Message);
// //                     }
// //                     else
// //                     {
// //                         // For image, video, or document
// //                         payload.Add("media", request.MediaBase64);
                        
// //                         if (!string.IsNullOrWhiteSpace(request.Message))
// //                         {
// //                             payload.Add("caption", request.Message);
// //                         }
// //                         if (!string.IsNullOrWhiteSpace(request.FileName))
// //                         {
// //                             payload.Add("file_name", request.FileName);
// //                         }
// //                     }

// //                     string jsonPayload = JsonSerializer.Serialize(payload);
// //                     var content = new StringContent(jsonPayload, Encoding.UTF8);
// //                     content.Headers.ContentType = new MediaTypeHeaderValue("application/json");

// //                     // Execute the POST request to Whapi
// //                     var response = await httpClient.PostAsync(whapiUrl, content);

// //                     if (response.IsSuccessStatusCode)
// //                     {
// //                         successCount++;
// //                     }
// //                     else
// //                     {
// //                         failCount++;
// //                         // Optional: Log response.Content.ReadAsStringAsync() for specific Whapi errors if needed
// //                     }
// //                 }

// //                 // 8. Return Detailed Statistics to Android UI
// //                 return Ok(new { 
// //                     success = true, 
// //                     message = $"Dispatched {successCount} out of {totalAttempted} messages successfully.",
// //                     successCount = successCount,
// //                     failCount = failCount,
// //                     totalAttempted = totalAttempted
// //                 });
// //             }
// //             catch (Exception ex)
// //             {
// //                 return StatusCode(500, new { success = false, message = "Internal Server Error: " + ex.Message });
// //             }
// //         }

// //         [HttpPost("mark-child")]
// //         public async Task<IActionResult> MarkChildAttendance([FromBody] MarkChildAttendanceDto request)
// //         {
// //             try
// //             {
// //                 if (request == null) return BadRequest(new { message = "Invalid payload" });

// //                 var nowIst = GetIstTime();
// //                 var todayIst = nowIst.Date;

// //                 // Validate that the enrollment exists
// //                 var enrollmentExists = await _context.ParentsEnrollments
// //                     .AnyAsync(pe => pe.Id == request.EnrollmentId); // Maps to ParentEnrollment[cite: 1, 12]
// //                 if (!enrollmentExists) return NotFound(new { message = "Child enrollment record not found." });

// //                 // Validate that the batch variation exists[cite: 1]
// //                 var batchExists = await _context.GroupVariations
// //                     .AnyAsync(gv => gv.Id == request.BatchId); // Maps to GroupVariation[cite: 1, 7]
// //                 if (!batchExists) return NotFound(new { message = "Batch variation not found." });

// //                 // Check if an attendance record already exists for this child today[cite: 1]
// //                 var existingAttendance = await _context.ChildAttendances
// //                     .FirstOrDefaultAsync(ca => ca.ChildEnrollmentId == request.EnrollmentId 
// //                                             && ca.GroupVariationId == request.BatchId 
// //                                             && ca.AttendanceDate == todayIst); //[cite: 1]

// //                 if (request.Action.ToUpper() == "ABSENT")
// //                 {
// //                     if (existingAttendance != null)
// //                     {
// //                         // If changing an existing present record to absent, remove or flag it[cite: 1]
// //                         existingAttendance.IsPresent = false; //[cite: 1]
// //                         existingAttendance.CheckInTime = null; //[cite: 1]
// //                         existingAttendance.CheckOutTime = null; //[cite: 1]
// //                     }
// //                     else
// //                     {
// //                         // Create a specific absent entry[cite: 1]
// //                         var absentRecord = new ChildAttendance //[cite: 1]
// //                         {
// //                             ChildEnrollmentId = request.EnrollmentId, //[cite: 1]
// //                             GroupVariationId = request.BatchId, //[cite: 1]
// //                             AttendanceDate = todayIst, //[cite: 1]
// //                             IsPresent = false, //[cite: 1]
// //                             CheckInTime = null, //[cite: 1]
// //                             CheckOutTime = null //[cite: 1]
// //                         };
// //                         _context.ChildAttendances.Add(absentRecord); //[cite: 1]
// //                     }
// //                 }
// //                 else if (request.Action.ToUpper() == "IN")
// //                 {
// //                     if (existingAttendance == null)
// //                     {
// //                         var checkInRecord = new ChildAttendance //[cite: 1]
// //                         {
// //                             ChildEnrollmentId = request.EnrollmentId, //[cite: 1]
// //                             GroupVariationId = request.BatchId, //[cite: 1]
// //                             AttendanceDate = todayIst, //[cite: 1]
// //                             IsPresent = true, //[cite: 1]
// //                             CheckInTime = nowIst.TimeOfDay, // Sets current IST Time[cite: 1]
// //                             CheckOutTime = null //[cite: 1]
// //                         };
// //                         _context.ChildAttendances.Add(checkInRecord); //[cite: 1]
// //                     }
// //                     else
// //                     {
// //                         existingAttendance.IsPresent = true; //[cite: 1]
// //                         existingAttendance.CheckInTime = nowIst.TimeOfDay; // Reset check-in timestamp[cite: 1]
// //                     }
// //                 }
// //                 else if (request.Action.ToUpper() == "OUT")
// //                 {
// //                     if (existingAttendance == null)
// //                     {
// //                         return BadRequest(new { message = "Cannot check-out. Child has not been marked 'IN' today." });
// //                     }
// //                     existingAttendance.IsPresent = true; // Still present for the day[cite: 1]
// //                     existingAttendance.CheckOutTime = nowIst.TimeOfDay; // Mark checkout timestamp in IST[cite: 1]
// //                 }
// //                 else
// //                 {
// //                     return BadRequest(new { message = "Unknown action type. Use 'IN', 'OUT', or 'ABSENT'." });
// //                 }

// //                 await _context.SaveChangesAsync();
// //                 return Ok(new { message = $"Successfully synced status '{request.Action}' to server." });
// //             }
// //             catch (Exception ex)
// //             {
// //                 var actualError = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
// //                 return StatusCode(500, new { message = $"Attendance Sync Error: {actualError}" });
// //             }
// //         }

// //         public class MarkChildAttendanceDto
// //     {
// //         public int EnrollmentId { get; set; }
// //         public int BatchId { get; set; }
// //         public string Action { get; set; } // "IN", "OUT", "ABSENT"
// //     }

// // //         [HttpPost("attendance/mark-child")]
// // // public async Task<IActionResult> MarkChildAttendance([FromBody] MarkChildAttendanceRequest req)
// // // {
// // //     var today = DateTime.UtcNow.Date;
// // //     var timeNow = DateTime.UtcNow.TimeOfDay; // Convert to IST as needed

// // //     var record = await _context.ChildAttendances
// // //         .FirstOrDefaultAsync(a => a.ChildEnrollmentId == req.EnrollmentId && a.AttendanceDate == today);

// // //     if (record == null)
// // //     {
// // //         // First punch of the day (Check In or Absent)
// // //         record = new ChildAttendance
// // //         {
// // //             ChildEnrollmentId = req.EnrollmentId,
// // //             GroupVariationId = req.BatchId,
// // //             AttendanceDate = today,
// // //             IsPresent = req.Action == "IN" ? true : false,
// // //             CheckInTime = req.Action == "IN" ? timeNow : null
// // //         };
// // //         _context.ChildAttendances.Add(record);
// // //     }
// // //     else if (req.Action == "OUT")
// // //     {
// // //         // Second punch (Check out)
// // //         record.CheckOutTime = timeNow;
// // //     }

// // //     await _context.SaveChangesAsync();
// // //     return Ok(new { success = true, message = "Attendance updated" });
// // // }


// // [HttpGet("batch/{batchId}/attendance-history")]
// // public async Task<IActionResult> GetBatchAttendanceHistory(int batchId)
// // {
// //     var batchHistory = await _context.GroupVariations
// //         .Include(gv => gv.ChildAttendances) // 🔥 Automatically joins the ChildAttendances table!
// //         .Where(gv => gv.Id == batchId)
// //         .Select(gv => new 
// //         {
// //             BatchName = $"{gv.AgeGroup} ({gv.Days})",
// //             Time = gv.TimeSlot,
// //             TotalLogs = gv.ChildAttendances.Count(),
// //             AttendanceRecords = gv.ChildAttendances.Select(a => new 
// //             {
// //                 Date = a.AttendanceDate.ToString("yyyy-MM-dd"),
// //                 IsPresent = a.IsPresent,
// //                 CheckIn = a.CheckInTime,
// //                 ChildId = a.ChildEnrollmentId
// //             }).ToList()
// //         })
// //         .FirstOrDefaultAsync();

// //     if (batchHistory == null) return NotFound("Batch not found");

// //     return Ok(batchHistory);
// // }

// // [HttpPost("attendance/finalize-batch")]
// // public async Task<IActionResult> FinalizeBatch([FromBody] FinalizeBatchRequest req)
// // {
// //     var today = DateTime.UtcNow.Date;

// //     // Optional: You can create a "SessionLogs" table in your database to store this permanently.
// //     // For now, we will just simulate a successful save.
    
// //     /* var sessionLog = new SessionLog 
// //     {
// //         GroupVariationId = req.BatchId,
// //         CoachId = req.CoachId,
// //         SessionDate = today,
// //         CoachRemark = req.Remark,
// //         Status = "COMPLETED"
// //     };
// //     _context.SessionLogs.Add(sessionLog);
// //     await _context.SaveChangesAsync();
// //     */

// //     return Ok(new { 
// //         success = true, 
// //         message = "Session finalized and remarks saved successfully." 
// //     });
// // }



        
// //     }

    
// // //     // Data Transfer Object that perfectly matches the Android App's JSON payload
// // //     public class BulkWhatsAppRequest
// // //     {
// // //         public int CounselorId { get; set; }
// // //         public List<int> ParentIds { get; set; }
// // //         public string Message { get; set; }
// // //     }
// // // }
// // }

