using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using BCrypt.Net;
using DTT_Backend_API.Data;
using DTT_Backend_API.DTOs;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _config;

    public AuthController(AppDbContext context, IConfiguration config)
    {
        _context = context;
        _config = config;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
        if (user == null)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }

        // Verify password with BCrypt
        bool isValidPassword = false;
        try
        {
            isValidPassword = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        }
        catch
        {
            isValidPassword = user.PasswordHash == dto.Password;
        }

        if (!isValidPassword)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
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

    [HttpPost("doctor-login")]
    public async Task<IActionResult> DoctorLogin([FromBody] LoginRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
        if (user == null)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }

        bool isValidPassword = false;
        try
        {
            isValidPassword = BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash);
        }
        catch
        {
            isValidPassword = user.PasswordHash == dto.Password;
        }

        if (!isValidPassword)
        {
            return Unauthorized(new { message = "Số điện thoại hoặc mật khẩu không chính xác." });
        }

        var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == user.UserId);
        if (doctor == null && user.RoleId != 2)
        {
            return Unauthorized(new { message = "Tài khoản của bạn không có quyền ra vào Không gian Bác sĩ." });
        }

        if (doctor == null)
        {
            // Auto-create basic doctor profile if RoleId == 2 but no Doctor entry found
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

        var specialty = doctor.SpecialtyId.HasValue ? await _context.Specialties.FirstOrDefaultAsync(s => s.SpecialtyId == doctor.SpecialtyId.Value) : null;

        return Ok(new DoctorAuthResponseDto
        {
            Token = GenerateJwtToken(user),
            UserId = user.UserId,
            DoctorId = doctor.DoctorId,
            FullName = doctor.FullName ?? "Bác sĩ",
            Degree = doctor.Degree ?? "Chuyên khoa",
            ClinicRoom = doctor.ClinicRoom ?? "Phòng 101",
            SpecialtyId = doctor.SpecialtyId ?? 1,
            SpecialtyName = specialty?.SpecialtyName ?? "Nội tổng quát",
            Phone = user.PhoneNumber,
            Email = user.Email
        });
    }

    [HttpPost("register")]
    public async Task<IActionResult> Register([FromBody] RegisterRequestDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        var existingUser = await _context.Users.AnyAsync(u => u.PhoneNumber == dto.Phone || u.Email == dto.Email);
        if (existingUser)
        {
            return BadRequest(new { message = "Số điện thoại hoặc Email đã được sử dụng." });
        }

        // 1. Ensure Roles table has entries
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

        var hashedPassword = BCrypt.Net.BCrypt.HashPassword(dto.Password);

        // 2. Save User account FIRST
        var user = new User
        {
            UserId = Guid.NewGuid(),
            PhoneNumber = dto.Phone,
            Email = dto.Email,
            PasswordHash = hashedPassword,
            RoleId = 1, // Role 1 = Patient
            Status = "Active" // Must be Active to pass users_status_check constraint
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        // 3. Save Patient profile SECOND linked to saved User
        var patient = new Patient
        {
            UserId = user.UserId,
            FullName = dto.FullName,
            PhoneNumber = dto.Phone,
            VerificationStatus = "pending",
            CreatedAt = DateTime.UtcNow
        };

        _context.Patients.Add(patient);
        await _context.SaveChangesAsync();

        var token = GenerateJwtToken(user);

        // 4. Generate & Log OTP for Registration
        var otp = new Random().Next(100000, 999999).ToString();
        _otpStore[dto.Phone] = (otp, DateTime.UtcNow.AddMinutes(5));

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
            OtpCode = otp
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
            new Claim(ClaimTypes.Email, user.Email),
            new Claim(ClaimTypes.Role, "Patient")
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
    [HttpPost("send-otp")]
    public async Task<IActionResult> SendOtp([FromBody] SendOtpDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Phone))
            return BadRequest(new { success = false, message = "Vui lòng nhập số điện thoại." });

        var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
        if (user == null)
            return NotFound(new { success = false, message = "Số điện thoại chưa được đăng ký trong hệ thống." });

        // Generate 6-digit OTP
        var otp = new Random().Next(100000, 999999).ToString();
        _otpStore[dto.Phone] = (otp, DateTime.UtcNow.AddMinutes(5));

        Console.WriteLine("\n╔═════════════════════════════════════════════════════════════════════╗");
        Console.WriteLine($"║ 🔑 [DEMO ĐỒ ÁN TỐT NGHIỆP] MÃ OTP BỆNH NHÂN: {otp}                  ║");
        Console.WriteLine($"║ 📱 Số điện thoại: {dto.Phone,-49} ║");
        Console.WriteLine($"║ ⏳ Thời gian hiệu lực: 5 phút (đến {DateTime.Now.AddMinutes(5):HH:mm:ss})                      ║");
        Console.WriteLine("╚═════════════════════════════════════════════════════════════════════╝\n");

        // In production: integrate SMS gateway (Twilio, ESMS.vn, etc.)
        // For now: return OTP in response body (dev/demo mode only)
        return Ok(new
        {
            success = true,
            message = $"Mã OTP đã được gửi đến số {dto.Phone}.",
            otpCode = otp, // REMOVE in production!
            expiresInSeconds = 300
        });
    }

    // POST /api/auth/verify-otp — Xác minh mã OTP (dùng cho đăng ký / quên mật khẩu)
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
        if (user != null)
        {
            user.Status = "Active";
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == user.UserId);
            if (patient != null)
            {
                patient.VerificationStatus = "verified";
                patient.VerifiedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();
        }

        return Ok(new { success = true, message = "Xác minh OTP thành công." });
    }

    // POST /api/auth/reset-password — Đặt lại mật khẩu sau khi xác minh OTP
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
            var user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == dto.Phone);
            if (user == null)
                return NotFound(new { message = "Không tìm thấy tài khoản." });

            bool isCurrentValid = false;
            try { isCurrentValid = BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash); }
            catch { isCurrentValid = user.PasswordHash == dto.CurrentPassword; }

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

public class UpdateProfileDto
{
    public int PatientId { get; set; }
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Gender { get; set; }
    public string? Address { get; set; }
    public string? HealthInsuranceNumber { get; set; }
    public DateTime? DateOfBirth { get; set; }
}

public class ChangePasswordDto
{
    public string Phone { get; set; } = string.Empty;
    public string CurrentPassword { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}

public class SendOtpDto
{
    public string Phone { get; set; } = string.Empty;
}

public class VerifyOtpDto
{
    public string Phone { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
}

public class ResetPasswordDto
{
    public string Phone { get; set; } = string.Empty;
    public string OtpCode { get; set; } = string.Empty;
    public string NewPassword { get; set; } = string.Empty;
}
