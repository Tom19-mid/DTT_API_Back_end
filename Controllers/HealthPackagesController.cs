using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
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
        try
        {
            var pkg = await _context.HealthPackages.FirstOrDefaultAsync(p => p.PackageId == id && p.IsActive);
            if (pkg == null)
                return NotFound(new { message = "Không tìm thấy gói khám." });

            // Increment booked count
            pkg.BookedCount += 1;
            pkg.UpdatedAt = DateTime.UtcNow;

            // Ensure dedicated Health Package record exists in SQL to avoid associating with regular doctors
            int validDoctorId = 0;
            int validSlotId = 0;
            try
            {
                var conn = _context.Database.GetDbConnection();
                if (conn.State != ConnectionState.Open) await conn.OpenAsync();

                using (var findCmd = conn.CreateCommand())
                {
                    findCmd.CommandText = "SELECT doctor_id FROM doctors WHERE full_name = 'Gói Khám Sức Khỏe' LIMIT 1";
                    var dVal = await findCmd.ExecuteScalarAsync();
                    if (dVal != null && dVal != DBNull.Value) validDoctorId = Convert.ToInt32(dVal);
                }

                if (validDoctorId == 0)
                {
                    int specId = 1;
                    using (var specCmd = conn.CreateCommand())
                    {
                        specCmd.CommandText = "INSERT INTO specialties (specialty_name, description, icon, is_active) VALUES ('Gói Khám Sức Khỏe', 'Khu Tiếp Nhận & Khám theo gói', 'medkit', TRUE) ON CONFLICT (specialty_name) DO UPDATE SET is_active = TRUE RETURNING specialty_id";
                        try { var sVal = await specCmd.ExecuteScalarAsync(); if (sVal != null) specId = Convert.ToInt32(sVal); } catch { }
                    }

                    using (var insDoc = conn.CreateCommand())
                    {
                        insDoc.CommandText = $"INSERT INTO doctors (user_id, specialty_id, full_name, title, experience_years, bio, clinic_room, price, status) VALUES (gen_random_uuid(), {specId}, 'Gói Khám Sức Khỏe', '', 0, 'Khu khám chuyên biệt theo chuỗi dịch vụ', '', 0, 'Active') RETURNING doctor_id";
                        try { var newDoc = await insDoc.ExecuteScalarAsync(); if (newDoc != null) validDoctorId = Convert.ToInt32(newDoc); } catch { }
                    }
                }

                if (validDoctorId > 0)
                {
                    using (var findSlot = conn.CreateCommand())
                    {
                        findSlot.CommandText = @"
                            SELECT s.slot_id 
                            FROM doctor_schedule_slots s
                            JOIN doctor_schedules ds ON s.schedule_id = ds.schedule_id
                            WHERE ds.doctor_id = " + validDoctorId + " LIMIT 1";
                        var sVal = await findSlot.ExecuteScalarAsync();
                        if (sVal != null && sVal != DBNull.Value) validSlotId = Convert.ToInt32(sVal);
                    }

                    if (validSlotId == 0)
                    {
                        int scheduleId = 0;
                        using (var insSched = conn.CreateCommand())
                        {
                            insSched.CommandText = $"INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time, status) VALUES ({validDoctorId}, CURRENT_DATE, '07:00:00'::time, '17:00:00'::time, 'Available') RETURNING schedule_id";
                            try { var sc = await insSched.ExecuteScalarAsync(); if (sc != null) scheduleId = Convert.ToInt32(sc); } catch { }
                        }
                        if (scheduleId > 0)
                        {
                            using (var insSlot = conn.CreateCommand())
                            {
                                insSlot.CommandText = $"INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status) VALUES ({scheduleId}, 1, '07:30:00'::time, '11:30:00'::time, 'Available') RETURNING slot_id";
                                try { var st = await insSlot.ExecuteScalarAsync(); if (st != null) validSlotId = Convert.ToInt32(st); } catch { }
                            }
                        }
                    }
                }

                // Fallback if anything failed
                if (validDoctorId == 0 || validSlotId == 0)
                {
                    using var cmd = conn.CreateCommand();
                    cmd.CommandText = "SELECT ds.doctor_id, s.slot_id FROM doctor_schedule_slots s JOIN doctor_schedules ds ON s.schedule_id = ds.schedule_id LIMIT 1";
                    using var reader = await cmd.ExecuteReaderAsync();
                    if (await reader.ReadAsync())
                    {
                        if (validDoctorId == 0) validDoctorId = Convert.ToInt32(reader["doctor_id"]);
                        if (validSlotId == 0) validSlotId = Convert.ToInt32(reader["slot_id"]);
                    }
                    await reader.CloseAsync();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Schedule lookup warning in package booking: " + ex.Message);
            }

            // Create appointment record for package booking
            var appointment = new Appointment
            {
                PatientId = req.PatientId > 0 ? req.PatientId : 2,
                DoctorId = validDoctorId,
                SlotId = validSlotId,
                Reason = $"{pkg.Title} - {req.PreferredDate ?? DateTime.UtcNow.AddDays(1).ToString("dd/M/yyyy")}",
                Note = $"Gói khám: {pkg.Title} | {FormatPrice(pkg.Price)} | Bệnh nhân: {req.PatientName}",
                StatusId = 1, // Confirmed
                QueueNumber = (await _context.Appointments.CountAsync()) + 1,
                CreatedAt = DateTime.UtcNow
            };

            _context.Appointments.Add(appointment);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã đặt gói khám '{pkg.Title}' thành công!",
                appointmentId = appointment.AppointmentId,
                packageTitle = pkg.Title,
                priceFormatted = FormatPrice(pkg.Price),
                preferredDate = req.PreferredDate ?? DateTime.UtcNow.AddDays(1).ToString("dd/MM/yyyy"),
                queueNumber = appointment.QueueNumber
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
