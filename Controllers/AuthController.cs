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
            Status = "Active"
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

        return Ok(new AuthResponseDto
        {
            Token = token,
            UserId = user.UserId,
            PatientId = patient.PatientId,
            FullName = patient.FullName,
            Phone = user.PhoneNumber,
            Email = user.Email,
            VerificationStatus = patient.VerificationStatus
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
}
