using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;
using System.Globalization;
using System.Security.Claims;

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

    // Trả về user_id thật của nhân viên đang đăng nhập (từ JWT), thay vì đoán đại một tài khoản
    // lễ tân bất kỳ trong DB hay dùng GUID cố định khi không tìm thấy.
    private Guid? GetCurrentStaffUserId()
    {
        var claim = User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(claim, out var id) ? id : (Guid?)null;
    }

    // GET /api/familymembers/patient/{patientId}
    [HttpGet("patient/{patientId}")]
    public async Task<IActionResult> GetPatientProfiles(int patientId)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, patientId)) return this.ForbidJson();
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
                    Bhyt = owner.HealthInsuranceNumber ?? "",
                    Address = owner.Address ?? ""
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
                    Bhyt = m.HealthInsuranceNumber ?? "",
                    Address = m.Address ?? ""
                });
            }

            return Ok(result);
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { success = false, message = "Lỗi lấy danh sách hồ sơ: " + msg });
        }
    }

    // POST /api/familymembers
    [HttpPost]
    public async Task<IActionResult> CreateFamilyMember([FromBody] CreateFamilyMemberDto dto)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, dto.OwnerPatientId)) return this.ForbidJson();
        try
        {
            if (string.IsNullOrWhiteSpace(dto.Name))
                return BadRequest(new { success = false, message = "Họ tên không được để trống." });

            string verStatus = "pending";
            if (!string.IsNullOrWhiteSpace(dto.VerificationStatus))
            {
                var v = dto.VerificationStatus.Trim().ToLower();
                if (v == "đã duyệt" || v == "verified") verStatus = "verified";
                else if (v == "từ chối" || v == "rejected") verStatus = "rejected";
            }

            Guid? currentUserId = GetCurrentStaffUserId();
            if (verStatus == "verified" && currentUserId == null)
                return BadRequest(new { success = false, message = "Không xác định được nhân viên thực hiện duyệt hồ sơ." });

            var member = new FamilyMember
            {
                OwnerPatientId = dto.OwnerPatientId,
                FullName = dto.Name.Trim().ToUpper(),
                Relationship = string.IsNullOrWhiteSpace(dto.Relationship) ? "Khác" : dto.Relationship.Trim(),
                Gender = dto.Gender ?? "Nam",
                PhoneNumber = dto.Phone,
                CccdNumber = dto.Cccd,
                HealthInsuranceNumber = dto.Bhyt,
                Address = dto.Address,
                VerificationStatus = verStatus,
                VerifiedBy = verStatus == "verified" ? currentUserId : null,
                VerifiedAt = verStatus == "verified" ? DateTime.UtcNow : null,
                VerificationNote = verStatus == "verified" ? (dto.VerificationNote ?? $"Đã đối chiếu thẻ CCCD thực tế tại Quầy Lễ Tân. CCCD: {dto.Cccd}") : null,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (TryParseDate(dto.Dob, out var parsedDob))
            {
                member.DateOfBirth = parsedDob;
            }

            _context.FamilyMembers.Add(member);
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateException)
            {
                // Tự động đồng bộ lại PostgreSQL sequence cho member_id nếu bị lệch key
                try
                {
                    await _context.Database.ExecuteSqlRawAsync(
                        "SELECT setval(pg_get_serial_sequence('family_members', 'member_id'), coalesce(max(member_id), 0) + 1, false) FROM family_members;");
                    await _context.SaveChangesAsync();
                }
                catch
                {
                    throw;
                }
            }

            var response = new ProfileResponseDto
            {
                Id = member.MemberId.ToString(),
                RealId = member.MemberId,
                IsOwner = false,
                Name = member.FullName,
                PatientId = $"#F{member.MemberId:D5}",
                Relationship = member.Relationship,
                VerificationStatus = member.VerificationStatus,
                IsVerified = member.VerificationStatus == "verified",
                VerificationNote = member.VerificationNote,
                Dob = member.DateOfBirth?.ToString("dd/MM/yyyy") ?? "",
                Gender = member.Gender,
                Phone = member.PhoneNumber ?? "",
                Cccd = member.CccdNumber ?? "",
                Bhyt = member.HealthInsuranceNumber ?? "",
                Address = member.Address
            };

            return Ok(new { success = true, message = "Thêm hồ sơ người thân thành công.", profile = response });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { success = false, message = "Lỗi thêm hồ sơ: " + msg });
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
                if (!await AccessControl.CanAccessPatientAsync(User, _context, ownerId)) return this.ForbidJson();

                var owner = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == ownerId);
                if (owner == null) return NotFound(new { success = false, message = "Không tìm thấy hồ sơ gốc." });

                if (!string.IsNullOrWhiteSpace(dto.Name)) owner.FullName = dto.Name.Trim().ToUpper();
                if (!string.IsNullOrWhiteSpace(dto.Gender)) owner.Gender = dto.Gender;
                if (!string.IsNullOrWhiteSpace(dto.Phone)) owner.PhoneNumber = dto.Phone;
                if (!string.IsNullOrWhiteSpace(dto.Cccd)) owner.CccdNumber = dto.Cccd;
                if (!string.IsNullOrWhiteSpace(dto.Bhyt)) owner.HealthInsuranceNumber = dto.Bhyt;
                if (dto.Address != null) owner.Address = dto.Address;
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

                if (!await AccessControl.CanAccessPatientAsync(User, _context, member.OwnerPatientId)) return this.ForbidJson();

                if (!string.IsNullOrWhiteSpace(dto.Name)) member.FullName = dto.Name.Trim().ToUpper();
                if (!string.IsNullOrWhiteSpace(dto.Relationship)) member.Relationship = dto.Relationship.Trim();
                if (!string.IsNullOrWhiteSpace(dto.Gender)) member.Gender = dto.Gender;
                if (dto.Phone != null) member.PhoneNumber = dto.Phone;
                if (dto.Cccd != null) member.CccdNumber = dto.Cccd;
                if (dto.Bhyt != null) member.HealthInsuranceNumber = dto.Bhyt;
                if (dto.Address != null) member.Address = dto.Address;
                if (TryParseDate(dto.Dob, out var parsedMemberDob)) member.DateOfBirth = parsedMemberDob;

                if (!string.IsNullOrWhiteSpace(dto.VerificationStatus))
                {
                    var v = dto.VerificationStatus.Trim().ToLower();
                    if (v == "đã duyệt" || v == "verified")
                    {
                        member.VerificationStatus = "verified";
                        if (!member.VerifiedAt.HasValue) member.VerifiedAt = DateTime.UtcNow;
                        if (!member.VerifiedBy.HasValue)
                        {
                            var staffId = GetCurrentStaffUserId();
                            if (staffId == null)
                                return BadRequest(new { success = false, message = "Không xác định được nhân viên thực hiện duyệt hồ sơ." });
                            member.VerifiedBy = staffId;
                        }
                    }
                    else if (v == "từ chối" || v == "rejected")
                    {
                        member.VerificationStatus = "rejected";
                    }
                    else
                    {
                        member.VerificationStatus = "pending";
                    }
                }
                if (dto.VerificationNote != null) member.VerificationNote = dto.VerificationNote;
                member.UpdatedAt = DateTime.UtcNow;

                await _context.SaveChangesAsync();
                return Ok(new { success = true, message = "Cập nhật hồ sơ người thân thành công." });
            }
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { success = false, message = "Lỗi cập nhật hồ sơ: " + msg });
        }
    }

    // PATCH /api/familymembers/{id}/verify — Lễ Tân đối chiếu CCCD thực tế & duyệt hồ sơ người thân
    // (người thân không có tài khoản đăng nhập riêng, nên thông báo được gửi tới chủ tài khoản - owner)
    [HttpPatch("{id}/verify")]
    public async Task<IActionResult> VerifyFamilyMember(int id, [FromBody] VerifyPatientDto dto)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var member = await _context.FamilyMembers.FirstOrDefaultAsync(m => m.MemberId == id);
            if (member == null) return NotFound(new { success = false, message = "Không tìm thấy hồ sơ người thân." });

            // dto.CccdNumber là string non-nullable trong C# nhưng client vẫn có thể gửi JSON "null" —
            // System.Text.Json vẫn gán được null vào đó, gây NullReferenceException ở dto.CccdNumber.Length
            // bên dưới nếu không chặn sớm.
            if (string.IsNullOrWhiteSpace(dto.CccdNumber))
                return BadRequest(new { success = false, message = "Vui lòng nhập số CCCD." });

            var staffId = GetCurrentStaffUserId();
            if (staffId == null)
                return BadRequest(new { success = false, message = "Không xác định được nhân viên thực hiện duyệt hồ sơ." });
            Guid currentUserId = staffId.Value;

            member.CccdNumber = dto.CccdNumber;
            member.VerificationStatus = "verified";
            member.VerifiedBy = currentUserId;
            member.VerifiedAt = DateTime.UtcNow;
            member.VerificationNote = $"Đã đối chiếu thẻ CCCD thực tế tại Quầy Lễ Tân. CCCD: {dto.CccdNumber}. Duyệt lúc: {DateTime.UtcNow:dd/MM/yyyy HH:mm}";
            member.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var owner = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == member.OwnerPatientId);
            if (owner != null && owner.UserId != Guid.Empty)
            {
                var cccdMasked = dto.CccdNumber.Length >= 4 ? "****" + dto.CccdNumber.Substring(dto.CccdNumber.Length - 4) : dto.CccdNumber;
                _context.Notifications.Add(new Notification
                {
                    UserId = owner.UserId,
                    Title = "✅ Hồ Sơ Người Thân Đã Được Xác Thực CCCD",
                    Content = $"Hồ sơ người thân '{member.FullName}' ({member.Relationship}) trong tài khoản của bạn đã được Lễ Tân Bệnh viện DTT Healthcare đối chiếu thẻ CCCD thực tế (CCCD: {cccdMasked}) và chính thức được XÁC THỰC.",
                    Type = "system",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return Ok(new
            {
                success = true,
                message = "Đã xác thực CCCD hồ sơ người thân thành công!",
                memberId = id,
                cccd = dto.CccdNumber,
                verificationStatus = "verified"
            });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { success = false, message = "Lỗi xác thực: " + msg });
        }
    }

    // PATCH /api/familymembers/{id}/reject — Lễ Tân/Admin từ chối hồ sơ người thân
    // (CCCD không khớp, thông tin không hợp lệ...) — trước đây chỉ có thể "từ chối" gián tiếp
    // qua form Sửa chung, không có hành động riêng biệt như /verify.
    [HttpPatch("{id}/reject")]
    public async Task<IActionResult> RejectFamilyMember(int id, [FromBody] RejectFamilyMemberDto dto)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var member = await _context.FamilyMembers.FirstOrDefaultAsync(m => m.MemberId == id);
            if (member == null) return NotFound(new { success = false, message = "Không tìm thấy hồ sơ người thân." });

            var staffId = GetCurrentStaffUserId();
            if (staffId == null)
                return BadRequest(new { success = false, message = "Không xác định được nhân viên thực hiện từ chối hồ sơ." });

            member.VerificationStatus = "rejected";
            member.VerifiedBy = staffId;
            member.VerifiedAt = DateTime.UtcNow;
            member.VerificationNote = string.IsNullOrWhiteSpace(dto.Reason)
                ? $"Từ chối hồ sơ lúc: {DateTime.UtcNow:dd/MM/yyyy HH:mm}"
                : $"Từ chối: {dto.Reason} (lúc {DateTime.UtcNow:dd/MM/yyyy HH:mm})";
            member.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            var owner = await _context.Patients.FirstOrDefaultAsync(p => p.PatientId == member.OwnerPatientId);
            if (owner != null && owner.UserId != Guid.Empty)
            {
                _context.Notifications.Add(new Notification
                {
                    UserId = owner.UserId,
                    Title = "❌ Hồ Sơ Người Thân Bị Từ Chối Xác Thực",
                    Content = $"Hồ sơ người thân '{member.FullName}' ({member.Relationship}) trong tài khoản của bạn đã bị Lễ Tân từ chối xác thực." +
                              (string.IsNullOrWhiteSpace(dto.Reason) ? "" : $" Lý do: {dto.Reason}"),
                    Type = "system",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }

            return Ok(new { success = true, message = "Đã từ chối hồ sơ người thân.", memberId = id, verificationStatus = "rejected" });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { success = false, message = "Lỗi từ chối hồ sơ: " + msg });
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

            if (!await AccessControl.CanAccessPatientAsync(User, _context, member.OwnerPatientId)) return this.ForbidJson();

            _context.FamilyMembers.Remove(member);
            await _context.SaveChangesAsync();

            return Ok(new { success = true, message = "Xóa hồ sơ người thân thành công." });
        }
        catch (Exception ex)
        {
            var msg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { success = false, message = "Lỗi xóa hồ sơ: " + msg });
        }
    }

    private static bool TryParseDate(string? dateStr, out DateTime result)
    {
        result = default;
        if (string.IsNullOrWhiteSpace(dateStr)) return false;

        string[] formats = { "dd/MM/yyyy", "d/M/yyyy", "yyyy-MM-dd", "MM/dd/yyyy", "dd-MM-yyyy", "yyyy/MM/dd" };
        if (DateTime.TryParseExact(dateStr.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
        {
            result = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return true;
        }

        if (DateTime.TryParse(dateStr.Trim(), out dt))
        {
            result = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            return true;
        }
        return false;
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
    public string? Address { get; set; }
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
    public string? Address { get; set; }
    public string? VerificationStatus { get; set; }
    public string? VerificationNote { get; set; }
}

public class RejectFamilyMemberDto
{
    public string? Reason { get; set; }
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
    public string? Address { get; set; }
    public string? VerificationStatus { get; set; }
    public string? VerificationNote { get; set; }
}
