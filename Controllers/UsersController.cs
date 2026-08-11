using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.DTOs;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _context;

    public UsersController(AppDbContext context)
    {
        _context = context;
    }

    // Helper chuẩn hóa trạng thái về đúng constraint database (Active / Inactive / Locked)
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
        return "Active";
    }

    // GET /api/users — Lấy danh sách tất cả tài khoản
    [HttpGet]
    public async Task<IActionResult> GetAllUsers([FromQuery] int? roleId, [FromQuery] string? status, [FromQuery] string? search)
    {
        try
        {
            var query = _context.Users.AsQueryable();

            if (roleId.HasValue && roleId.Value > 0)
            {
                query = query.Where(u => u.RoleId == roleId.Value);
            }

            if (!string.IsNullOrWhiteSpace(status))
            {
                var s = status.Trim().ToLower();
                if (s == "active" || s == "đang hoạt động")
                {
                    query = query.Where(u => u.Status == "Active" || u.Status == "Đang hoạt động");
                }
                else if (s == "locked" || s == "đã khóa")
                {
                    query = query.Where(u => u.Status == "Locked" || u.Status == "Đã khóa");
                }
                else if (s == "inactive" || s == "ngưng hoạt động")
                {
                    query = query.Where(u => u.Status == "Inactive" || u.Status == "Ngưng hoạt động");
                }
                else
                {
                    query = query.Where(u => u.Status == status);
                }
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                var kw = search.Trim().ToLower();
                query = query.Where(u =>
                    u.PhoneNumber.Contains(kw) ||
                    (u.Email != null && u.Email.ToLower().Contains(kw)) ||
                    (u.FullName != null && u.FullName.ToLower().Contains(kw))
                );
            }

            var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();

            var roleIds = users.Select(u => u.RoleId).Distinct().ToList();
            var roles = await _context.Roles.Where(r => roleIds.Contains(r.RoleId)).ToDictionaryAsync(r => r.RoleId);

            var userIds = users.Select(u => u.UserId).ToList();
            var patients = await _context.Patients.Where(p => userIds.Contains(p.UserId)).ToDictionaryAsync(p => p.UserId);
            var doctors = await _context.Doctors.Where(d => userIds.Contains(d.UserId)).ToDictionaryAsync(d => d.UserId);

            var result = users.Select(u =>
            {
                roles.TryGetValue(u.RoleId, out var role);
                patients.TryGetValue(u.UserId, out var patient);
                doctors.TryGetValue(u.UserId, out var doctor);

                string fullName = !string.IsNullOrWhiteSpace(u.FullName)
                    ? u.FullName
                    : (patient?.FullName ?? doctor?.FullName ?? "Người dùng");

                string roleCode = role?.RoleCode ?? (u.RoleId == 1 ? "ADMIN" : u.RoleId == 2 ? "DOCTOR" : u.RoleId == 3 ? "PATIENT" : "STAFF");
                string roleName = role?.RoleName ?? (u.RoleId == 1 ? "Quản trị viên" : u.RoleId == 2 ? "Bác sĩ" : u.RoleId == 3 ? "Bệnh nhân" : "Nhân viên");

                return new UserDto
                {
                    UserId = u.UserId,
                    Phone = u.PhoneNumber,
                    Email = u.Email,
                    RoleId = u.RoleId,
                    RoleCode = roleCode,
                    RoleName = roleName,
                    FullName = fullName,
                    Status = u.Status,
                    CreatedAt = u.CreatedAt,
                    UpdatedAt = u.UpdatedAt
                };
            }).ToList();

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy danh sách tài khoản.", error = ex.Message });
        }
    }

    // GET /api/users/{id} — Lấy thông tin tài khoản chi tiết theo UserId
    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(Guid id)
    {
        try
        {
            var u = await _context.Users.FirstOrDefaultAsync(x => x.UserId == id);
            if (u == null)
            {
                return NotFound(new { message = "Không tìm thấy tài khoản." });
            }

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == u.RoleId);
            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == u.UserId);
            var doctor = await _context.Doctors.FirstOrDefaultAsync(d => d.UserId == u.UserId);

            string fullName = !string.IsNullOrWhiteSpace(u.FullName)
                ? u.FullName
                : (patient?.FullName ?? doctor?.FullName ?? "Người dùng");

            string roleCode = role?.RoleCode ?? (u.RoleId == 1 ? "ADMIN" : u.RoleId == 2 ? "DOCTOR" : u.RoleId == 3 ? "PATIENT" : "STAFF");
            string roleName = role?.RoleName ?? (u.RoleId == 1 ? "Quản trị viên" : u.RoleId == 2 ? "Bác sĩ" : u.RoleId == 3 ? "Bệnh nhân" : "Nhân viên");

            return Ok(new UserDto
            {
                UserId = u.UserId,
                Phone = u.PhoneNumber,
                Email = u.Email,
                RoleId = u.RoleId,
                RoleCode = roleCode,
                RoleName = roleName,
                FullName = fullName,
                Status = u.Status,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi lấy chi tiết tài khoản.", error = ex.Message });
        }
    }

    // POST /api/users — Tạo mới tài khoản người dùng
    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserDto dto)
    {
        if (!ModelState.IsValid) return BadRequest(ModelState);

        try
        {
            if (!string.IsNullOrWhiteSpace(dto.Phone) && dto.Phone.Trim().Length > 10)
            {
                return BadRequest(new { message = "Số điện thoại không được vượt quá 10 chữ số." });
            }

            var existingUser = await _context.Users.AnyAsync(u => u.PhoneNumber == dto.Phone || (!string.IsNullOrEmpty(dto.Email) && u.Email == dto.Email));
            if (existingUser)
            {
                return BadRequest(new { message = "Số điện thoại hoặc Email đã được sử dụng." });
            }

            string password = !string.IsNullOrWhiteSpace(dto.Password) ? dto.Password : "123456";
            var hashedPassword = BCrypt.Net.BCrypt.HashPassword(password);

            var newUser = new User
            {
                UserId = Guid.NewGuid(),
                PhoneNumber = dto.Phone,
                Email = !string.IsNullOrWhiteSpace(dto.Email) ? dto.Email : $"{dto.Phone}@dtt.health",
                PasswordHash = hashedPassword,
                RoleId = dto.RoleId > 0 ? dto.RoleId : 3,
                FullName = dto.FullName,
                Status = "Active",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            // Cập nhật hồ sơ bệnh nhân/bác sĩ tương ứng nếu có
            if (newUser.RoleId == 3)
            {
                var p = await _context.Patients.FirstOrDefaultAsync(x => x.UserId == newUser.UserId);
                if (p != null)
                {
                    p.FullName = dto.FullName;
                    p.PhoneNumber = dto.Phone;
                }
                else
                {
                    _context.Patients.Add(new Patient
                    {
                        UserId = newUser.UserId,
                        FullName = dto.FullName,
                        PhoneNumber = dto.Phone,
                        CreatedAt = DateTime.UtcNow
                    });
                }
                await _context.SaveChangesAsync();
            }
            else if (newUser.RoleId == 2)
            {
                var d = await _context.Doctors.FirstOrDefaultAsync(x => x.UserId == newUser.UserId);
                if (d != null)
                {
                    d.FullName = dto.FullName;
                }
                else
                {
                    _context.Doctors.Add(new Doctor
                    {
                        UserId = newUser.UserId,
                        FullName = dto.FullName,
                        Degree = "Bác sĩ Chuyên khoa",
                        ExperienceYears = 5,
                        ClinicRoom = "Phòng 101",
                        SpecialtyId = 1,
                        Status = newUser.Status ?? "Active",
                        Rating = 5.0m,
                        ReviewCount = 0
                    });
                }
                await _context.SaveChangesAsync();
            }

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == newUser.RoleId);

            return Ok(new UserDto
            {
                UserId = newUser.UserId,
                Phone = newUser.PhoneNumber,
                Email = newUser.Email,
                RoleId = newUser.RoleId,
                RoleCode = role?.RoleCode ?? "USER",
                RoleName = role?.RoleName ?? "Người dùng",
                FullName = newUser.FullName ?? dto.FullName,
                Status = newUser.Status,
                CreatedAt = newUser.CreatedAt,
                UpdatedAt = newUser.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi tạo tài khoản mới.", error = ex.Message });
        }
    }

    // PUT /api/users/{id} — Cập nhật thông tin tài khoản
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(Guid id, [FromBody] UpdateUserDto dto)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(dto.Phone) && dto.Phone.Trim().Length > 10)
            {
                return BadRequest(new { message = "Số điện thoại không được vượt quá 10 chữ số." });
            }

            var u = await _context.Users.FirstOrDefaultAsync(x => x.UserId == id);
            if (u == null)
            {
                return NotFound(new { message = "Không tìm thấy tài khoản." });
            }

            if (!string.IsNullOrWhiteSpace(dto.Phone) && dto.Phone != u.PhoneNumber)
            {
                bool phoneExists = await _context.Users.AnyAsync(x => x.PhoneNumber == dto.Phone && x.UserId != id);
                if (phoneExists)
                {
                    return BadRequest(new { message = "Số điện thoại đã được tài khoản khác sử dụng." });
                }
                u.PhoneNumber = dto.Phone;
            }

            if (!string.IsNullOrWhiteSpace(dto.Email) && dto.Email != u.Email)
            {
                bool emailExists = await _context.Users.AnyAsync(x => x.Email == dto.Email && x.UserId != id);
                if (emailExists)
                {
                    return BadRequest(new { message = "Email đã được tài khoản khác sử dụng." });
                }
                u.Email = dto.Email;
            }

            if (dto.RoleId.HasValue && dto.RoleId.Value > 0)
            {
                u.RoleId = dto.RoleId.Value;
            }

            if (dto.FullName != null)
            {
                u.FullName = dto.FullName;
            }

            if (!string.IsNullOrWhiteSpace(dto.Status))
            {
                u.Status = NormalizeUserStatus(dto.Status);
            }

            u.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            // Cập nhật tên trong hồ sơ Bệnh nhân nếu có
            var p = await _context.Patients.FirstOrDefaultAsync(x => x.UserId == id);
            if (p != null && !string.IsNullOrWhiteSpace(dto.FullName))
            {
                p.FullName = dto.FullName;
                await _context.SaveChangesAsync();
            }

            var role = await _context.Roles.FirstOrDefaultAsync(r => r.RoleId == u.RoleId);

            return Ok(new UserDto
            {
                UserId = u.UserId,
                Phone = u.PhoneNumber,
                Email = u.Email,
                RoleId = u.RoleId,
                RoleCode = role?.RoleCode ?? "USER",
                RoleName = role?.RoleName ?? "Người dùng",
                FullName = u.FullName ?? "Người dùng",
                Status = u.Status,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi cập nhật thông tin tài khoản.", error = ex.Message });
        }
    }

    // PUT /api/users/{id}/status — Khóa / Kích hoạt tài khoản
    [HttpPut("{id}/status")]
    public async Task<IActionResult> UpdateUserStatus(Guid id, [FromBody] UpdateUserStatusDto dto)
    {
        try
        {
            var u = await _context.Users.FirstOrDefaultAsync(x => x.UserId == id);
            if (u == null)
            {
                return NotFound(new { message = "Không tìm thấy tài khoản." });
            }

            u.Status = NormalizeUserStatus(dto.Status);
            u.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = $"Đã cập nhật trạng thái tài khoản thành: {u.Status}", status = u.Status });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { message = "Lỗi khi cập nhật trạng thái tài khoản.", error = ex.Message });
        }
    }
}
