using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net;
using DTT_Backend_API.Data;
using DTT_Backend_API.DTOs;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;
    private readonly IWebHostEnvironment _env;

    public AuthController(AppDbContext context, IConfiguration config, IWebHostEnvironment env)
    {
        _context = context;
        _config = config;
        _env = env;
    }

    [AllowAnonymous]
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var phone = dto.Phone?.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone || u.PhoneNumber == phone);
        if (user == null)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }
        // Verify password with BCrypt — không còn fallback so khớp plaintext, tránh mở lại
        // đúng dạng lỗ hổng vừa gỡ (nếu password_hash không phải bcrypt hợp lệ, coi là sai).
        // bool isValidPassword;
        // Kiểm tra trạng thái khóa tài khoản
        if (user.Status == "Locked" || user.Status == "Đã khóa")
        {
            return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ Quản trị viên." });
        }
        if (user.Status == "Inactive" || user.Status == "Ngưng hoạt động")
        {
            return Unauthorized(new { message = "Tài khoản của bạn đã ngưng hoạt động." });
        }

        // Verify password with BCrypt (hỗ trợ cả mật khẩu chưa mã hóa plaintext trong DB)
        bool isValidPassword = false;
        try
        {
            isValidPassword = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        }
        catch
        {
            isValidPassword = false;
        }

        if (!isValidPassword && user.PasswordHash == dto.Password)
        {
            isValidPassword = true;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            await _context.SaveChangesAsync();
        }

        if (!isValidPassword)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }

        if (user.RoleId != 3) // RoleId 3 = Patient (Mobile App only)
        {
            return Unauthorized(new { message = "Tài khoản này không có quyền truy cập App Bệnh nhân." });
        }

        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == user.UserId);

        var token = GenerateJwtToken(user);

        return Ok(new AuthResponseDto
        {
            Token = token,
            UserId = user.UserId,
            PatientId = patient?.PatientId ?? 0,
            FullName = patient?.FullName ?? "Bệnh nhân",
            Phone = user.PhoneNumber,
            Email = user.Email,
            VerificationStatus = patient?.VerificationStatus ?? "pending"
        });
    }

    [AllowAnonymous]
    [HttpPost("doctor-login")]
    public async Task<IActionResult> DoctorLogin([FromBody] LoginRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var phone = dto.Phone?.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone || u.PhoneNumber == phone);
        if (user == null)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }
        // bool isValidPassword;
        // Kiểm tra trạng thái khóa tài khoản
        if (user.Status == "Locked" || user.Status == "Đã khóa")
        {
            return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ Quản trị viên." });
        }
        if (user.Status == "Inactive" || user.Status == "Ngưng hoạt động")
        {
            return Unauthorized(new { message = "Tài khoản của bạn đã ngưng hoạt động." });
        }

        bool isValidPassword = false;
        try
        {
            isValidPassword = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        }
        catch
        {
            isValidPassword = false;
        }

        if (!isValidPassword && user.PasswordHash == dto.Password)
        {
            isValidPassword = true;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            await _context.SaveChangesAsync();
        }

        if (!isValidPassword)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.UserId);
        if (doctor == null && user.RoleId == 3) // RoleId 3 = Patient (Mobile App only)
        {
            return Unauthorized(new { message = "Tài khoản Bệnh nhân chỉ dành cho App Mobile." });
        }

        // Chỉ tự tạo hồ sơ bác sĩ "BS. Điều trị" khi user thực sự có RoleId=2 (Doctor).
        // Trước đây điều kiện này chạy cho MỌI role (Lễ tân, Điều dưỡng, KTV, Dược sĩ...) mỗi khi
        // họ đăng nhập vào app WinForms mà chưa có hồ sơ Doctor — tạo ra các bác sĩ giả (specialty_id=1,
        // Active) lẫn vào danh sách bác sĩ thật, khiến các luồng tự chọn bác sĩ theo chuyên khoa
        // (vd: đặt gói khám) có thể chọn trúng bác sĩ giả không có lịch làm việc → lỗi khi đặt lịch.
        if (doctor == null && user.RoleId == 2)
        {
            doctor = new Doctor
            {
                UserId = user.UserId,
                FullName = "BS. Điều trị",
                Degree = "Bác sĩ Chuyên khoa",
                ExperienceYears = 5,
                ClinicRoom = "Phòng 101",
                SpecialtyId = 1,
                Status = "Active"
            };
            _context.Doctors.Add(doctor);
            await _context.SaveChangesAsync();
        }

        var specialty = doctor?.SpecialtyId != null ? await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == doctor.SpecialtyId.Value) : null;

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == user.RoleId);
        string roleCode = role?.RoleCode ?? (user.RoleId == 1 ? "ADMIN" : user.RoleId == 3 ? "PATIENT" : user.RoleId == 4 ? "RECEPTIONIST" : user.RoleId == 5 ? "NURSE" : user.RoleId == 6 ? "LAB_TECH" : user.RoleId == 7 ? "PHARMACIST" : "DOCTOR");
        string roleName = role?.RoleName ?? (user.RoleId == 1 ? "Quản trị viên" : user.RoleId == 4 ? "Lễ tân tiếp đón" : user.RoleId == 5 ? "Điều dưỡng" : user.RoleId == 6 ? "Kỹ thuật viên CLS" : user.RoleId == 7 ? "Dược sĩ" : "Bác sĩ");

        return Ok(new DoctorAuthResponseDto
        {
            Token = GenerateJwtToken(user),
            UserId = user.UserId,
            DoctorId = doctor?.DoctorId ?? 0,
            RoleId = user.RoleId,
            RoleCode = roleCode,
            RoleName = roleName,
            // Ưu tiên users.full_name (add_staff_full_names.sql — dành cho role không có hồ sơ riêng
            // như Lễ tân/Điều dưỡng/KTV/Dược sĩ), rồi tới Doctors.FullName, cuối cùng mới fallback.
            FullName = (!string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : doctor?.FullName)
                ?? (roleName + " " + (user.PhoneNumber.Length > 4 ? user.PhoneNumber.Substring(user.PhoneNumber.Length - 4) : "")),
            Degree = doctor?.Degree ?? roleName,
            ClinicRoom = doctor?.ClinicRoom ?? "Quầy làm việc",
            SpecialtyId = doctor?.SpecialtyId ?? (user.RoleId == 2 ? 1 : 0),
            SpecialtyName = specialty?.SpecialtyName ?? (user.RoleId == 2 ? "Nội tổng quát" : string.Empty),
            Phone = user.PhoneNumber,
            Email = user.Email
        });
    }

    [AllowAnonymous]
    [HttpPost("admin-login")]
    public async Task<IActionResult> AdminLogin([FromBody] LoginRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var phone = dto.Phone?.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone || u.PhoneNumber == phone);
        if (user == null)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }

        // Kiểm tra trạng thái khóa tài khoản
        if (user.Status == "Locked" || user.Status == "Đã khóa")
        {
            return Unauthorized(new { message = "Tài khoản của bạn đã bị khóa. Vui lòng liên hệ Quản trị viên." });
        }
        if (user.Status == "Inactive" || user.Status == "Ngưng hoạt động")
        {
            return Unauthorized(new { message = "Tài khoản của bạn đã ngưng hoạt động." });
        }

        bool isValidPassword = false;
        try
        {
            isValidPassword = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        }
        catch
        {
            isValidPassword = false;
        }

        // Hỗ trợ kiểm tra mật khẩu chưa mã hóa (Plaintext) và tự động mã hóa nâng cấp sang BCrypt
        if (!isValidPassword && user.PasswordHash == dto.Password)
        {
            isValidPassword = true;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password);
            await _context.SaveChangesAsync();
        }

        if (!isValidPassword)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }

        if (user.RoleId != 1)
        {
            return Unauthorized(new { message = "Tài khoản này không có quyền truy cập trang Quản trị (Admin)." });
        }

        var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == user.RoleId);
        string roleCode = role?.RoleCode ?? "ADMIN";
        string roleName = role?.RoleName ?? "Quản trị viên";

        return Ok(new AdminAuthResponseDto
        {
            Token = GenerateJwtToken(user),
            UserId = user.UserId,
            RoleId = user.RoleId,
            RoleCode = roleCode,
            RoleName = roleName,
            FullName = !string.IsNullOrWhiteSpace(user.FullName) ? user.FullName : "Admin",
            Phone = user.PhoneNumber,
            Email = user.Email
        });
    }

    [AllowAnonymous]
    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var phone = dto.Phone?.Trim();
        var existingUser = await _context.Users.AnyAsync(u => u.PhoneNumber == dto.Phone || u.PhoneNumber == phone || u.Email == dto.Email);
        if (existingUser)
        {
            return BadRequest(new { message = "Số điện thoại hoặc Email đã được sử dụng." });
        }

        // 1. Ensure Roles table has entries (khớp với bảng roles thật: 1=Admin, 2=Doctor, 3=Patient)
        var roleCount = await _context.Roles.CountAsync();
        if (roleCount == 0)
        {
            _context.Roles.AddRange(
                new Role { RoleId = 1, RoleName = "Admin", Description = "Quản trị viên" },
                new Role { RoleId = 2, RoleName = "Doctor", Description = "Bác sĩ" },
                new Role { RoleId = 3, RoleName = "Patient", Description = "Bệnh nhân" }
            );
            await _context.SaveChangesAsync();
        }

        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        // 2. Save User account FIRST
        var user = new User
        {
            UserId = Guid.NewGuid(),
            PhoneNumber = dto.Phone?.Trim() ?? string.Empty,
            Email = dto.Email?.Trim() ?? string.Empty,
            PasswordHash = hashedPassword,
            RoleId = 3, // Role 3 = Patient (theo bảng roles thật: 1=Admin, 2=Doctor, 3=Patient)
            Status = "Active" // Must be Active to pass users_status_check constraint
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // 3. Save Patient profile SECOND linked to saved User
        // DB có trigger trg_create_profile_on_user_insert (AFTER INSERT ON users) tự động tạo sẵn 1
        // dòng patients rỗng cho user vừa tạo ở bước 2 — nên KHÔNG được insert thêm 1 dòng mới ở đây
        // (sẽ vi phạm patients_user_id_key, lỗi 500 với MỌI lần đăng ký). Phải tìm dòng trigger đã tạo
        // rồi UPDATE lại đúng thông tin thật của bệnh nhân, chỉ insert mới khi trigger chưa/không chạy.
        var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == user.UserId);
        if (patient != null)
        {
            patient.FullName = dto.FullName;
            patient.PhoneNumber = dto.Phone;
            patient.VerificationStatus = "pending";
        }
        else
        {
            patient = new Patient
            {
                UserId = user.UserId,
                FullName = dto.FullName,
                PhoneNumber = dto.Phone,
                VerificationStatus = "pending",
                CreatedAt = DateTime.UtcNow
            };
            _context.Patients.Add(patient);
        }
        await _context.SaveChangesAsync();

        var token = GenerateJwtToken(user);

        // 4. Generate & Log OTP for Registration
        var otp = new Random().Next(100000, 999999).ToString();
        _otpStore[user.PhoneNumber] = (otp, DateTime.UtcNow.AddMinutes(5));

        Console.WriteLine("\n╔═════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║ 🔑 [DEMO ĐỒ ÁN TỐT NGHIỆP] MÃ OTP ĐĂNG KÝ TÀI KHOẢN: {otp}        ║");
        Console.WriteLine($"║ 📱 Số điện thoại: {dto.Phone,-49} ║");
        Console.WriteLine($"║ ⏳ Thời gian hiệu lực: 5 phút (đến {DateTime.Now.AddMinutes(5):HH:mm:ss})                      ║");
        Console.WriteLine("╚═════════════════════════════════════════════════════════════════════╝\n");

        return Ok(new AuthResponseDto
        {
            Token = token,
            UserId = user.UserId,
            PatientId = patient.PatientId,
            FullName = patient.FullName,
            Phone = user.PhoneNumber,
            Email = user.Email,
            VerificationStatus = patient.VerificationStatus,
            // Chỉ trả mã OTP thật trong response ở môi trường Development (demo đồ án, tránh tốn phí SMS thật).
            // Ở Production, mã chỉ còn hiện trong console log server — không lộ qua API cho client.
            OtpCode = _env.IsDevelopment() ? otp : null
        });
    }

    private string GenerateJwtToken(User user)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["Jwt:SecretKey"] ?? "DTT_Healthcare_Super_Secret_Key_2026_Graduation_Project"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.UserId.ToString()),
            new Claim(ClaimTypes.MobilePhone, user.PhoneNumber),
            new Claim(ClaimTypes.Email, user.Email ?? string.Empty),
            new Claim("role_id", user.RoleId.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _config["Jwt:Issuer"] ?? "DTT_Backend",
            audience: _config["Jwt:Audience"] ?? "DTT_Patients_App",
            claims: claims,
            expires: DateTime.UtcNow.AddDays(30),
            signingCredentials: creds
        );

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    // ── In-memory OTP store (phone -> (otp, expiry)) ──────────────────────────
    // NOTE: In production, use Redis or DB-backed storage with proper TTL
    private static readonly Dictionary<string, (string Code, DateTime Expiry)> _otpStore = new();

    // POST /api/auth/send-otp — Tạo & gửi mã OTP (6 số) cho số điện thoại
    [AllowAnonymous]
    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Phone))
            return BadRequest(new { success = false, message = "Vui lòng nhập số điện thoại." });

        var cleanPhone = dto.Phone?.Trim();
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone || u.PhoneNumber.Trim() == cleanPhone);
        if (user == null)
            return NotFound(new { success = false, message = "Số điện thoại chưa được đăng ký trong hệ thống." });

        if (user.Status == "Locked" || user.Status == "Đã khóa")
            return BadRequest(new { success = false, message = "Tài khoản đã bị khóa, không thể gửi OTP." });
        if (user.Status == "Inactive" || user.Status == "Ngưng hoạt động")
            return BadRequest(new { success = false, message = "Tài khoản đã ngưng hoạt động, không thể gửi OTP." });

        // Generate 6-digit OTP
        var otp = new Random().Next(100000, 999999).ToString();
        _otpStore[user.PhoneNumber] = (otp, DateTime.UtcNow.AddMinutes(5));

        Console.WriteLine("\n╔═════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║ 🔑 [DEMO ĐỒ ÁN TỐT NGHIỆP] MÃ OTP BỆNH NHÂN: {otp}                  ║");
        Console.WriteLine($"║ 📱 Số điện thoại: {dto.Phone,-49} ║");
        Console.WriteLine($"║ ⏳ Thời gian hiệu lực: 5 phút (đến {DateTime.Now.AddMinutes(5):HH:mm:ss})                      ║");
        Console.WriteLine("╚═════════════════════════════════════════════════════════════════════╝\n");

        // In production: integrate SMS gateway (Twilio, ESMS.vn, etc.)
        // Demo đồ án: chỉ trả mã OTP trong response khi chạy ở Development, tránh tốn phí SMS thật.
        // Ở Production, mã chỉ còn hiện trong console log server phía trên — không lộ qua API cho client.
        return Ok(new
        {
            success = true,
            message = $"Mã OTP đã được gửi đến số {user.PhoneNumber}.",
            otpCode = _env.IsDevelopment() ? otp : null,
            expiresInSeconds = 300
        });
    }

    // POST /api/auth/verify-otp — Xác minh mã OTP (dùng cho đăng ký / quên mật khẩu)
    [AllowAnonymous]
    [HttpPost("verify-otp")]
    public async Task<IActionResult> VerifyOtp([FromBody] VerifyOtpDto dto)
    {
        if (!_otpStore.TryGetValue(dto.Phone, out var entry))
            return BadRequest(new { success = false, message = "Chưa có mã OTP cho số điện thoại này. Vui lòng gửi lại." });

        if (DateTime.UtcNow > entry.Expiry)
        {
            _otpStore.Remove(dto.Phone);
            return BadRequest(new { success = false, message = "Mã OTP đã hết hạn. Vui lòng gửi lại mã mới." });
        }

        if (entry.Code != dto.OtpCode)
            return BadRequest(new { success = false, message = "Mã OTP không chính xác. Vui lòng kiểm tra lại." });

        // Update user status & patient verification status in database upon OTP confirmation
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
        Patient? patient = null;
        if (user != null)
        {
            user.Status = "Active";
            patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == user.UserId);
            if (patient != null)
            {
                patient.VerificationStatus = "verified";
                patient.VerifiedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
        }

        var token = user != null ? GenerateJwtToken(user) : null;

        return Ok(new
        {
            success = true,
            message = "Xác minh mã OTP thành công.",
            token = token,
            userId = user?.UserId,
            patientId = patient?.PatientId ?? 0,
            fullName = patient?.FullName ?? "Bệnh nhân",
            phone = user?.PhoneNumber,
            email = user?.Email,
            verificationStatus = patient?.VerificationStatus ?? "verified"
        });
    }

    // POST /api/auth/reset-password — Đặt lại mật khẩu sau khi xác minh OTP
    [AllowAnonymous]
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        // 1. Verify OTP is still valid
        if (!_otpStore.TryGetValue(dto.Phone, out var entry))
            return BadRequest(new { success = false, message = "Phiên xác minh OTP không hợp lệ. Vui lòng thực hiện lại." });

        if (DateTime.UtcNow > entry.Expiry)
        {
            _otpStore.Remove(dto.Phone);
            return BadRequest(new { success = false, message = "Mã OTP đã hết hạn. Vui lòng gửi lại mã mới." });
        }

        if (entry.Code != dto.OtpCode)
            return BadRequest(new { success = false, message = "Mã OTP không chính xác." });

        // 2. Find user and reset password
        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
        if (user == null)
            return NotFound(new { success = false, message = "Không tìm thấy tài khoản." });

        if (string.IsNullOrWhiteSpace(dto.NewPassword) || dto.NewPassword.Length < 6)
            return BadRequest(new { success = false, message = "Mật khẩu mới phải có ít nhất 6 ký tự." });

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;
        await _context.SaveChangesAsync();

        // 3. Remove used OTP
        _otpStore.Remove(dto.Phone);

        return Ok(new { success = true, message = "Mật khẩu đã được đặt lại thành công. Vui lòng đăng nhập lại." });
    }

    // PUT /api/auth/profile — Cập nhật thông tin hồ sơ bệnh nhân
    [HttpPut("profile")]
    public async Task<IActionResult> UpdateProfile([FromBody] UpdateProfileDto dto)
    {
        try
        {
            if (!await AccessControl.CanAccessPatientAsync(User, _context, dto.PatientId))
                return this.ForbidJson();

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == dto.PatientId);
            if (patient == null)
                return NotFound(new { message = "Không tìm thấy hồ sơ bệnh nhân." });

            if (!string.IsNullOrWhiteSpace(dto.FullName)) patient.FullName = dto.FullName;
            if (!string.IsNullOrWhiteSpace(dto.Gender)) patient.Gender = dto.Gender;
            if (!string.IsNullOrWhiteSpace(dto.Address)) patient.Address = dto.Address;
            if (!string.IsNullOrWhiteSpace(dto.HealthInsuranceNumber)) patient.HealthInsuranceNumber = dto.HealthInsuranceNumber;
            if (dto.DateOfBirth.HasValue) patient.DateOfBirth = dto.DateOfBirth;
            patient.UpdatedAt = DateTime.UtcNow;

            // Update email in users table if provided
            if (!string.IsNullOrWhiteSpace(dto.Email))
            {
                var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == patient.UserId);
                if (user != null)
                {
                    user.Email = dto.Email;
                    user.UpdatedAt = DateTime.UtcNow;
                }
            }

            await _context.SaveChangesAsync();
            return Ok(new { success = true, message = "Cập nhật thông tin thành công.", fullName = patient.FullName });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi cập nhật: " + ex.Message });
        }
    }

    // POST /api/auth/change-password — Đổi mật khẩu
    [HttpPost("change-password")]
    public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
    {
        try
        {
            if (!AccessControl.IsSelfByPhone(User, dto.Phone))
                return this.ForbidJson();

            var phone = dto.Phone?.Trim();
            var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone || u.PhoneNumber == phone);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy tài khoản." });

            bool isCurrentValid = false;
            try
            {
                isCurrentValid = BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash);
            }
            catch
            {
                isCurrentValid = false;
            }

            if (!isCurrentValid && user.PasswordHash == dto.CurrentPassword)
            {
                isCurrentValid = true;
            }

            if (!isCurrentValid)
                return BadRequest(new { success = false, message = "Mật khẩu hiện tại không chính xác." });

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Đổi mật khẩu thành công." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi đổi mật khẩu: " + ex.Message });
        }
    }
}
