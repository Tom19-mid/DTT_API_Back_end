using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Models;
using System.Globalization;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class FamilyMembersController : ControllerBase
{
    private readonly AppDbContext _context;

    public FamilyMembersController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/familymembers/patient/{patientId}
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientProfiles(int patientId)
    {
        try
        {
            var result = new List<ProfileResponseDto>();

            // 1. Get primary patient profile ("Bản thân")
            var owner = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == patientId);
            if (owner != null)
            {
                result.Add(new ProfileResponseDto
                {
                    Id = "owner_" + owner.PatientId,
                    RealId = owner.PatientId,
                    IsOwner = true,
                    Name = (owner.FullName ?? "Bệnh nhân").ToUpper(),
                    PatientId = $"#{owner.PatientId:D6}",
                    Relationship = "Bản thân",
                    VerificationStatus = owner.VerificationStatus ?? "verified",
                    IsVerified = (owner.VerificationStatus ?? "verified") == "verified",
                    VerificationNote = owner.VerificationNote,
                    Dob = owner.DateOfBirth?.ToString("dd/MM/yyyy") ?? "01/01/1990",
                    Gender = owner.Gender ?? "Nam",
                    Phone = owner.PhoneNumber ?? "",
                    Cccd = owner.CccdNumber ?? "",
                    Bhyt = owner.HealthInsuranceNumber ?? ""
                });
            }

            // 2. Get all family members from DB
            var members = await _context.FamilyMembers
                .Where(m => m.OwnerPatientId == patientId)
                .OrderBy(m => m.CreatedAt)
                .ToListAsync();

            foreach (var m in members)
            {
                result.Add(new ProfileResponseDto
                {
                    Id = m.MemberId.ToString(),
                    RealId = m.MemberId,
                    IsOwner = false,
                    Name = (m.FullName ?? "Người thân").ToUpper(),
                    PatientId = $"#F{m.MemberId:D5}",
                    Relationship = m.Relationship ?? "Khác",
                    VerificationStatus = m.VerificationStatus ?? "pending",
                    IsVerified = (m.VerificationStatus ?? "pending") == "verified",
                    VerificationNote = m.VerificationNote,
                    Dob = m.DateOfBirth?.ToString("dd/MM/yyyy") ?? "",
                    Gender = m.Gender ?? "Nam",
                    Phone = m.PhoneNumber ?? "",
                    Cccd = m.CccdNumber ?? "",
                    Bhyt = m.HealthInsuranceNumber ?? ""
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi lấy danh sách hồ sơ: " + ex.Message });
        }
    }

    // POST /api/familymembers
    [HttpPost]
    public async Task<IActionResult> CreateFamilyMember([FromBody] CreateFamilyMemberDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Họ tên không được để trống." });

            var member = new FamilyMember
            {
                OwnerPatientId = dto.OwnerPatientId,
                FullName = dto.Name.Trim().ToUpper(),
                Relationship = string.IsNullOrWhiteSpace(dto.Relationship) ? "Khác" : dto.Relationship.Trim(),
                Gender = dto.Gender ?? "Nam",
                PhoneNumber = dto.Phone,
                CccdNumber = dto.Cccd,
                HealthInsuranceNumber = dto.Bhyt,
                VerificationStatus = "pending",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (TryParseDate(dto.Dob, out var parsedDob))
            {
                member.DateOfBirth = parsedDob;
            }

            _context.FamilyMembers.Add(member);
            await _context.SaveChangesAsync();

            var response = new ProfileResponseDto
            {
                Id = member.MemberId.ToString(),
                RealId = member.MemberId,
                IsOwner = false,
                Name = member.FullName,
                PatientId = $"#F{member.MemberId:D5}",
                Relationship = member.Relationship,
                VerificationStatus = member.VerificationStatus,
                IsVerified = false,
                Dob = member.DateOfBirth?.ToString("dd/MM/yyyy") ?? "",
                Gender = member.Gender,
                Phone = member.PhoneNumber ?? "",
                Cccd = member.CccdNumber ?? "",
                Bhyt = member.HealthInsuranceNumber ?? ""
            };

            return Ok(new { success = true, message = "Thêm hồ sơ người thân thành công.", profile = response });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi thêm hồ sơ: " + ex.Message });
        }
    }

    // PUT /api/familymembers/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateFamilyMember(string id, [FromBody] UpdateFamilyMemberDto dto)
    {
        try
        {
            // Check if updating owner or member
            if (id.StartsWith("owner_") || dto.IsOwner)
            {
                int ownerId = dto.RealId > 0 ? dto.RealId : int.TryParse(id.Replace("owner_", ""), out int oid) ? oid : 0;
                var owner = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == ownerId);
                if (owner == null) return NotFound(new { success = false, message = "Không tìm thấy hồ sơ gốc." });

                if (!string.IsNullOrWhiteSpace(dto.Name)) owner.FullName = dto.Name.Trim().ToUpper();
                if (!string.IsNullOrWhiteSpace(dto.Gender)) owner.Gender = dto.Gender;
                if (!string.IsNullOrWhiteSpace(dto.Phone)) owner.PhoneNumber = dto.Phone;
                if (!string.IsNullOrWhiteSpace(dto.Cccd)) owner.CccdNumber = dto.Cccd;
                if (!string.IsNullOrWhiteSpace(dto.Bhyt)) owner.HealthInsuranceNumber = dto.Bhyt;
                if (TryParseDate(dto.Dob, out var parsedOwnerDob)) owner.DateOfBirth = parsedOwnerDob;
                owner.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Cập nhật hồ sơ gốc thành công." });
            }
            else
            {
                int memberId = dto.RealId > 0 ? dto.RealId : int.TryParse(id, out int mid) ? mid : 0;
                var member = await _context.FamilyMembers.FirstOrDefaultAsync(m => m.MemberId == memberId);
                if (member == null) return NotFound(new { success = false, message = "Không tìm thấy hồ sơ người thân." });

                if (!string.IsNullOrWhiteSpace(dto.Name)) member.FullName = dto.Name.Trim().ToUpper();
                if (!string.IsNullOrWhiteSpace(dto.Relationship)) member.Relationship = dto.Relationship.Trim();
                if (!string.IsNullOrWhiteSpace(dto.Gender)) member.Gender = dto.Gender;
                if (dto.Phone != null) member.PhoneNumber = dto.Phone;
                if (dto.Cccd != null) member.CccdNumber = dto.Cccd;
                if (dto.Bhyt != null) member.HealthInsuranceNumber = dto.Bhyt;
                if (TryParseDate(dto.Dob, out var parsedMemberDob)) member.DateOfBirth = parsedMemberDob;
                member.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Cập nhật hồ sơ người thân thành công." });
            }
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi cập nhật hồ sơ: " + ex.Message });
        }
    }

    // DELETE /api/familymembers/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteFamilyMember(string id)
    {
        try
        {
            if (id.StartsWith("owner_"))
                return BadRequest(new { success = false, message = "Không thể xóa hồ sơ gốc của tài khoản." });

            if (!int.TryParse(id, out int memberId))
                return BadRequest(new { success = false, message = "ID hồ sơ không hợp lệ." });

            var member = await _context.FamilyMembers.FirstOrDefaultAsync(m => m.MemberId == memberId);
            if (member == null)
                return NotFound(new { success = false, message = "Không tìm thấy hồ sơ để xóa." });

            _context.FamilyMembers.Remove(member);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Xóa hồ sơ người thân thành công." });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi xóa hồ sơ: " + ex.Message });
        }
    }

    private static bool TryParseDate(string? dateStr, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "dd-MM-yyyy" };
        if (DateTime.TryParseExact(dateStr.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out result))
            return true;

        return DateTime.TryParse(dateStr.Trim(), out result);
    }
}

public class ProfileResponseDto
{
    public string Id { get; set; } = string.Empty;
    public int RealId { get; set; }
    public bool IsOwner { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PatientId { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "pending";
    public bool IsVerified { get; set; }
    public string? VerificationNote { get; set; }
    public string? Dob { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Cccd { get; set; }
    public string? Bhyt { get; set; }
}

public class CreateFamilyMemberDto
{
    public int OwnerPatientId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Relationship { get; set; } = string.Empty;
    public string? Dob { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Cccd { get; set; }
    public string? Bhyt { get; set; }
}

public class UpdateFamilyMemberDto
{
    public int RealId { get; set; }
    public bool IsOwner { get; set; }
    public string? Name { get; set; }
    public string? Relationship { get; set; }
    public string? Dob { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Cccd { get; set; }
    public string? Bhyt { get; set; }
}
