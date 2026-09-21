using System.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;
using DTT_Backend_API.Helpers;

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
        // Only perform heavy DB seeding (150+ SQL queries) if database is completely empty
        if (!await _context.Doctors.AnyAsync())
        {
            var specCount = await _context.Specialties.CountAsync();
            if (specCount < 11)
            {
                // Khớp đủ 11 chuyên khoa thật trong DB (trước đây chỉ seed 8, thiếu Răng hàm mặt/
                // Tai-Mũi-Họng/Mắt) — mỗi dòng chỉ insert nếu SpecialtyId đó CHƯA tồn tại (xem vòng lặp
                // bên dưới), nên không đụng tới 8 chuyên khoa đã có sẵn trên DB thật.
                var defaults = new[]
                {
                    new Specialty { SpecialtyId = 1, SpecialtyName = "Nội tổng quát", Description = "Khám bệnh nội khoa chung", Status = true },
                    new Specialty { SpecialtyId = 2, SpecialtyName = "Nhi khoa", Description = "Chăm sóc sức khỏe trẻ em", Status = true },
                    new Specialty { SpecialtyId = 3, SpecialtyName = "Sản phụ khoa", Description = "Khám thai và bệnh phụ khoa", Status = true },
                    new Specialty { SpecialtyId = 4, SpecialtyName = "Cơ xương khớp", Description = "Điều trị bệnh cơ xương khớp", Status = true },
                    new Specialty { SpecialtyId = 5, SpecialtyName = "Tim mạch", Description = "Khám và điều trị bệnh tim mạch", Status = true },
                    new Specialty { SpecialtyId = 6, SpecialtyName = "Thần kinh", Description = "Tầm soát bệnh lý thần kinh", Status = true },
                    new Specialty { SpecialtyId = 7, SpecialtyName = "Da liễu", Description = "Khám và điều trị bệnh da liễu", Status = true },
                    new Specialty { SpecialtyId = 8, SpecialtyName = "Chẩn đoán hình ảnh", Description = "Siêu âm, X-quang, chụp CT scanner", Status = true },
                    new Specialty { SpecialtyId = 9, SpecialtyName = "Răng hàm mặt", Description = "Khám và điều trị các bệnh lý về răng, hàm, mặt", Status = true },
                    new Specialty { SpecialtyId = 10, SpecialtyName = "Tai-Mũi-Họng", Description = "Khám và điều trị các bệnh lý về tai, mũi và họng", Status = true },
                    new Specialty { SpecialtyId = 11, SpecialtyName = "Mắt", Description = "Khám, chẩn đoán và điều trị các bệnh lý về mắt", Status = true }
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

            var roleCount = await _context.Roles.CountAsync();
            if (roleCount == 0)
            {
                // Đúng theo bảng roles thật + quy ước dùng xuyên suốt codebase (AuthController.Register,
                // AccessControl.IsStaff kiểm tra role_id=="3"...): 1=Admin, 2=Doctor, 3=Patient. Trước đây
                // bị đảo ngược (1=Patient, 3=Admin) — chỉ gây sai LỆCH TÊN hiển thị nếu bảng roles từng
                // trống khi endpoint này chạy trước AuthController, không ảnh hưởng phân quyền (mọi kiểm
                // tra quyền đều dựa vào role_id số, không phải RoleName).
                _context.Roles.AddRange(
                    new Role { RoleId = 1, RoleName = "Admin", Description = "Quản trị viên" },
                    new Role { RoleId = 2, RoleName = "Doctor", Description = "Bác sĩ" },
                    new Role { RoleId = 3, RoleName = "Patient", Description = "Bệnh nhân" }
                );
                await _context.SaveChangesAsync();
            }

        }

        /* [OLD CODE COMMENTED OUT — chưa quét cập nhật trạng thái khi hết hạn nghỉ phép]
        var specialtiesDict = await _context.Specialties.AsNoTracking().ToDictionaryAsync(s => s.SpecialtyId, s => s.SpecialtyName);
        var userIds = await _context.Doctors.AsNoTracking().Select(d => d.UserId).Distinct().ToListAsync();
        var usersDict = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId);
        */

        // Tự động kiểm tra và cập nhật các bác sĩ đã hết thời gian nghỉ phép sang 'Active'
        await DoctorLeavesController.AutoUpdateExpiredDoctorLeavesAsync(_context);

        var specialtiesDict = await _context.Specialties.AsNoTracking().ToDictionaryAsync(s => s.SpecialtyId, s => s.SpecialtyName);
        var userIds = await _context.Doctors.AsNoTracking().Select(d => d.UserId).Distinct().ToListAsync();
        var usersDict = await _context.Users.AsNoTracking().Where(u => userIds.Contains(u.UserId)).ToDictionaryAsync(u => u.UserId);

        var query = _context.Doctors.AsNoTracking().AsQueryable();
        // Strictly include only real doctors (BS., Bác sĩ, ThS., TS.)
        /* [OLD CODE COMMENTED OUT — bộ lọc lọc mất bác sĩ tạo mới không có tiền tố BS./Bác sĩ]
        query = query.Where(d => d.FullName != null && (d.FullName.Contains("BS.") || d.FullName.Contains("Bác sĩ") || d.FullName.Contains("ThS.") || d.FullName.Contains("TS.")));
        */

        if (specialtyId.HasValue && specialtyId.Value > 0)
        {
            query = query.Where(d => d.SpecialtyId == specialtyId.Value);
        }

        // Chỉ Admin (Web Admin, quản lý toàn bộ bác sĩ kể cả Khóa/Nghỉ phép) mới thấy đủ mọi trạng
        // thái. Với các caller khác (Mobile duyệt danh sách theo chuyên khoa): bác sĩ "OnLeave" (nghỉ
        // phép — vd hôm nay không đi làm) VẪN phải hiện ra khi duyệt/xem hồ sơ theo chuyên khoa, chỉ
        // ẩn ở đúng bước ĐẶT LỊCH cho ngày họ nghỉ (GetDoctorSchedules đã xử lý riêng qua isWorking).
        // Trước đây lọc "chỉ Active" ở NGAY BƯỚC DUYỆT CHUYÊN KHOA khiến bác sĩ nghỉ phép biến mất
        // hoàn toàn kể cả khi bệnh nhân chỉ đang xem thông tin, không hề đặt lịch. Chỉ thật sự ẩn với
        // "Locked" (tài khoản bị khóa) và "Inactive" (đã nghỉ việc) — 2 trạng thái này mới có nghĩa là
        // bác sĩ không còn nên xuất hiện trước bệnh nhân ở bất kỳ đâu.
        bool isAdminCaller = User.FindFirst("role_id")?.Value == "1";
        if (!isAdminCaller)
        {
            query = query.Where(d => string.IsNullOrEmpty(d.Status) || d.Status == "Active" || d.Status == "OnLeave");
            // Hồ sơ Admin tự đánh dấu "dữ liệu test" (QA tạo để thử nghiệm) — ẩn khỏi App Bệnh nhân dù
            // đang Active/OnLeave, không cần khóa tài khoản (bác sĩ test vẫn dùng WinForms bình thường).
            query = query.Where(d => !d.IsTestData);
        }

        // Lấy doctors trước rồi mới suy ra doctorIds từ kết quả trong bộ nhớ, thay vì quét lại toàn
        // bộ bảng Doctors 1 lần nữa chỉ để lấy danh sách ID (trước đây quét bảng Doctors 2 lần/request).
        var doctors = await query.ToListAsync();
        var doctorIds = doctors.Select(d => d.DoctorId).ToList();
        var leavesList = await _context.DoctorLeaves
            .AsNoTracking()
            .Where(l => doctorIds.Contains(l.DoctorId))
            .OrderByDescending(l => l.CreatedAt)
            .ToListAsync();
        var leavesDict = leavesList
            .GroupBy(l => l.DoctorId)
            .ToDictionary(g => g.Key, g => g.First());

        var today = DateOnly.FromDateTime(DateTime.Today);
        var workingDaysByDoctor = await GetWorkingDaysTextAsync(doctorIds);

        var result = doctors.Select(d => {
            usersDict.TryGetValue(d.UserId, out var u);
            specialtiesDict.TryGetValue(d.SpecialtyId ?? 0, out var specName);
            leavesDict.TryGetValue(d.DoctorId, out var leaveRecord);

            string rawStatus = !string.IsNullOrEmpty(d.Status) ? d.Status : (u?.Status ?? "Active");
            string formattedStatus = rawStatus switch
            {
                "Active" or "active" or "Đang hoạt động" => "Đang hoạt động",
                "Inactive" or "inactive" or "Ngưng hoạt động" => "Ngưng hoạt động",
                "Locked" or "locked" or "Đã khóa" => "Đã khóa",
                "OnLeave" or "onleave" or "Nghỉ phép" => (leaveRecord != null && leaveRecord.LeaveEndDate < today) ? "Đang hoạt động" : "Nghỉ phép",
                _ => "Đang hoạt động"
            };

            return new
            {
                d.DoctorId,
                id = d.DoctorId,
                d.UserId,
                d.SpecialtyId,
                SpecialtyName = specName ?? "",
                specialty = specName ?? "",
                FullName = !string.IsNullOrEmpty(d.FullName) ? d.FullName : (u?.FullName ?? "Bác sĩ DTT"),
                Degree = d.Degree ?? "",
                d.ExperienceYears,
                ClinicRoom = d.ClinicRoom ?? "",
                d.Rating,
                d.ReviewCount,
                ratingAverage = d.Rating,
                totalReviews = d.ReviewCount,
                Phone = u?.PhoneNumber ?? "Chưa cập nhật",
                Email = u?.Email ?? "Chưa cập nhật",
                userEmail = u?.Email ?? "",
                // Các thứ trong tuần bác sĩ CÓ lịch trực thật trong 4 tuần tới (doctor_schedules). Trước đây lấy từ danh sách bác sĩ
                // mẫu khớp theo TỪ CUỐI của tên ("A", "B", "C"...) nên bác sĩ thật nào có chữ "A" trong tên cũng bị gán
                // "Thứ Hai, Tư, Sáu & Chủ Nhật"; không khớp thì luôn ghi "Thứ Hai đến Thứ Bảy".
                WorkingDaysText = workingDaysByDoctor.TryGetValue(d.DoctorId, out var wdText) ? wdText : "",
                Status = formattedStatus,
                Avatar = d.AvatarUrl ?? u?.AvatarUrl ?? "",
                AvatarUrl = d.AvatarUrl ?? u?.AvatarUrl ?? "",
                LeaveStartDate = leaveRecord != null ? leaveRecord.LeaveStartDate.ToString("dd/MM/yyyy") : null,
                LeaveEndDate = leaveRecord != null ? leaveRecord.LeaveEndDate.ToString("dd/MM/yyyy") : null,
                LeaveReason = leaveRecord?.Reason,
                LeaveStatus = leaveRecord != null ? FormatLeaveStatusToVi(leaveRecord.Status) : null,
                // CHỈ 1 property — trước đây có cả IsTestData VÀ isTestData (2 tên khác nhau trong C#
                // nhưng cùng biến thành "isTestData" sau khi ASP.NET Core tự chuyển camelCase), khiến
                // System.Text.Json ném lỗi "collides with another property" và sập HẲN endpoint này
                // (500 Internal Server Error) — đây là nguyên nhân khiến app Mobile không gọi được API
                // thật, phải rơi về dữ liệu fallback (đúng như ảnh bạn gửi: mọi chuyên khoa đều hiện
                // lại y hệt 2 bác sĩ fallback "Nguyễn Văn A"/"Lê Thị B" bất kể bấm vào khoa nào).
                IsTestData = d.IsTestData
            };
        }).ToList();

        return Ok(result);
    }

    // Tên các thứ trong tuần (Thứ Hai → Chủ Nhật) mà mỗi bác sĩ có lịch trực THẬT (doctor_schedules, trạng thái khác
    // Unavailable/Off) trong 28 ngày tới. Bác sĩ chưa có lịch nào → không có mục trong kết quả (client tự hiện "chưa có lịch").
    private async Task<Dictionary<int, string>> GetWorkingDaysTextAsync(List<int> doctorIds)
    {
        var result = new Dictionary<int, string>();
        if (doctorIds.Count == 0) return result;
        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = @"SELECT DISTINCT doctor_id, work_date FROM doctor_schedules
                                WHERE work_date >= CURRENT_DATE AND work_date < CURRENT_DATE + 28
                                  AND COALESCE(status, 'Available') NOT IN ('Unavailable', 'Off')";
            var daysByDoctor = new Dictionary<int, HashSet<DayOfWeek>>();
            using (var reader = await cmd.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    int docId = reader.GetInt32(0);
                    var workDate = reader.GetDateTime(1);
                    if (!daysByDoctor.TryGetValue(docId, out var set)) daysByDoctor[docId] = set = new HashSet<DayOfWeek>();
                    set.Add(workDate.DayOfWeek);
                }
            }

            // Thứ tự hiển thị: Thứ Hai → Thứ Bảy, Chủ Nhật cuối
            var order = new[] { DayOfWeek.Monday, DayOfWeek.Tuesday, DayOfWeek.Wednesday, DayOfWeek.Thursday, DayOfWeek.Friday, DayOfWeek.Saturday, DayOfWeek.Sunday };
            foreach (var kv in daysByDoctor)
            {
                result[kv.Key] = string.Join(", ", order.Where(kv.Value.Contains).Select(GetVietnameseDayName));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine("GetWorkingDaysTextAsync warning: " + ex.Message);
        }
        return result;
    }

    // Helper chuẩn hóa trạng thái đơn nghỉ phép theo đúng CHECK CONSTRAINT ('Pending', 'Approved', 'Rejected', 'Cancelled')
    private static string NormalizeLeaveStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Approved";
        var s = rawStatus.Trim();
        if (s == "Chờ duyệt" || s.Equals("pending", StringComparison.OrdinalIgnoreCase)) return "Pending";
        if (s == "Đã duyệt" || s.Equals("approved", StringComparison.OrdinalIgnoreCase)) return "Approved";
        if (s == "Từ chối" || s.Equals("rejected", StringComparison.OrdinalIgnoreCase)) return "Rejected";
        if (s == "Đã hủy" || s.Equals("cancelled", StringComparison.OrdinalIgnoreCase)) return "Cancelled";
        return "Approved";
    }

    private static string FormatLeaveStatusToVi(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Đã duyệt";
        var s = rawStatus.Trim();
        if (s == "Pending" || s == "Chờ duyệt") return "Chờ duyệt";
        if (s == "Approved" || s == "Đã duyệt") return "Đã duyệt";
        if (s == "Rejected" || s == "Từ chối") return "Từ chối";
        if (s == "Cancelled" || s == "Đã hủy") return "Đã hủy";
        return "Đã duyệt";
    }

    private static DateOnly ParseDateOnlyDDMMYYYY(string? dateStr)
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

    /* [OLD CODE COMMENTED OUT — bảng users trước đây chỉ map OnLeave về Inactive]
    private static string NormalizeUserStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Active";
        var s = rawStatus.Trim();
        if (s == "Đang hoạt động" || s.Equals("active", StringComparison.OrdinalIgnoreCase))
            return "Active";
        if (s == "Đã khóa" || s.Equals("locked", StringComparison.OrdinalIgnoreCase))
            return "Locked";
        if (s == "Ngưng hoạt động" || s.Equals("inactive", StringComparison.OrdinalIgnoreCase))
            return "Inactive";
        if (s == "Nghỉ phép" || s.Equals("onleave", StringComparison.OrdinalIgnoreCase))
            return "Inactive"; // Bảng users chỉ hỗ trợ Active, Inactive, Locked
        return "Active";
    }
    */
    // Helper chuẩn hóa trạng thái cho bảng users (chấp nhận Active / Inactive / Locked / OnLeave)
    private static string NormalizeUserStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Active";
        var s = rawStatus.Trim();
        if (s == "Đang hoạt động" || s.Equals("active", StringComparison.OrdinalIgnoreCase))
            return "Active";
        if (s == "Đã khóa" || s.Equals("locked", StringComparison.OrdinalIgnoreCase))
            return "Locked";
        if (s == "Ngưng hoạt động" || s.Equals("inactive", StringComparison.OrdinalIgnoreCase))
            return "Inactive";
        if (s == "Nghỉ phép" || s.Equals("onleave", StringComparison.OrdinalIgnoreCase))
            return "OnLeave";
        return "Active";
    }

    // Helper chuẩn hóa trạng thái cho bảng doctors (Active / Inactive / Locked / OnLeave)
    private static string NormalizeDoctorStatus(string? rawStatus)
    {
        if (string.IsNullOrWhiteSpace(rawStatus)) return "Active";
        var s = rawStatus.Trim();
        if (s == "Đang hoạt động" || s.Equals("active", StringComparison.OrdinalIgnoreCase))
            return "Active";
        if (s == "Đã khóa" || s.Equals("locked", StringComparison.OrdinalIgnoreCase))
            return "Locked";
        if (s == "Ngưng hoạt động" || s.Equals("inactive", StringComparison.OrdinalIgnoreCase))
            return "Inactive";
        if (s == "Nghỉ phép" || s.Equals("onleave", StringComparison.OrdinalIgnoreCase))
            return "OnLeave";
        return "Active";
    }

    // POST /api/doctors — Tạo mới Bác sĩ
    // [StaffOnly] — phát hiện thêm khi rà soát toàn bộ Controller lần 2: trước đây không có bất kỳ
    // kiểm tra quyền nào, bất kỳ ai đăng nhập (kể cả bệnh nhân) cũng tạo được hồ sơ Bác sĩ giả.
    [HttpPost]
    [StaffOnly]
    public async Task<IActionResult> CreateDoctor([FromBody] DTT_Backend_API.DTOs.CreateDoctorDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            if (!string.IsNullOrWhiteSpace(dto.Phone) && dto.Phone.Trim().Length > 10)
            {
                return BadRequest(new { message = "Số điện thoại không được vượt quá 10 chữ số." });
            }

            var phone = dto.Phone.Trim();
            var existingUser = await _context.Users.AnyAsync(u => u.PhoneNumber == phone || (!string.IsNullOrEmpty(dto.Email) && u.Email == dto.Email));
            if (existingUser)
            {
                return BadRequest(new { message = "Số điện thoại hoặc Email đã được sử dụng." });
            }

            string password = !string.IsNullOrWhiteSpace(dto.Password) ? dto.Password : "Doctor@123";
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);
            string normDocStatus = NormalizeDoctorStatus(dto.Status);
            string normUserStatus = NormalizeUserStatus(dto.Status);

            string? inputAvatar = !string.IsNullOrWhiteSpace(dto.AvatarUrl) ? dto.AvatarUrl : dto.Avatar;

            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                PhoneNumber = phone,
                Email = !string.IsNullOrWhiteSpace(dto.Email) ? dto.Email : $"{phone}@dtt.health",
                PasswordHash = hashedPassword,
                RoleId = 2, // Doctor
                FullName = dto.FullName,
                //Status = !string.IsNullOrWhiteSpace(dto.Status) ? dto.Status : "Active",
                Status = normUserStatus,
                AvatarUrl = inputAvatar,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            var newDoctor = new Doctor
            {
                UserId = newUser.UserId,
                FullName = dto.FullName,
                Degree = dto.Degree,
                ExperienceYears = dto.ExperienceYears,
                ClinicRoom = dto.ClinicRoom,
                SpecialtyId = dto.SpecialtyId > 0 ? dto.SpecialtyId : 1,
                // Status = newUser.Status,
                Status = normDocStatus,
                AvatarUrl = inputAvatar,
                Rating = 0m, // chưa có đánh giá thật nào — không mặc định 5 sao
                ReviewCount = 0,
                IsTestData = dto.IsTestData
            };

            _context.Doctors.Add(newDoctor);
            await _context.SaveChangesAsync();

            // Sinh sẵn lịch làm việc thật (doctor_schedules/doctor_schedule_slots) cho 7 ngày tới —
            // trước đây bác sĩ tạo qua Admin không có lịch nào (SeedDoctorSchedulesAsync chỉ chạy 1
            // lần/vòng đời app, không áp dụng cho bác sĩ tạo sau đó), nên Mobile phải hiện giờ khám giả
            // (GenerateDoctorTimeSlots) và đặt lịch chỉ tạo được đúng 1 slot fallback rồi hết.
            await SeedInitialScheduleForNewDoctorAsync(newDoctor.DoctorId);

            if (normDocStatus == "OnLeave" || !string.IsNullOrWhiteSpace(dto.LeaveStartDate))
            {
                var startDate = ParseDateOnlyDDMMYYYY(dto.LeaveStartDate);
                var endDate = ParseDateOnlyDDMMYYYY(dto.LeaveEndDate ?? dto.LeaveStartDate);
                string dbLeaveStatus = NormalizeLeaveStatus(dto.LeaveStatus ?? "Approved");

                var newLeave = new DoctorLeave
                {
                    DoctorId = newDoctor.DoctorId,
                    LeaveStartDate = startDate,
                    LeaveEndDate = endDate,
                    Reason = dto.LeaveReason,
                    Status = dbLeaveStatus,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.DoctorLeaves.Add(newLeave);
                await _context.SaveChangesAsync();
            }

            string specName = "";
            if (newDoctor.SpecialtyId.HasValue)
            {
                var spec = await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == newDoctor.SpecialtyId.Value);
                if (spec != null) specName = spec.SpecialtyName;
            }

            return Ok(new
            {
                success = true,
                message = "Tạo tài khoản Bác sĩ mới thành công!",
                doctor = new
                {
                    newDoctor.DoctorId,
                    id = newDoctor.DoctorId,
                    newDoctor.UserId,
                    newDoctor.SpecialtyId,
                    SpecialtyName = specName,
                    specialty = specName,
                    FullName = newDoctor.FullName,
                    Degree = newDoctor.Degree,
                    newDoctor.ExperienceYears,
                    ClinicRoom = newDoctor.ClinicRoom,
                    newDoctor.Rating,
                    newDoctor.ReviewCount,
                    Phone = newUser.PhoneNumber,
                    Email = newUser.Email,
                    Status = newDoctor.Status,
                    Avatar = newDoctor.AvatarUrl ?? "",
                    AvatarUrl = newDoctor.AvatarUrl ?? ""
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi tạo tài khoản Bác sĩ mới.", error = ex.Message });
        }
    }

    // PUT /api/doctors/{id} — Cập nhật thông tin Bác sĩ
    // [StaffOnly] — trước đây không kiểm tra quyền, bất kỳ ai đăng nhập cũng sửa được hồ sơ bất kỳ
    // Bác sĩ nào.
    [HttpPut("{id}")]
    [StaffOnly]
    public async Task<IActionResult> UpdateDoctor(int id, [FromBody] DTT_Backend_API.DTOs.UpdateDoctorDto dto)
    {
        try
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor == null)
            {
                return NotFound(new { message = "Không tìm thấy thông tin Bác sĩ." });
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == doctor.UserId);
            if (user == null && doctor.UserId == Guid.Empty)
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.FullName != null && u.FullName == doctor.FullName);
            }

            string? inputAvatar = dto.AvatarUrl ?? dto.Avatar;
            if (inputAvatar != null)
            {
                doctor.AvatarUrl = inputAvatar;
                if (user != null) user.AvatarUrl = inputAvatar;
            }

            if (!string.IsNullOrWhiteSpace(dto.FullName))
            {
                doctor.FullName = dto.FullName;
                if (user != null) user.FullName = dto.FullName;
            }

            if (!string.IsNullOrWhiteSpace(dto.Degree)) doctor.Degree = dto.Degree;
            if (dto.ExperienceYears.HasValue) doctor.ExperienceYears = dto.ExperienceYears.Value;
            if (!string.IsNullOrWhiteSpace(dto.ClinicRoom)) doctor.ClinicRoom = dto.ClinicRoom;
            if (dto.SpecialtyId.HasValue && dto.SpecialtyId.Value > 0) doctor.SpecialtyId = dto.SpecialtyId.Value;
            if (dto.IsTestData.HasValue) doctor.IsTestData = dto.IsTestData.Value;

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                // doctor.Status = dto.Status;
                // if (user != null) user.Status = dto.Status;
                string normDocStatus = NormalizeDoctorStatus(dto.Status);
                string normUserStatus = NormalizeUserStatus(dto.Status);
                doctor.Status = normDocStatus;
                if (user != null) user.Status = normUserStatus;
            }

            if (user != null)
            {
                if (!string.IsNullOrWhiteSpace(dto.Phone) && dto.Phone != user.PhoneNumber)
                {
                    bool phoneExists = await _context.Users.AnyAsync(u => u.PhoneNumber == dto.Phone && u.UserId != user.UserId);
                    if (phoneExists)
                    {
                        return BadRequest(new { message = "Số điện thoại đã được sử dụng bởi tài khoản khác." });
                    }
                    user.PhoneNumber = dto.Phone;
                }

                if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != user.Email)
                {
                    bool emailExists = await _context.Users.AnyAsync(u => u.Email == dto.Email && u.UserId != user.UserId);
                    if (emailExists)
                    {
                        return BadRequest(new { message = "Email đã được sử dụng bởi tài khoản khác." });
                    }
                    user.Email = dto.Email;
                }

                user.UpdatedAt = DateTime.UtcNow;
            }

            if (doctor.Status == "OnLeave" || !string.IsNullOrWhiteSpace(dto.LeaveStartDate))
            {
                var startDate = ParseDateOnlyDDMMYYYY(dto.LeaveStartDate);
                var endDate = ParseDateOnlyDDMMYYYY(dto.LeaveEndDate ?? dto.LeaveStartDate);
                
                // Mặc định đơn mới tạo luôn ở trạng thái Pending (Chờ duyệt) trừ khi được ghi rõ Approved
                string dbLeaveStatus = "Pending";
                if (!string.IsNullOrWhiteSpace(dto.LeaveStatus) && (dto.LeaveStatus.Equals("Approved", StringComparison.OrdinalIgnoreCase) || dto.LeaveStatus == "Đã duyệt"))
                {
                    dbLeaveStatus = "Approved";
                }

                // Luôn tạo mới đơn nghỉ phép (mã đơn mới) mỗi lần đăng ký nghỉ phép
                var newLeave = new DoctorLeave
                {
                    DoctorId = doctor.DoctorId,
                    LeaveStartDate = startDate,
                    LeaveEndDate = endDate,
                    Reason = dto.LeaveReason,
                    Status = dbLeaveStatus,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow,
                    ApprovedAt = dbLeaveStatus == "Approved" ? DateTime.UtcNow : null
                };
                _context.DoctorLeaves.Add(newLeave);

                if (dbLeaveStatus == "Approved")
                {
                    // Cập nhật trạng thái các ca làm việc trùng lịch sang 'Off' (giữ nguyên dữ liệu trong CSDL)
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
                        var p1 = updCmd.CreateParameter(); p1.ParameterName = "@docId"; p1.Value = doctor.DoctorId; updCmd.Parameters.Add(p1);
                        var p2 = updCmd.CreateParameter(); p2.ParameterName = "@sDate"; p2.Value = startDate.ToDateTime(TimeOnly.MinValue); updCmd.Parameters.Add(p2);
                        var p3 = updCmd.CreateParameter(); p3.ParameterName = "@eDate"; p3.Value = endDate.ToDateTime(TimeOnly.MinValue); updCmd.Parameters.Add(p3);
                        await updCmd.ExecuteNonQueryAsync();
                    }
                    catch (Exception schedEx)
                    {
                        Console.WriteLine($"[UpdateDoctor Warning] Không thể cập nhật status doctor_schedules: {schedEx.Message}");
                    }
                }
            }

            await _context.SaveChangesAsync();

            string specName = "";
            if (doctor.SpecialtyId.HasValue)
            {
                var spec = await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == doctor.SpecialtyId.Value);
                if (spec != null) specName = spec.SpecialtyName;
            }

            return Ok(new
            {
                success = true,
                message = "Cập nhật thông tin Bác sĩ thành công!",
                doctor = new
                {
                    doctor.DoctorId,
                    id = doctor.DoctorId,
                    doctor.UserId,
                    doctor.SpecialtyId,
                    SpecialtyName = specName,
                    specialty = specName,
                    FullName = doctor.FullName,
                    Degree = doctor.Degree,
                    doctor.ExperienceYears,
                    ClinicRoom = doctor.ClinicRoom,
                    doctor.Rating,
                    doctor.ReviewCount,
                    Phone = user?.PhoneNumber ?? "Chưa cập nhật",
                    Email = user?.Email ?? "Chưa cập nhật",
                    Status = doctor.Status,
                    Avatar = doctor.AvatarUrl ?? "",
                    AvatarUrl = doctor.AvatarUrl ?? ""
                }
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[UpdateDoctor Error]: {ex.Message} | Inner: {ex.InnerException?.Message}");
            return StatusCode(500, new { message = $"Lỗi khi cập nhật thông tin Bác sĩ: {ex.InnerException?.Message ?? ex.Message}", error = ex.Message });
        }
    }

    // PUT /api/doctors/{id}/status — Cập nhật trạng thái/Khóa Bác sĩ
    // [StaffOnly] — trước đây không kiểm tra quyền, bất kỳ ai đăng nhập cũng khóa/mở khóa được bất kỳ
    // Bác sĩ nào.
    [HttpPut("{id}/status")]
    [StaffOnly]
    public async Task<IActionResult> UpdateDoctorStatus(int id, [FromBody] DTT_Backend_API.DTOs.UpdateDoctorStatusDto dto)
    {
        try
        {
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.DoctorId == id);
            if (doctor == null)
            {
                return NotFound(new { message = "Không tìm thấy Bác sĩ." });
            }

            string normDocStatus = NormalizeDoctorStatus(dto.Status);
            string normUserStatus = NormalizeUserStatus(dto.Status);
            doctor.Status = normDocStatus;
            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == doctor.UserId);
            if (user != null)
            {
                // user.Status = dto.Status;

                user.Status = normUserStatus;
                user.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cập nhật trạng thái Bác sĩ thành công!",
                status = doctor.Status
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi cập nhật trạng thái Bác sĩ.", error = ex.Message });
        }
    }

    [HttpGet("schedules")]
    public async Task<IActionResult> GetDoctorSchedules([FromQuery] int? doctorId, [FromQuery] int? specialtyId, [FromQuery] string? dateStr)
    {
        /* [OLD CODE COMMENTED OUT — bộ lọc lọc mất bác sĩ tạo mới không có tiền tố BS./Bác sĩ]
        var doctors = await _context.Doctors
            .Where(d => d.FullName != null && (d.FullName.Contains("BS.") || d.FullName.Contains("Bác sĩ") || d.FullName.Contains("ThS.") || d.FullName.Contains("TS.")))
            .ToListAsync();
        */
        var doctors = await _context.Doctors.ToListAsync();

        // Ẩn hồ sơ bác sĩ "dữ liệu test" (Admin tự đánh dấu IsTestData=true để QA thử nghiệm, vd
        // "BS. Điều trị" tự sinh khi 1 tài khoản RoleId=2 đăng nhập WinForms lần đầu chưa có hồ sơ
        // Doctor thật) khỏi mọi luồng đặt lịch — GetAllDoctors (danh sách bác sĩ theo chuyên khoa) đã
        // lọc !IsTestData từ trước, nhưng endpoint này (dùng riêng để tra khung giờ khám, gọi bởi cả
        // Mobile lẫn WinForms khi đặt lịch/đặt khám ngay/đặt tái khám) lại quét thẳng toàn bộ bảng
        // Doctors không lọc gì — khiến các hồ sơ test này lọt vào danh sách bác sĩ có thể đặt lịch,
        // hiện xen kẽ với bác sĩ thật ở MỌI chuyên khoa/ngày mà chúng có lịch làm việc. Chỉ Admin (Web
        // Admin quản lý toàn bộ, kể cả dữ liệu test) mới cần thấy đủ.
        bool isAdminCallerForSchedules = User.FindFirst("role_id")?.Value == "1";
        if (!isAdminCallerForSchedules)
        {
            doctors = doctors.Where(d => !d.IsTestData).ToList();
        }

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

        // Đọc lịch làm việc THẬT từ doctor_schedules/doctor_schedule_slots (trước đây hàm này tự sinh
        // isWorking/timeSlots giả bằng CheckDoctorWorkingDay/GenerateDoctorTimeSlots dựa trên doctorId/tên,
        // không liên quan gì tới dữ liệu DB — khiến Mobile/WinForms hiện lịch khám sai ngày/sai khung giờ
        // so với những gì Lễ Tân/Admin thấy trên Web).
        var doctorIds = doctors.Select(d => d.DoctorId).ToList();
        var schedulesByDoctor = new Dictionary<int, List<int>>(); // doctorId -> list of schedule_ids (ca sáng + ca chiều)
        var scheduleStatus = new Dictionary<int, string>(); // schedule_id -> status
        var slotsBySchedule = new Dictionary<int, List<string>>(); // schedule_id -> ["HH:mm - HH:mm", ...]
        // Một bác sĩ có thể có NHIỀU dòng doctor_schedules trong cùng 1 work_date (ca sáng + ca chiều,
        // có khoảng nghỉ trưa ở giữa — đã xác nhận qua khảo sát dữ liệu thật). Gộp lại thành khoảng
        // "sớm nhất -> muộn nhất" cho mỗi bác sĩ để lọc theo giờ hiện tại (chấp nhận đánh đổi: giờ nghỉ
        // trưa nằm trong khoảng này vẫn tính là "đang trong ca", đủ để chặn trường hợp báo lỗi gốc —
        // bác sĩ chỉ trực buổi sáng nhưng buổi tối vẫn hiện trong danh sách).
        var doctorShiftStart = new Dictionary<int, TimeSpan>(); // doctorId -> earliest start_time
        var doctorShiftEnd = new Dictionary<int, TimeSpan>(); // doctorId -> latest end_time

        if (doctorIds.Count > 0)
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            var idsStr = string.Join(",", doctorIds);
            var scheduleIds = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT schedule_id, doctor_id, status, start_time, end_time
                    FROM doctor_schedules
                    WHERE doctor_id IN ({idsStr}) AND work_date = @wDate";
                var p = cmd.CreateParameter(); p.ParameterName = "@wDate"; p.Value = targetDate.Date; cmd.Parameters.Add(p);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int schId = reader.GetInt32(0);
                    int docId = reader.GetInt32(1);
                    string status = reader.IsDBNull(2) ? "Available" : reader.GetString(2);
                    if (!schedulesByDoctor.ContainsKey(docId))
                        schedulesByDoctor[docId] = new List<int>();
                    schedulesByDoctor[docId].Add(schId);
                    scheduleStatus[schId] = status;
                    scheduleIds.Add(schId);

                    if (!reader.IsDBNull(3) && !reader.IsDBNull(4))
                    {
                        TimeSpan rowStart = GetTimeSpanValue(reader, 3);
                        TimeSpan rowEnd = GetTimeSpanValue(reader, 4);
                        if (!doctorShiftStart.TryGetValue(docId, out var curStart) || rowStart < curStart)
                            doctorShiftStart[docId] = rowStart;
                        if (!doctorShiftEnd.TryGetValue(docId, out var curEnd) || rowEnd > curEnd)
                            doctorShiftEnd[docId] = rowEnd;
                    }
                }
            }

            var nowVn = DateTime.UtcNow.AddHours(7);
            bool isPastDate = targetDate.Date < nowVn.Date;
            bool isToday = targetDate.Date == nowVn.Date;
            TimeSpan currentTime = nowVn.TimeOfDay;

            if (scheduleIds.Count > 0 && !isPastDate)
            {
                var schIdsStr = string.Join(",", scheduleIds);
                using var slotCmd = conn.CreateCommand();
                slotCmd.CommandText = $@"
                    SELECT schedule_id, start_time, end_time
                    FROM doctor_schedule_slots
                    WHERE schedule_id IN ({schIdsStr}) AND status = 'Available'
                      AND slot_id NOT IN (SELECT slot_id FROM appointments WHERE slot_id IS NOT NULL)
                    ORDER BY schedule_id ASC, start_time ASC";
                using var slotReader = await slotCmd.ExecuteReaderAsync();
                while (await slotReader.ReadAsync())
                {
                    int schId = slotReader.GetInt32(0);
                    TimeSpan sTime = GetTimeSpanValue(slotReader, 1);
                    TimeSpan eTime = GetTimeSpanValue(slotReader, 2);

                    // Nếu ngày xem là hôm nay, bỏ qua các khung giờ đã trôi qua so với giờ hiện tại
                    if (isToday && sTime <= currentTime)
                    {
                        continue;
                    }

                    if (!slotsBySchedule.ContainsKey(schId)) slotsBySchedule[schId] = new List<string>();
                    slotsBySchedule[schId].Add($"{sTime.Hours:D2}:{sTime.Minutes:D2} - {eTime.Hours:D2}:{eTime.Minutes:D2}");
                }
            }
        }

        var targetDateOnly = DateOnly.FromDateTime(targetDate);
        var onLeaveDoctorIds = await _context.DoctorLeaves
            .AsNoTracking()
            .Where(l => doctorIds.Contains(l.DoctorId) &&
                        (l.Status == "Approved" || l.Status == "Đã duyệt") &&
                        l.LeaveStartDate <= targetDateOnly &&
                        l.LeaveEndDate >= targetDateOnly)
            .Select(l => l.DoctorId)
            .Distinct()
            .ToListAsync();

        var list = new List<object>();

        foreach (var doc in doctors)
        {
            bool hasSchedule = schedulesByDoctor.TryGetValue(doc.DoctorId, out var docScheduleIds) && docScheduleIds.Count > 0;
            bool isWorking = hasSchedule && docScheduleIds!.Any(sId =>
            {
                var raw = scheduleStatus.GetValueOrDefault(sId, "Available");
                return raw != "Unavailable" && raw != "Off" && raw != "Không hoạt động";
            });

            // Gộp tất cả các khung giờ từ tất cả các ca (sáng + chiều) của bác sĩ
            var allSlots = new List<string>();
            if (isWorking && docScheduleIds != null)
            {
                foreach (var sId in docScheduleIds)
                {
                    var raw = scheduleStatus.GetValueOrDefault(sId, "Available");
                    if (raw != "Unavailable" && raw != "Off" && raw != "Không hoạt động")
                    {
                        if (slotsBySchedule.TryGetValue(sId, out var sSlots))
                        {
                            allSlots.AddRange(sSlots);
                        }
                    }
                }
            }
            var timeSlots = allSlots.Distinct().OrderBy(s => s).ToArray();

            bool isOnLeave = onLeaveDoctorIds.Contains(doc.DoctorId);
            string shiftDescription;
            if (isWorking)
            {
                shiftDescription = timeSlots.Length > 0 ? "Đang nhận lịch khám" : "Đã kín lịch khám";
            }
            else if (isOnLeave)
            {
                shiftDescription = "Nghỉ phép (Off)";
            }
            else
            {
                shiftDescription = "Không có lịch khám";
            }

            string shiftStartTime = "";
            string shiftEndTime = "";
            if (isWorking)
            {
                if (doctorShiftStart.TryGetValue(doc.DoctorId, out var sTime))
                    shiftStartTime = $"{sTime.Hours:D2}:{sTime.Minutes:D2}";
                if (doctorShiftEnd.TryGetValue(doc.DoctorId, out var eTime))
                    shiftEndTime = $"{eTime.Hours:D2}:{eTime.Minutes:D2}";
            }

            list.Add(new
            {
                doc.DoctorId,
                doc.SpecialtyId,
                FullName = doc.FullName ?? "Bác sĩ DTT",
                Degree = doc.Degree ?? "",
                ClinicRoom = doc.ClinicRoom ?? "",
                Date = targetDate.ToString("dd/MM/yyyy"),
                DayOfWeek = GetVietnameseDayName(targetDate.DayOfWeek),
                IsWorking = isWorking,
                IsOnLeave = isOnLeave,
                StatusText = shiftDescription,
                TimeSlots = timeSlots,
                ShiftStartTime = shiftStartTime,
                ShiftEndTime = shiftEndTime
            });
        }

        return Ok(list);
    }

    private static TimeSpan GetTimeSpanValue(System.Data.Common.DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        if (value is TimeSpan ts) return ts;
        if (value is DateTime dt) return dt.TimeOfDay;
        return TimeSpan.TryParse(value?.ToString(), out var parsed) ? parsed : TimeSpan.Zero;
    }

    // Sinh lịch làm việc thật (7 ngày tới, Thứ Hai-Thứ Bảy, 08:00-17:00, slot 30 phút) cho 1 bác sĩ
    // vừa tạo — cùng format/status với WorkSchedulesController.CreateSchedule (đã xác nhận khớp với
    // logic đặt lịch), để Mobile/booking đọc được slot thật ngay từ đầu thay vì giờ khám giả.
    private async Task SeedInitialScheduleForNewDoctorAsync(int doctorId)
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            var startTime = new TimeSpan(8, 0, 0);
            var endTime = new TimeSpan(17, 0, 0);
            var slotDuration = TimeSpan.FromMinutes(30);
            var today = DateTime.Today;

            for (int dayOffset = 0; dayOffset < 7; dayOffset++)
            {
                var workDate = today.AddDays(dayOffset);
                if (workDate.DayOfWeek == DayOfWeek.Sunday) continue; // Thứ Hai-Thứ Bảy, giống WorkingDaysText mặc định

                int scheduleId;
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = @"
                        INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time, status, created_at, updated_at)
                        VALUES (@docId, @wDate, @sTime, @eTime, 'Available', NOW(), NOW())
                        RETURNING schedule_id;";
                    var p1 = cmd.CreateParameter(); p1.ParameterName = "@docId"; p1.Value = doctorId; cmd.Parameters.Add(p1);
                    var p2 = cmd.CreateParameter(); p2.ParameterName = "@wDate"; p2.Value = workDate.Date; cmd.Parameters.Add(p2);
                    var p3 = cmd.CreateParameter(); p3.ParameterName = "@sTime"; p3.Value = startTime; cmd.Parameters.Add(p3);
                    var p4 = cmd.CreateParameter(); p4.ParameterName = "@eTime"; p4.Value = endTime; cmd.Parameters.Add(p4);
                    scheduleId = Convert.ToInt32(await cmd.ExecuteScalarAsync());
                }

                int slotOrder = 1;
                var currSlotStart = startTime;
                while (currSlotStart + slotDuration <= endTime)
                {
                    var currSlotEnd = currSlotStart + slotDuration;
                    using (var slotCmd = conn.CreateCommand())
                    {
                        slotCmd.CommandText = @"
                            INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status, created_at, updated_at)
                            VALUES (@schId, @sOrder, @sTime, @eTime, 'Available', NOW(), NOW());";
                        var sp1 = slotCmd.CreateParameter(); sp1.ParameterName = "@schId"; sp1.Value = scheduleId; slotCmd.Parameters.Add(sp1);
                        var sp2 = slotCmd.CreateParameter(); sp2.ParameterName = "@sOrder"; sp2.Value = slotOrder; slotCmd.Parameters.Add(sp2);
                        var sp3 = slotCmd.CreateParameter(); sp3.ParameterName = "@sTime"; sp3.Value = currSlotStart; slotCmd.Parameters.Add(sp3);
                        var sp4 = slotCmd.CreateParameter(); sp4.ParameterName = "@eTime"; sp4.Value = currSlotEnd; slotCmd.Parameters.Add(sp4);
                        await slotCmd.ExecuteNonQueryAsync();
                    }
                    slotOrder++;
                    currSlotStart = currSlotEnd;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SeedInitialScheduleForNewDoctorAsync warning (doctorId={doctorId}): {ex.Message}");
        }
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
}
