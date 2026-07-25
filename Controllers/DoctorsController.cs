using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class DoctorsController : ControllerBase
{
    private readonly AppDbContext _context;

    public DoctorsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> GetDoctors([FromQuery] int? specialtyId)
    {
        // 1. Ensure Specialties table is seeded with all 8 standard hospital specialties
        var specCount = await _context.Specialties.CountAsync();
        if (specCount < 8)
        {
            var defaults = new[]
            {
                new Specialty { SpecialtyId = 1, SpecialtyName = "Nội tổng quát", Description = "Khám bệnh nội khoa chung", Status = true },
                new Specialty { SpecialtyId = 2, SpecialtyName = "Nhi khoa", Description = "Chăm sóc sức khỏe trẻ em", Status = true },
                new Specialty { SpecialtyId = 3, SpecialtyName = "Sản phụ khoa", Description = "Khám thai và bệnh phụ khoa", Status = true },
                new Specialty { SpecialtyId = 4, SpecialtyName = "Cơ xương khớp", Description = "Điều trị bệnh cơ xương khớp", Status = true },
                new Specialty { SpecialtyId = 5, SpecialtyName = "Tim mạch", Description = "Khám và điều trị bệnh tim mạch", Status = true },
                new Specialty { SpecialtyId = 6, SpecialtyName = "Thần kinh", Description = "Tầm soát bệnh lý thần kinh", Status = true },
                new Specialty { SpecialtyId = 7, SpecialtyName = "Da liễu", Description = "Khám và điều trị bệnh da liễu", Status = true },
                new Specialty { SpecialtyId = 8, SpecialtyName = "Chẩn đoán hình ảnh", Description = "Siêu âm, X-quang, chụp CT scanner", Status = true }
            };
            foreach (var sp in defaults)
            {
                if (!await _context.Specialties.AnyAsync(s => s.SpecialtyId == sp.SpecialtyId))
                {
                    _context.Specialties.Add(sp);
                }
            }
            await _context.SaveChangesAsync();
        }

        // 2. Ensure Roles table has entries
        var roleCount = await _context.Roles.CountAsync();
        if (roleCount == 0)
        {
            _context.Roles.AddRange(
                new Role { RoleId = 1, RoleName = "Patient", Description = "Bệnh nhân" },
                new Role { RoleId = 2, RoleName = "Doctor", Description = "Bác sĩ" },
                new Role { RoleId = 3, RoleName = "Admin", Description = "Quản trị viên" }
            );
            await _context.SaveChangesAsync();
        }

        // 3. Seed 10 realistic specialist doctors covering all 8 specialties
        var doctorMasterList = new[]
        {
            new { Name = "BS. CKII Nguyễn Văn A", Degree = "Chuyên khoa II Nội tổng quát", Exp = 15, Room = "Phòng 101", Rating = 4.9m, Reviews = 145, Phone = "0901111111", Email = "doctor1@gmail.com", SpecId = 1, WorkingDays = "Thứ Hai, Tư, Sáu & Chủ Nhật" },
            new { Name = "BS. CKI Lê Thị B", Degree = "Bác sĩ Chuyên khoa I Nhi", Exp = 12, Room = "Phòng 102", Rating = 5.0m, Reviews = 132, Phone = "0902222222", Email = "doctor2@gmail.com", SpecId = 2, WorkingDays = "Thứ Ba, Năm, Bảy" },
            new { Name = "ThS. BS Trần Văn C", Degree = "Thạc sĩ Chuyên môn Tim mạch", Exp = 14, Room = "Phòng 201", Rating = 4.8m, Reviews = 160, Phone = "0903333333", Email = "doctor3@gmail.com", SpecId = 5, WorkingDays = "Thứ Hai, Ba, Năm, Sáu" },
            new { Name = "BS. CKI Phạm Thị D", Degree = "Bác sĩ Chuyên khoa Da liễu", Exp = 9, Room = "Phòng 202", Rating = 4.9m, Reviews = 90, Phone = "0904444444", Email = "doctor4@gmail.com", SpecId = 7, WorkingDays = "Thứ Tư, Sáu, Bảy & Chủ Nhật" },
            new { Name = "TS. BS Đỗ Phương Hạnh", Degree = "Tiến sĩ Chuyên môn Phụ & Sản khoa", Exp = 18, Room = "Phòng 301", Rating = 5.0m, Reviews = 210, Phone = "0905555555", Email = "doctor5@gmail.com", SpecId = 3, WorkingDays = "Thứ Hai, Tư, Năm, Bảy" },
            new { Name = "BS. CKII Phạm Tuấn Kiệt", Degree = "Chuyên khoa II Cơ xương khớp", Exp = 16, Room = "Phòng 302", Rating = 4.9m, Reviews = 175, Phone = "0906666666", Email = "doctor6@gmail.com", SpecId = 4, WorkingDays = "Thứ Ba, Tư, Sáu, Chủ Nhật" },
            new { Name = "ThS. BS Vũ Bích Ngọc", Degree = "Thạc sĩ Bác sĩ Thần kinh", Exp = 11, Room = "Phòng 401", Rating = 4.8m, Reviews = 115, Phone = "0907777777", Email = "doctor7@gmail.com", SpecId = 6, WorkingDays = "Thứ Hai, Ba, Sáu, Bảy" },
            new { Name = "BS. CKI Hoàng Văn Long", Degree = "Chuyên khoa Chẩn đoán hình ảnh", Exp = 10, Room = "Phòng 402", Rating = 4.9m, Reviews = 104, Phone = "0908888888", Email = "doctor8@gmail.com", SpecId = 8, WorkingDays = "Thứ Hai đến Thứ Sáu" },
            new { Name = "BS. CKII Trịnh Hoàng Minh", Degree = "Bác sĩ Cố vấn Nội tổng quát", Exp = 20, Room = "Phòng 103", Rating = 5.0m, Reviews = 250, Phone = "0909999999", Email = "doctor9@gmail.com", SpecId = 1, WorkingDays = "Thứ Ba, Năm, Bảy & Chủ Nhật" },
            new { Name = "ThS. BS Nguyễn Mai Chi", Degree = "Thạc sĩ Chuyên khoa Nhi", Exp = 8, Room = "Phòng 104", Rating = 4.9m, Reviews = 88, Phone = "0910000000", Email = "doctor10@gmail.com", SpecId = 2, WorkingDays = "Thứ Hai, Tư, Sáu, Bảy" }
        };

        foreach (var doc in doctorMasterList)
        {
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == doc.Phone || u.Email == doc.Email);
            User user;
            if (existingUser == null)
            {
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    PhoneNumber = doc.Phone,
                    Email = doc.Email,
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("Doctor@123"),
                    RoleId = 2,
                    Status = "Active"
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }
            else
            {
                user = existingUser;
            }

            var existingDoc = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.UserId || (d.FullName != null && d.FullName.Contains(doc.Name.Substring(doc.Name.LastIndexOf(' ') + 1))));
            if (existingDoc == null)
            {
                var newDoc = new Doctor
                {
                    UserId = user.UserId,
                    FullName = doc.Name,
                    Degree = doc.Degree,
                    ExperienceYears = doc.Exp,
                    ClinicRoom = doc.Room,
                    Rating = doc.Rating,
                    ReviewCount = doc.Reviews,
                    SpecialtyId = doc.SpecId,
                    Status = "Active"
                };
                _context.Doctors.Add(newDoc);
                await _context.SaveChangesAsync();
            }
            else
            {
                // Sync properties for realism and specialty alignment
                bool needsSave = false;
                if (existingDoc.SpecialtyId != doc.SpecId) { existingDoc.SpecialtyId = doc.SpecId; needsSave = true; }
                if (existingDoc.FullName != doc.Name) { existingDoc.FullName = doc.Name; needsSave = true; }
                if (existingDoc.Degree != doc.Degree) { existingDoc.Degree = doc.Degree; needsSave = true; }
                if (existingDoc.ClinicRoom != doc.Room) { existingDoc.ClinicRoom = doc.Room; needsSave = true; }
                if (needsSave) await _context.SaveChangesAsync();
            }
        }

        // Auto-seed doctor working schedule shifts for 14 days
        await SeedDoctorSchedulesAsync();

        var query = _context.Doctors.AsQueryable();
        if (specialtyId.HasValue && specialtyId.Value > 0)
        {
            query = query.Where(d => d.SpecialtyId == specialtyId.Value);
        }

        var doctors = await query.ToListAsync();

        var result = doctors.Select(d => {
            var master = doctorMasterList.FirstOrDefault(m => d.FullName != null && d.FullName.Contains(m.Name.Substring(m.Name.LastIndexOf(' ') + 1)));
            return new
            {
                d.DoctorId,
                d.UserId,
                d.SpecialtyId,
                FullName = !string.IsNullOrEmpty(d.FullName) ? d.FullName : "BS. CKII Nguyễn Văn A",
                Degree = !string.IsNullOrEmpty(d.Degree) ? d.Degree : "Chuyên khoa Bác sĩ",
                d.ExperienceYears,
                ClinicRoom = !string.IsNullOrEmpty(d.ClinicRoom) ? d.ClinicRoom : "Phòng 101",
                d.Rating,
                d.ReviewCount,
                WorkingDaysText = master?.WorkingDays ?? "Thứ Hai đến Thứ Bảy",
                d.Status
            };
        }).ToList();

        return Ok(result);
    }

    [HttpGet("schedules")]
    public async Task<IActionResult> GetDoctorSchedules([FromQuery] int? doctorId, [FromQuery] int? specialtyId, [FromQuery] string? dateStr)
    {
        await SeedDoctorSchedulesAsync();

        var doctors = await _context.Doctors.ToListAsync();

        if (doctorId.HasValue && doctorId.Value > 0)
        {
            doctors = doctors.Where(d => d.DoctorId == doctorId.Value).ToList();
        }
        else if (specialtyId.HasValue && specialtyId.Value > 0)
        {
            doctors = doctors.Where(d => d.SpecialtyId == specialtyId.Value).ToList();
        }

        DateTime targetDate = DateTime.Today;
        if (!string.IsNullOrEmpty(dateStr) && DateTime.TryParse(dateStr, out var parsed))
        {
            targetDate = parsed.Date;
        }

        var list = new List<object>();

        foreach (var doc in doctors)
        {
            bool isWorking = CheckDoctorWorkingDay(doc.DoctorId, targetDate);
            string[] timeSlots = isWorking ? GenerateDoctorTimeSlots(doc.DoctorId, targetDate) : new string[0];

            string shiftDescription = "Nghỉ phép (Off)";
            if (isWorking)
            {
                int shiftType = (doc.DoctorId + targetDate.DayOfYear) % 3;
                shiftDescription = shiftType == 0 ? "Khám ca Sáng" : shiftType == 1 ? "Khám ca Chiều" : "Khám Cả Ngày";
            }

            list.Add(new
            {
                doc.DoctorId,
                doc.SpecialtyId,
                FullName = doc.FullName ?? "Bác sĩ DTT",
                Degree = doc.Degree ?? "ThS. Bác sĩ",
                ClinicRoom = doc.ClinicRoom ?? "Phòng 101",
                Date = targetDate.ToString("dd/MM/yyyy"),
                DayOfWeek = GetVietnameseDayName(targetDate.DayOfWeek),
                IsWorking = isWorking,
                StatusText = shiftDescription,
                TimeSlots = timeSlots
            });
        }

        return Ok(list);
    }

    private static bool CheckDoctorWorkingDay(int doctorId, DateTime date)
    {
        var dow = date.DayOfWeek;
        int pattern = doctorId % 4;

        switch (pattern)
        {
            case 1: // Group A: Mon, Wed, Fri, Sun
                return dow == DayOfWeek.Monday || dow == DayOfWeek.Wednesday || dow == DayOfWeek.Friday || dow == DayOfWeek.Sunday;
            case 2: // Group B: Tue, Thu, Sat, Sun
                return dow == DayOfWeek.Tuesday || dow == DayOfWeek.Thursday || dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;
            case 3: // Group C: Mon, Tue, Thu, Fri
                return dow == DayOfWeek.Monday || dow == DayOfWeek.Tuesday || dow == DayOfWeek.Thursday || dow == DayOfWeek.Friday;
            case 0: // Group D: Wed, Thu, Fri, Sat
            default:
                return dow == DayOfWeek.Wednesday || dow == DayOfWeek.Thursday || dow == DayOfWeek.Friday || dow == DayOfWeek.Saturday;
        }
    }

    private static string[] GenerateDoctorTimeSlots(int doctorId, DateTime date)
    {
        int shiftPattern = (doctorId + date.DayOfYear) % 3;
        return shiftPattern switch
        {
            0 => new[] { "07:30 - 08:30", "08:30 - 09:30", "09:30 - 10:30", "10:30 - 11:30" }, // Ca Sáng
            1 => new[] { "13:30 - 14:30", "14:30 - 15:30", "15:30 - 16:30", "16:30 - 17:30" }, // Ca Chiều
            _ => new[] { "08:00 - 09:00", "09:30 - 10:30", "13:30 - 14:30", "15:00 - 16:00" }, // Cả ngày
        };
    }

    private static string GetVietnameseDayName(DayOfWeek dow)
    {
        return dow switch
        {
            DayOfWeek.Monday => "Thứ Hai",
            DayOfWeek.Tuesday => "Thứ Ba",
            DayOfWeek.Wednesday => "Thứ Tư",
            DayOfWeek.Thursday => "Thứ Năm",
            DayOfWeek.Friday => "Thứ Sáu",
            DayOfWeek.Saturday => "Thứ Bảy",
            DayOfWeek.Sunday => "Chủ Nhật",
            _ => "Ngày khám"
        };
    }

    private async Task SeedDoctorSchedulesAsync()
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            var doctors = await _context.Doctors.ToListAsync();
            if (doctors.Count == 0) return;

            for (int dayOffset = 0; dayOffset < 14; dayOffset++)
            {
                var workDate = DateTime.Today.AddDays(dayOffset);

                foreach (var doc in doctors)
                {
                    bool isOnDuty = CheckDoctorWorkingDay(doc.DoctorId, workDate);
                    if (isOnDuty)
                    {
                        using var insCmd = conn.CreateCommand();
                        insCmd.CommandText = @"
                            INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time, status)
                            SELECT @docId, @workDate, '08:00:00'::time, '17:00:00'::time, 'Available'
                            WHERE NOT EXISTS (
                                SELECT 1 FROM doctor_schedules WHERE doctor_id = @docId AND work_date = @workDate
                            )";
                        
                        var p1 = insCmd.CreateParameter(); p1.ParameterName = "@docId"; p1.Value = doc.DoctorId; insCmd.Parameters.Add(p1);
                        var p2 = insCmd.CreateParameter(); p2.ParameterName = "@workDate"; p2.Value = workDate.Date; insCmd.Parameters.Add(p2);
                        
                        await insCmd.ExecuteNonQueryAsync();
                    }
                }
            }

            using var slotCmd = conn.CreateCommand();
            slotCmd.CommandText = @"
                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 1, '07:30:00'::time, '08:30:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND slot_order = 1);

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 2, '08:30:00'::time, '09:30:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND slot_order = 2);

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 3, '13:30:00'::time, '14:30:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND slot_order = 3);

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 4, '15:30:00'::time, '16:30:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND slot_order = 4);";
            await slotCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("SeedDoctorSchedulesAsync info: " + ex.Message);
        }
    }
}
