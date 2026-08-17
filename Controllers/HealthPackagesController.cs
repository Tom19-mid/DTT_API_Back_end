using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class HealthPackagesController : ControllerBase
{
    private readonly AppDbContext _context;

    public HealthPackagesController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/healthpackages — Lấy toàn bộ gói khám (kèm chi tiết dịch vụ)
    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? gender = null)
    {
        try
        {
            var query = _context.HealthPackages
                .Where(p => p.IsActive)
                .Include(p => p.Details.OrderBy(d => d.SortOrder))
                .AsQueryable();

            // Filter by gender_target: 'male', 'female', or 'all' (all always show)
            if (!string.IsNullOrEmpty(gender) && (gender == "male" || gender == "female"))
            {
                query = query.Where(p => p.GenderTarget == gender || p.GenderTarget == "all");
            }

            var packages = await query
                .OrderByDescending(p => p.BookedCount)
                .Select(p => new HealthPackageResponseDto
                {
                    PackageId = p.PackageId,
                    Title = p.Title,
                    Description = p.Description,
                    Price = p.Price,
                    PriceFormatted = FormatPrice(p.Price),
                    GenderTarget = p.GenderTarget,
                    ImageUrl = p.ImageUrl,
                    BookedCount = p.BookedCount,
                    BookedCountFormatted = FormatBookedCount(p.BookedCount),
                    IsActive = p.IsActive,
                    Details = p.Details.Select(d => d.ServiceName).ToList()
                })
                .ToListAsync();

            return Ok(packages);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error getting health packages: " + ex.Message);
            return StatusCode(500, new { message = "Lỗi tải danh sách gói khám: " + ex.Message });
        }
    }

    // GET /api/healthpackages/{id} — Lấy chi tiết 1 gói khám
    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(int id)
    {
        try
        {
            var pkg = await _context.HealthPackages
                .Where(p => p.PackageId == id && p.IsActive)
                .Include(p => p.Details.OrderBy(d => d.SortOrder))
                .Select(p => new HealthPackageResponseDto
                {
                    PackageId = p.PackageId,
                    Title = p.Title,
                    Description = p.Description,
                    Price = p.Price,
                    PriceFormatted = FormatPrice(p.Price),
                    GenderTarget = p.GenderTarget,
                    ImageUrl = p.ImageUrl,
                    BookedCount = p.BookedCount,
                    BookedCountFormatted = FormatBookedCount(p.BookedCount),
                    IsActive = p.IsActive,
                    Details = p.Details.Select(d => d.ServiceName).ToList()
                })
                .FirstOrDefaultAsync();

            if (pkg == null)
                return NotFound(new { message = "Không tìm thấy gói khám." });

            return Ok(pkg);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi: " + ex.Message });
        }
    }

    // POST /api/healthpackages/{id}/book — Đặt gói khám
    [HttpPost("{id}/book")]
    public async Task<IActionResult> BookPackage(int id, [FromBody] BookPackageRequest req)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, req.PatientId)) return this.ForbidJson();
        try
        {
            var pkg = await _context.HealthPackages.FirstOrDefaultAsync(p => p.PackageId == id && p.IsActive);
            if (pkg == null)
                return NotFound(new { message = "Không tìm thấy gói khám." });

            // Increment booked count
            pkg.BookedCount += 1;
            pkg.UpdatedAt = DateTime.UtcNow;
            try { await _context.SaveChangesAsync(); } catch { }

            // 1. Ensure appointment_statuses table has entries
            var statusCount = await _context.AppointmentStatuses.CountAsync();
            if (statusCount == 0)
            {
                _context.AppointmentStatuses.AddRange(
                    new AppointmentStatus { StatusId = 1, StatusName = "Scheduled" },
                    new AppointmentStatus { StatusId = 2, StatusName = "Waiting" },
                    new AppointmentStatus { StatusId = 3, StatusName = "InProgress" },
                    new AppointmentStatus { StatusId = 4, StatusName = "Completed" },
                    new AppointmentStatus { StatusId = 5, StatusName = "Cancelled" },
                    new AppointmentStatus { StatusId = 6, StatusName = "NoShow" },
                    new AppointmentStatus { StatusId = 7, StatusName = "CheckedIn" },
                    new AppointmentStatus { StatusId = 8, StatusName = "WaitingForDoctor" }
                );
                try { await _context.SaveChangesAsync(); } catch { }
            }

            // 2. Validate patient exists in PostgreSQL
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == req.PatientId);
            if (patient == null)
                return BadRequest(new { message = "Không tìm thấy bệnh nhân." });
            int validPatientId = patient.PatientId;

            // 3. Obtain target specialty & doctor matching the health package domain
            int targetSpecialtyId = 1;
            string titleLower = pkg.Title.ToLower();
            if (titleLower.Contains("tim mạch")) targetSpecialtyId = 5;
            else if (titleLower.Contains("xương khớp") || titleLower.Contains("cột sống")) targetSpecialtyId = 4;
            else if (titleLower.Contains("sản") || titleLower.Contains("phụ khoa")) targetSpecialtyId = 3;
            else if (titleLower.Contains("nhi") || titleLower.Contains("trẻ em")) targetSpecialtyId = 2;
            else if (titleLower.Contains("thần kinh")) targetSpecialtyId = 6;
            else if (titleLower.Contains("da liễu")) targetSpecialtyId = 7;

            var targetSpecObj = await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == targetSpecialtyId);
            string specName = targetSpecObj?.SpecialtyName ?? "Nội tổng quát";

            var matchedDoctor = await _context.Doctors.FirstOrDefaultAsync(d => d.SpecialtyId == targetSpecialtyId && d.Status == "Active")
                              ?? await _context.Doctors.FirstOrDefaultAsync(d => d.SpecialtyId == targetSpecialtyId)
                              ?? await _context.Doctors.FirstOrDefaultAsync(d => d.Status == "Active")
                              ?? await _context.Doctors.FirstOrDefaultAsync();
            if (matchedDoctor == null)
                return BadRequest(new { message = "Không tìm thấy bác sĩ phù hợp để xếp lịch." });
            int validDoctorId = matchedDoctor.DoctorId;

            // 4. Safely get a valid, unbooked slot_id from doctor_schedule_slots table
            int validSlotId = 0;
            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync();

                using var slotCmd = conn.CreateCommand();
                slotCmd.CommandText = "SELECT s.slot_id FROM doctor_schedule_slots s JOIN doctor_schedules ds ON s.schedule_id = ds.schedule_id WHERE ds.doctor_id = " + validDoctorId + " AND s.status = 'Available' AND s.slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL) LIMIT 1";
                var val = await slotCmd.ExecuteScalarAsync();
                if (val != null && val != DBNull.Value) validSlotId = Convert.ToInt32(val);
                if (validSlotId == 0)
                {
                    using var anySlot = conn.CreateCommand();
                    anySlot.CommandText = "SELECT slot_id FROM doctor_schedule_slots WHERE status = 'Available' AND slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL) LIMIT 1";
                    var val2 = await anySlot.ExecuteScalarAsync();
                    if (val2 != null && val2 != DBNull.Value) validSlotId = Convert.ToInt32(val2);
                }
                if (validSlotId == 0)
                {
                    using var schedCmd = conn.CreateCommand();
                    schedCmd.CommandText = $"INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time) VALUES ({validDoctorId}, CURRENT_DATE, '08:00:00', '17:00:00') RETURNING schedule_id";
                    var scRes = await schedCmd.ExecuteScalarAsync();
                    int scId = scRes != null && scRes != DBNull.Value ? Convert.ToInt32(scRes) : 1;

                    using var insSlot = conn.CreateCommand();
                    insSlot.CommandText = $"INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status) VALUES ({scId}, 1, '08:00:00', '09:00:00', 'Available') RETURNING slot_id";
                    var slRes = await insSlot.ExecuteScalarAsync();
                    if (slRes != null && slRes != DBNull.Value) validSlotId = Convert.ToInt32(slRes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Slot lookup warning in package booking: " + ex.Message);
            }
            if (validSlotId == 0) validSlotId = 1;

            // 5. Create appointment record for package booking with EF Save & Raw SQL fallback
            int queueNum = (await _context.Appointments.CountAsync(a => a.PatientId == validPatientId)) + 1;
            int newAppointmentId = 0;

            // Xác định ngày hẹn khám thực tế (mặc định ngày mai nếu không có PreferredDate) và LƯU vào
            // cột appointment_date — trước đây chỉ nhúng vào chuỗi Reason (text tự do), khiến màn
            // "Tiếp Đón & Check-in hôm nay" (lọc theo appointment_date) không nhận diện được, nên các
            // ca đặt gói khám cho ngày mai vẫn bị hiện nhầm vào danh sách hôm nay (fallback theo created_at).
            DateOnly apptDateForPackage;
            if (!string.IsNullOrEmpty(req.PreferredDate) &&
                DateOnly.TryParseExact(req.PreferredDate, new[] { "d/M/yyyy", "dd/MM/yyyy", "yyyy-MM-dd", "M/d/yyyy" },
                    System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsedPkgDate))
            {
                apptDateForPackage = parsedPkgDate;
            }
            else
            {
                apptDateForPackage = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7).AddDays(1)); // mặc định: ngày mai (giờ VN)
            }
            string preferredDateStr = apptDateForPackage.ToString("dd/M/yyyy");

            string bookingReason = $"{specName} - Gói: {pkg.Title} - {preferredDateStr}";
            try
            {
                var appointment = new Appointment
                {
                    PatientId = validPatientId,
                    DoctorId = validDoctorId,
                    SlotId = validSlotId,
                    Reason = bookingReason,
                    Note = $"Gói khám: {pkg.Title} | {FormatPrice(pkg.Price)} | Bệnh nhân: {req.PatientName}",
                    StatusId = 1, // Confirmed
                    QueueNumber = queueNum,
                    AppointmentDate = apptDateForPackage,
                    CreatedAt = DateTime.UtcNow
                };

                _context.Appointments.Add(appointment);
                await _context.SaveChangesAsync();
                newAppointmentId = appointment.AppointmentId;
            }
            catch (Exception efEx)
            {
                Console.WriteLine("EF Save failed for package booking, executing raw SQL insert: " + efEx.Message);
                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync();

                using var rawCmd = conn.CreateCommand();
                rawCmd.CommandText = @"
                    INSERT INTO appointments (patient_id, doctor_id, slot_id, reason, status_id, queue_number, note, appointment_date, created_at)
                    VALUES (@pId, @dId, @sId, @reason, 1, @qNum, @note, @apptDate, NOW())
                    RETURNING appointment_id";
                var p1 = rawCmd.CreateParameter(); p1.ParameterName = "@pId"; p1.Value = validPatientId; rawCmd.Parameters.Add(p1);
                var p2 = rawCmd.CreateParameter(); p2.ParameterName = "@dId"; p2.Value = validDoctorId; rawCmd.Parameters.Add(p2);
                var p3 = rawCmd.CreateParameter(); p3.ParameterName = "@sId"; p3.Value = validSlotId; rawCmd.Parameters.Add(p3);
                var p4 = rawCmd.CreateParameter(); p4.ParameterName = "@reason"; p4.Value = bookingReason; rawCmd.Parameters.Add(p4);
                var p5 = rawCmd.CreateParameter(); p5.ParameterName = "@qNum"; p5.Value = queueNum; rawCmd.Parameters.Add(p5);
                var p6 = rawCmd.CreateParameter(); p6.ParameterName = "@note"; p6.Value = $"Gói khám: {pkg.Title} | {FormatPrice(pkg.Price)} | Bệnh nhân: {req.PatientName}"; rawCmd.Parameters.Add(p6);
                var p7 = rawCmd.CreateParameter(); p7.ParameterName = "@apptDate"; p7.Value = apptDateForPackage; rawCmd.Parameters.Add(p7);

                var inserted = await rawCmd.ExecuteScalarAsync();
                if (inserted != null && inserted != DBNull.Value) newAppointmentId = Convert.ToInt32(inserted);
            }

            // Gửi thông báo xác nhận đặt gói khám lên App Mobile — trước đây thiếu, bệnh nhân chỉ thấy
            // xác nhận tức thời trên màn hình lúc đặt, không có lịch sử nếu thoát app trước khi xem kỹ.
            var pkgPatient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == validPatientId);
            if (pkgPatient != null && pkgPatient.UserId != Guid.Empty)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = pkgPatient.UserId,
                    Title = "✅ Đặt Gói Khám Sức Khỏe Thành Công",
                    Content = $"Bạn đã đặt thành công gói khám '{pkg.Title}' (giá {FormatPrice(pkg.Price)}), dự kiến khám ngày {apptDateForPackage:dd/MM/yyyy}. Số thứ tự dự kiến: {queueNum}.\n\nVui lòng đến bệnh viện đúng ngày hẹn để làm thủ tục tiếp đón.",
                    Type = "appointment",
                    RelatedId = newAppointmentId > 0 ? newAppointmentId : (int?)null,
                    RelatedType = "appointment",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                try { await _context.SaveChangesAsync(); } catch { }
            }

            return Ok(new
            {
                success = true,
                message = $"Đã đặt gói khám '{pkg.Title}' thành công!",
                appointmentId = newAppointmentId > 0 ? newAppointmentId : queueNum,
                packageTitle = pkg.Title,
                priceFormatted = FormatPrice(pkg.Price),
                preferredDate = apptDateForPackage.ToString("dd/MM/yyyy"),
                queueNumber = queueNum
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error booking package: " + ex.Message);
            return StatusCode(500, new { message = "Lỗi đặt gói khám: " + ex.Message });
        }
    }

    // Helper: Format price as Vietnamese currency string
    private static string FormatPrice(decimal price)
    {
        return price.ToString("N0").Replace(",", ".") + "đ";
    }

    // Helper: Format booked count with k+ notation
    private static string FormatBookedCount(int count)
    {
        if (count >= 1000)
            return $"{count / 1000}.{(count % 1000) / 100}k+";
        return $"{count}+";
    }
}

// ── DTOs ─────────────────────────────────────────────────────────────────────

public class HealthPackageResponseDto
{
    public int PackageId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string? Description { get; set; }
    public decimal Price { get; set; }
    public string PriceFormatted { get; set; } = string.Empty;
    public string GenderTarget { get; set; } = "all";
    public string? ImageUrl { get; set; }
    public int BookedCount { get; set; }
    public string BookedCountFormatted { get; set; } = string.Empty;
    public bool IsActive { get; set; }
    public List<string> Details { get; set; } = new();
}

public class BookPackageRequest
{
    public int PatientId { get; set; }
    public string PatientName { get; set; } = string.Empty;
    public string? PreferredDate { get; set; }
    public string? PriceFormatted { get; set; }
}
