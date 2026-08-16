using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;
using System.ComponentModel.DataAnnotations;
using System.Data;

namespace DTT_Backend_API.Controllers;

public class UpdateLeaveStatusDto
{
    [Required(ErrorMessage = "Trạng thái mới là bắt buộc.")]
    public string Status { get; set; } = string.Empty;
}

public class CreateDoctorLeaveDto
{
    [Required(ErrorMessage = "DoctorId là bắt buộc.")]
    public int DoctorId { get; set; }

    [Required(ErrorMessage = "Ngày bắt đầu nghỉ là bắt buộc.")]
    public string LeaveStartDate { get; set; } = string.Empty;

    public string? LeaveEndDate { get; set; }

    public string? Reason { get; set; }

    public string Status { get; set; } = "Pending";
}

[ApiController]
[Route("api/doctors/leaves")]
public class DoctorLeavesController : ControllerBase
{
    private readonly AppDbContext _context;

    public DoctorLeavesController(AppDbContext context)
    {
        _context = context;
    }

    // Helper chuyển đổi status sang tiếng Việt
    private static string FormatStatusToVi(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Chờ duyệt";
        var s = rawStatus.Trim();
        if (s == "Pending" || s == "Chờ duyệt") return "Chờ duyệt";
        if (s == "Approved" || s == "Đã duyệt") return "Đã duyệt";
        if (s == "Rejected" || s == "Từ chối") return "Từ chối";
        if (s == "Cancelled" || s == "Đã hủy") return "Đã hủy";
        return "Chờ duyệt";
    }

    // Helper chuyển đổi status từ tiếng Việt sang mã DB chuẩn ('Pending', 'Approved', 'Rejected', 'Cancelled')
    private static string NormalizeDbLeaveStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Pending";
        var s = rawStatus.Trim();
        if (s == "Chờ duyệt" || s.Equals("pending", StringComparison.OrdinalIgnoreCase)) return "Pending";
        if (s == "Đã duyệt" || s.Equals("approved", StringComparison.OrdinalIgnoreCase)) return "Approved";
        if (s == "Từ chối" || s.Equals("rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        if (s == "Đã hủy" || s.Equals("cancelled", StringComparison.OrdinalIgnoreCase)) return "Cancelled";
        return "Pending";
    }

    private static DateOnly ParseDateOnly(string? dateStr)
    {
        if (string.IsNullOrWhiteSpace(dateStr)) return DateOnly.FromDateTime(DateTime.Today);
        var s = dateStr.Trim();
        string[] formats = new[] { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd" };
        if (DateOnly.TryParseExact(s, formats, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var parsed))
        {
            return parsed;
        }
        return DateOnly.FromDateTime(DateTime.Today);
    }

    // GET /api/doctors/leaves — Lấy danh sách tất cả đơn xin nghỉ phép
    [HttpGet]
    public async Task<IActionResult> GetLeaves([FromQuery] string? status)
    {
        try
        {
            var leavesQuery = _context.DoctorLeaves.AsNoTracking().AsQueryable();

            if (!string.IsNullOrWhiteSpace(status) && !status.Equals("Tất cả", StringComparison.OrdinalIgnoreCase) && !status.Equals("All", StringComparison.OrdinalIgnoreCase))
            {
                string normSearchStatus = NormalizeDbLeaveStatus(status);
                leavesQuery = leavesQuery.Where(l => l.Status == normSearchStatus);
            }

            var leaves = await leavesQuery.OrderByDescending(l => l.CreatedAt).ToListAsync();

            var doctorIds = leaves.Select(l => l.DoctorId).Distinct().ToList();
            var doctorsDict = await _context.Doctors.AsNoTracking().Where(d => doctorIds.Contains(d.DoctorId)).ToDictionaryAsync(d => d.DoctorId);

            var userIds = doctorsDict.Values.Select(d => d.UserId).Distinct().ToList();
            var usersDict = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId);

            var specialtyIds = doctorsDict.Values.Where(d => d.SpecialtyId.HasValue).Select(d => d.SpecialtyId!.Value).Distinct().ToList();
            var specialtiesDict = await _context.Specialties.AsNoTracking().Where(s => specialtyIds.Contains(s.SpecialtyId)).ToDictionaryAsync(s => s.SpecialtyId, s => s.SpecialtyName);

            var result = leaves.Select(l =>
            {
                doctorsDict.TryGetValue(l.DoctorId, out var doctor);
                User? user = null;
                if (doctor != null) usersDict.TryGetValue(doctor.UserId, out user);

                string specName = "Nội tổng quát";
                if (doctor?.SpecialtyId != null && specialtiesDict.TryGetValue(doctor.SpecialtyId.Value, out var sName))
                {
                    specName = sName;
                }

                return new
                {
                    l.LeaveId,
                    id = l.LeaveId,
                    l.DoctorId,
                    DoctorName = doctor?.FullName ?? user?.FullName ?? "Bác sĩ DTT",
                    SpecialtyName = specName,
                    Phone = user?.PhoneNumber ?? "Chưa cập nhật",
                    LeaveStartDate = l.LeaveStartDate.ToString("dd/MM/yyyy"),
                    LeaveEndDate = l.LeaveEndDate.ToString("dd/MM/yyyy"),
                    Reason = l.Reason ?? "Không có lý do",
                    Status = FormatStatusToVi(l.Status),
                    RawStatus = l.Status,
                    CreatedAt = l.CreatedAt,
                    ApprovedAt = l.ApprovedAt,
                    ApprovedBy = l.ApprovedBy
                };
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy danh sách đơn nghỉ phép.", error = ex.Message });
        }
    }

    // PUT /api/doctors/leaves/{id}/status — Duyệt/Từ chối đơn xin nghỉ phép
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateLeaveStatus(int id, [FromBody] UpdateLeaveStatusDto dto)
    {
        try
        {
            var leave = await _context.DoctorLeaves.FirstOrDefaultAsync(l => l.LeaveId == id);
            if (leave == null)
            {
                return NotFound(new { message = "Không tìm thấy đơn xin nghỉ phép." });
            }

            string newDbStatus = NormalizeDbLeaveStatus(dto.Status);
            leave.Status = newDbStatus;
            leave.UpdatedAt = DateTime.UtcNow;

            // Luôn cập nhật thời gian approved_at khi duyệt hoặc từ chối đơn nghỉ phép
            leave.ApprovedAt = DateTime.UtcNow;

            try
            {
                var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == leave.DoctorId);
                if (doctor != null)
                {
                    var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == doctor.UserId);

                    if (newDbStatus == "Approved")
                    {
                        // Đã duyệt đơn nghỉ phép -> Đổi trạng thái Bác sĩ sang "OnLeave"
                        doctor.Status = "OnLeave";
                        if (user != null) user.Status = "Inactive";

                        // Giữ nguyên lịch trong CSDL, chỉ cập nhật trạng thái lịch làm việc (doctor_schedules/slots) sang 'Off'
                        try
                        {
                            var conn = _context.Database.GetDbConnection();
                            if (conn.State != ConnectionState.Open) await conn.OpenAsync();
                            using var updCmd = conn.CreateCommand();
                            updCmd.CommandText = @"
                                UPDATE doctor_schedule_slots 
                                SET status = 'Off'
                                WHERE schedule_id IN (
                                    SELECT schedule_id FROM doctor_schedules 
                                    WHERE doctor_id = @docId AND work_date BETWEEN @sDate AND @eDate
                                );
                                UPDATE doctor_schedules 
                                SET status = 'Off'
                                WHERE doctor_id = @docId AND work_date BETWEEN @sDate AND @eDate;
                            ";
                            var p1 = updCmd.CreateParameter(); p1.ParameterName = "@docId"; p1.Value = leave.DoctorId; updCmd.Parameters.Add(p1);
                            var p2 = updCmd.CreateParameter(); p2.ParameterName = "@sDate"; p2.Value = leave.LeaveStartDate.ToDateTime(TimeOnly.MinValue); updCmd.Parameters.Add(p2);
                            var p3 = updCmd.CreateParameter(); p3.ParameterName = "@eDate"; p3.Value = leave.LeaveEndDate.ToDateTime(TimeOnly.MinValue); updCmd.Parameters.Add(p3);
                            await updCmd.ExecuteNonQueryAsync();
                        }
                        catch (Exception schedEx)
                        {
                            Console.WriteLine($"[UpdateLeaveStatus Warning] Không thể cập nhật status doctor_schedules: {schedEx.Message}");
                        }
                    }
                    else if (newDbStatus == "Rejected" || newDbStatus == "Cancelled")
                    {
                        // Từ chối hoặc Hủy đơn -> Khôi phục trạng thái Bác sĩ sang "Active", mở lại lịch làm việc
                        doctor.Status = "Active";
                        if (user != null) user.Status = "Active";

                        try
                        {
                            var conn = _context.Database.GetDbConnection();
                            if (conn.State != ConnectionState.Open) await conn.OpenAsync();
                            using var updCmd = conn.CreateCommand();
                            updCmd.CommandText = @"
                                UPDATE doctor_schedule_slots 
                                SET status = 'Available'
                                WHERE schedule_id IN (
                                    SELECT schedule_id FROM doctor_schedules 
                                    WHERE doctor_id = @docId AND work_date BETWEEN @sDate AND @eDate
                                );
                                UPDATE doctor_schedules 
                                SET status = 'Available'
                                WHERE doctor_id = @docId AND work_date BETWEEN @sDate AND @eDate;
                            ";
                            var p1 = updCmd.CreateParameter(); p1.ParameterName = "@docId"; p1.Value = leave.DoctorId; updCmd.Parameters.Add(p1);
                            var p2 = updCmd.CreateParameter(); p2.ParameterName = "@sDate"; p2.Value = leave.LeaveStartDate.ToDateTime(TimeOnly.MinValue); updCmd.Parameters.Add(p2);
                            var p3 = updCmd.CreateParameter(); p3.ParameterName = "@eDate"; p3.Value = leave.LeaveEndDate.ToDateTime(TimeOnly.MinValue); updCmd.Parameters.Add(p3);
                            await updCmd.ExecuteNonQueryAsync();
                        }
                        catch (Exception schedEx)
                        {
                            Console.WriteLine($"[UpdateLeaveStatus Warning] Không thể khôi phục doctor_schedules: {schedEx.Message}");
                        }
                    }
                }
            }
            catch (Exception docEx)
            {
                Console.WriteLine($"[UpdateLeaveStatus Warning] Không thể cập nhật trạng thái doctor/user: {docEx.Message}");
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã cập nhật trạng thái đơn nghỉ phép sang {FormatStatusToVi(newDbStatus)}!",
                leaveId = leave.LeaveId,
                status = FormatStatusToVi(newDbStatus)
            });
        }
        catch (Exception ex)
        {
            var detail = ex.InnerException?.Message ?? ex.Message;
            Console.WriteLine($"[UpdateLeaveStatus Error]: {detail} | {ex.StackTrace}");
            return StatusCode(500, new { message = $"Lỗi khi cập nhật trạng thái đơn nghỉ phép: {detail}", error = detail });
        }
    }

    // POST /api/doctors/leaves — Tạo mới đơn nghỉ phép
    [HttpPost]
    public async Task<IActionResult> CreateLeave([FromBody] CreateDoctorLeaveDto dto)
    {
        try
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == dto.DoctorId);
            if (doctor == null)
            {
                return BadRequest(new { message = "DoctorId không tồn tại." });
            }

            var startDate = ParseDateOnly(dto.LeaveStartDate);
            var endDate = ParseDateOnly(dto.LeaveEndDate ?? dto.LeaveStartDate);

            // Mặc định đơn mới luôn ở trạng thái 'Pending' (Chờ duyệt) trừ khi chỉ định rõ 'Approved'
            string dbStatus = "Pending";
            if (!string.IsNullOrWhiteSpace(dto.Status) && dto.Status.Equals("Approved", StringComparison.OrdinalIgnoreCase))
            {
                dbStatus = "Approved";
            }

            var newLeave = new DoctorLeave
            {
                DoctorId = dto.DoctorId,
                LeaveStartDate = startDate,
                LeaveEndDate = endDate,
                Reason = dto.Reason,
                Status = dbStatus,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (dbStatus == "Approved")
            {
                newLeave.ApprovedAt = DateTime.UtcNow;
                doctor.Status = "OnLeave";
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == doctor.UserId);
                if (user != null) user.Status = "Inactive";
            }

            _context.DoctorLeaves.Add(newLeave);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Tạo đơn xin nghỉ phép thành công!",
                leave = newLeave
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi tạo đơn xin nghỉ phép.", error = ex.Message });
        }
    }
}
