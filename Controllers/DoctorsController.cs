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

                var existingDoc = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.UserId || (d.FullName != null && d.FullName == doc.Name));
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
            }

            await SeedDoctorSchedulesAsync();
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

        var result = doctors.Select(d => {
            var master = doctorMasterList.FirstOrDefault(m => d.FullName != null && d.FullName.Contains(m.Name.Substring(m.Name.LastIndexOf(' ') + 1)));
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
                SpecialtyName = specName ?? "Nội tổng quát",
                specialty = specName ?? "Nội tổng quát",
                FullName = !string.IsNullOrEmpty(d.FullName) ? d.FullName : (u?.FullName ?? "Bác sĩ DTT"),
                Degree = !string.IsNullOrEmpty(d.Degree) ? d.Degree : "Chuyên khoa Bác sĩ",
                d.ExperienceYears,
                ClinicRoom = !string.IsNullOrEmpty(d.ClinicRoom) ? d.ClinicRoom : "Phòng 101",
                d.Rating,
                d.ReviewCount,
                ratingAverage = d.Rating,
                totalReviews = d.ReviewCount,
                Phone = u?.PhoneNumber ?? "Chưa cập nhật",
                Email = u?.Email ?? "Chưa cập nhật",
                userEmail = u?.Email ?? "",
                WorkingDaysText = master?.WorkingDays ?? "Thứ Hai đến Thứ Bảy",
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
    [HttpPost]
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
                Degree = dto.Degree ?? "Bác sĩ Chuyên khoa",
                ExperienceYears = dto.ExperienceYears,
                ClinicRoom = dto.ClinicRoom ?? "Phòng 101",
                SpecialtyId = dto.SpecialtyId > 0 ? dto.SpecialtyId : 1,
                // Status = newUser.Status,
                Status = normDocStatus,
                AvatarUrl = inputAvatar,
                Rating = 5.0m,
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

            string specName = "Nội tổng quát";
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
    [HttpPut("{id}")]
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

            string specName = "Nội tổng quát";
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
    [HttpPut("{id}/status")]
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

    private static bool _schedulesSeeded = false;

    [HttpGet("schedules")]
    public async Task<IActionResult> GetDoctorSchedules([FromQuery] int? doctorId, [FromQuery] int? specialtyId, [FromQuery] string? dateStr)
    {
        if (!_schedulesSeeded)
        {
            await SeedDoctorSchedulesAsync();
            _schedulesSeeded = true;
        }

        /* [OLD CODE COMMENTED OUT — bộ lọc lọc mất bác sĩ tạo mới không có tiền tố BS./Bác sĩ]
        var doctors = await _context.Doctors
            .Where(d => d.FullName != null && (d.FullName.Contains("BS.") || d.FullName.Contains("Bác sĩ") || d.FullName.Contains("ThS.") || d.FullName.Contains("TS.")))
            .ToListAsync();
        */
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

        // Đọc lịch làm việc THẬT từ doctor_schedules/doctor_schedule_slots (trước đây hàm này tự sinh
        // isWorking/timeSlots giả bằng CheckDoctorWorkingDay/GenerateDoctorTimeSlots dựa trên doctorId/tên,
        // không liên quan gì tới dữ liệu DB — khiến Mobile/WinForms hiện lịch khám sai ngày/sai khung giờ
        // so với những gì Lễ Tân/Admin thấy trên Web).
        var doctorIds = doctors.Select(d => d.DoctorId).ToList();
        var scheduleByDoctor = new Dictionary<int, int>(); // doctorId -> schedule_id
        var scheduleStatus = new Dictionary<int, string>(); // schedule_id -> status
        var slotsBySchedule = new Dictionary<int, List<string>>(); // schedule_id -> ["HH:mm - HH:mm", ...]

        if (doctorIds.Count > 0)
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            var idsStr = string.Join(",", doctorIds);
            var scheduleIds = new List<int>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = $@"
                    SELECT schedule_id, doctor_id, status
                    FROM doctor_schedules
                    WHERE doctor_id IN ({idsStr}) AND work_date = @wDate";
                var p = cmd.CreateParameter(); p.ParameterName = "@wDate"; p.Value = targetDate.Date; cmd.Parameters.Add(p);
                using var reader = await cmd.ExecuteReaderAsync();
                while (await reader.ReadAsync())
                {
                    int schId = reader.GetInt32(0);
                    int docId = reader.GetInt32(1);
                    string status = reader.IsDBNull(2) ? "Available" : reader.GetString(2);
                    scheduleByDoctor[docId] = schId;
                    scheduleStatus[schId] = status;
                    scheduleIds.Add(schId);
                }
            }

            if (scheduleIds.Count > 0)
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
                    if (!slotsBySchedule.ContainsKey(schId)) slotsBySchedule[schId] = new List<string>();
                    slotsBySchedule[schId].Add($"{sTime.Hours:D2}:{sTime.Minutes:D2} - {eTime.Hours:D2}:{eTime.Minutes:D2}");
                }
            }
        }

        var list = new List<object>();

        foreach (var doc in doctors)
        {
            bool hasSchedule = scheduleByDoctor.TryGetValue(doc.DoctorId, out var scheduleId);
            string rawStatus = hasSchedule ? scheduleStatus.GetValueOrDefault(scheduleId, "Available") : "Unavailable";
            bool isWorking = hasSchedule && rawStatus != "Unavailable" && rawStatus != "Off" && rawStatus != "Không hoạt động";
            var timeSlots = isWorking && slotsBySchedule.TryGetValue(scheduleId, out var slots) ? slots.ToArray() : Array.Empty<string>();

            string shiftDescription = "Nghỉ phép (Off)";
            if (isWorking)
            {
                shiftDescription = timeSlots.Length > 0 ? "Đang nhận lịch khám" : "Đã kín lịch khám";
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

    private static TimeSpan GetTimeSpanValue(System.Data.Common.DbDataReader reader, int ordinal)
    {
        var value = reader.GetValue(ordinal);
        if (value is TimeSpan ts) return ts;
        if (value is DateTime dt) return dt.TimeOfDay;
        return TimeSpan.TryParse(value?.ToString(), out var parsed) ? parsed : TimeSpan.Zero;
    }

    private static bool CheckDoctorWorkingDay(string docName, int doctorId, DateTime date)
    {
        var dow = date.DayOfWeek;
        string name = docName ?? "";

        if (name.Contains("Nguyễn Văn A"))
            return dow == DayOfWeek.Monday || dow == DayOfWeek.Wednesday || dow == DayOfWeek.Friday || dow == DayOfWeek.Sunday;

        if (name.Contains("Trịnh Hoàng Minh"))
            return dow == DayOfWeek.Tuesday || dow == DayOfWeek.Thursday || dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;

        if (name.Contains("Lê Thị B") || name.Contains("Lê Hoàng Văn"))
            return dow == DayOfWeek.Tuesday || dow == DayOfWeek.Thursday || dow == DayOfWeek.Saturday;

        if (name.Contains("Nguyễn Mai Chi"))
            return dow == DayOfWeek.Monday || dow == DayOfWeek.Wednesday || dow == DayOfWeek.Friday || dow == DayOfWeek.Saturday;

        if (name.Contains("Trần Văn C"))
            return dow == DayOfWeek.Monday || dow == DayOfWeek.Tuesday || dow == DayOfWeek.Thursday || dow == DayOfWeek.Friday;

        if (name.Contains("Phạm Thị D"))
            return dow == DayOfWeek.Wednesday || dow == DayOfWeek.Friday || dow == DayOfWeek.Saturday || dow == DayOfWeek.Sunday;

        if (name.Contains("Đỗ Phương Hạnh"))
            return dow == DayOfWeek.Monday || dow == DayOfWeek.Wednesday || dow == DayOfWeek.Thursday || dow == DayOfWeek.Saturday;

        if (name.Contains("Phạm Tuấn Kiệt"))
            return dow == DayOfWeek.Tuesday || dow == DayOfWeek.Wednesday || dow == DayOfWeek.Friday || dow == DayOfWeek.Sunday;

        if (name.Contains("Vũ Bích Ngọc"))
            return dow == DayOfWeek.Monday || dow == DayOfWeek.Tuesday || dow == DayOfWeek.Friday || dow == DayOfWeek.Saturday;

        if (name.Contains("Hoàng Văn Long"))
            return dow >= DayOfWeek.Monday && dow <= DayOfWeek.Friday;

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

    private async Task SeedDoctorSchedulesAsync()
    {
        try
        {
            var conn = _context.Database.GetDbConnection();
            if (conn.State != ConnectionState.Open) await conn.OpenAsync();

            using (var checkCmd = conn.CreateCommand())
            {
                checkCmd.CommandText = "SELECT COUNT(*) FROM doctor_schedules;";
                var countObj = await checkCmd.ExecuteScalarAsync();
                if (countObj != null && Convert.ToInt32(countObj) >= 10)
                {
                    _schedulesSeeded = true;
                    return;
                }
            }

            var doctors = await _context.Doctors.ToListAsync();
            if (doctors.Count == 0) return;

            for (int dayOffset = 0; dayOffset < 14; dayOffset++)
            {
                var workDate = DateTime.Today.AddDays(dayOffset);

                foreach (var doc in doctors)
                {
                    bool isOnDuty = CheckDoctorWorkingDay(doc.FullName ?? "", doc.DoctorId, workDate);
                    if (isOnDuty)
                    {
                        using var insCmd = conn.CreateCommand();
                        insCmd.CommandText = @"
                            INSERT INTO doctor_schedules (doctor_id, work_date, start_time, end_time, status)
                            SELECT @docId, @workDate, '08:00:00'::time, '17:00:00'::time, 'Available'
                            WHERE NOT EXISTS (
                                SELECT 1 FROM doctor_schedules WHERE doctor_id = @docId AND work_date = @workDate
                            )
                            AND NOT EXISTS (
                                SELECT 1 FROM doctor_leaves 
                                WHERE doctor_id = @docId AND status = 'Approved' AND @workDate BETWEEN leave_start_date AND leave_end_date
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
                SELECT ds.schedule_id, 1, '07:30:00'::time, '08:00:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND (slot_order = 1 OR start_time = '07:30:00'::time));

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 2, '08:00:00'::time, '08:30:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND (slot_order = 2 OR start_time = '08:00:00'::time));

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 3, '08:30:00'::time, '09:00:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND (slot_order = 3 OR start_time = '08:30:00'::time));

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 4, '09:00:00'::time, '09:30:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND (slot_order = 4 OR start_time = '09:00:00'::time));

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 5, '13:30:00'::time, '14:00:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND (slot_order = 5 OR start_time = '13:30:00'::time));

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 6, '14:00:00'::time, '14:30:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND (slot_order = 6 OR start_time = '14:00:00'::time));

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 7, '15:30:00'::time, '16:00:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND (slot_order = 7 OR start_time = '15:30:00'::time));

                INSERT INTO doctor_schedule_slots (schedule_id, slot_order, start_time, end_time, status)
                SELECT ds.schedule_id, 8, '16:00:00'::time, '16:30:00'::time, 'Available'
                FROM doctor_schedules ds WHERE NOT EXISTS (SELECT 1 FROM doctor_schedule_slots dss WHERE dss.schedule_id = ds.schedule_id AND (slot_order = 8 OR start_time = '16:00:00'::time));";
            await slotCmd.ExecuteNonQueryAsync();
        }
        catch (Exception ex)
        {
            Console.WriteLine("SeedDoctorSchedulesAsync info: " + ex.Message);
        }
    }
}
