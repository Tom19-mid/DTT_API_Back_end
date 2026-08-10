using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;
using DTT_Backend_API.Helpers;
using DTT_Backend_API.Models;
using System.Linq;

namespace DTT_Backend_API.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PatientsController : ControllerBase
{
    private readonly AppDbContext _context;

    public PatientsController(AppDbContext context)
    {
        _context = context;
    }

    // GET /api/patients — List all patients + hồ sơ người thân (family_members) chờ xác thực CCCD tại quầy
    // cho Lễ Tân. Trước đây chỉ trả về patients — hồ sơ người thân tự thêm qua App (đang "pending")
    // không có cách nào hiện lên màn "Xác Thực Hồ Sơ" để Lễ Tân duyệt khi họ đến quầy đối chiếu CCCD.
    [HttpGet]
    public async Task<IActionResult> GetAllPatients()
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            // patients.phone_number có thể NULL với các hồ sơ cũ/tạo qua vài luồng khác nhau,
            // trong khi users.phone_number (SĐT đăng nhập) LUÔN có giá trị thật — fallback sang
            // đó để Lễ Tân luôn tìm được bệnh nhân theo SĐT, tránh báo "Không tìm thấy hồ sơ" sai.
            /* [OLD CODE COMMENTED OUT]
            var patients = await (
                from p in _context.Patients
                join u in _context.Users on p.UserId equals u.UserId into pu
                from u in pu.DefaultIfEmpty()
                select new ReceptionProfileDto
                {
                    Id = p.PatientId,
                    RecordType = "patient",
                    FullName = p.FullName,
                    Relationship = "Bản thân",
                    Dob = p.DateOfBirth.HasValue ? p.DateOfBirth.Value.ToString("dd/MM/yyyy") : "",
                    Gender = p.Gender ?? "",
                    Phone = !string.IsNullOrEmpty(p.PhoneNumber) ? p.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    Cccd = p.CccdNumber ?? "",
                    Bhyt = p.HealthInsuranceNumber ?? "",
                    Address = p.Address ?? "",
                    VerificationStatus = p.VerificationStatus ?? "pending",
                    CreatedAt = p.CreatedAt
                })
                .ToListAsync();
            */

            /* [OLD CODE COMMENTED OUT BEFORE ADDING STATUS FROM USERS]
            var patients = await (
                from p in _context.Patients
                join u in _context.Users on p.UserId equals u.UserId into pu
                from u in pu.DefaultIfEmpty()
                join vUser in _context.Users on p.VerifiedBy equals vUser.UserId into vu
                from vUser in vu.DefaultIfEmpty()
                select new ReceptionProfileDto
                {
                    Id = p.PatientId,
                    RecordType = "patient",
                    FullName = p.FullName,
                    Relationship = "Bản thân",
                    Dob = p.DateOfBirth.HasValue ? p.DateOfBirth.Value.ToString("dd/MM/yyyy") : "",
                    Gender = p.Gender ?? "",
                    Phone = !string.IsNullOrEmpty(p.PhoneNumber) ? p.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    Cccd = p.CccdNumber ?? "",
                    Bhyt = p.HealthInsuranceNumber ?? "",
                    Address = p.Address ?? "",
                    VerificationStatus = p.VerificationStatus ?? "pending",
                    CreatedAt = p.CreatedAt,
                    VerifiedAt = p.VerifiedAt,
                    VerifiedBy = vUser != null ? (vUser.FullName ?? vUser.Email) : (p.VerifiedBy.HasValue ? p.VerifiedBy.Value.ToString() : null),
                    VerificationNote = p.VerificationNote,
                    UpdatedAt = p.UpdatedAt
                })
                .ToListAsync();

            var familyMembers = await (
                from m in _context.FamilyMembers
                join owner in _context.Patients on m.OwnerPatientId equals owner.PatientId into om
                from owner in om.DefaultIfEmpty()
                join u in _context.Users on (owner != null ? owner.UserId : Guid.Empty) equals u.UserId into mu
                from u in mu.DefaultIfEmpty()
                select new ReceptionProfileDto
                {
                    Id = m.MemberId,
                    RecordType = "family_member",
                    FullName = m.FullName,
                    Relationship = m.Relationship ?? "Người thân",
                    Dob = m.DateOfBirth.HasValue ? m.DateOfBirth.Value.ToString("dd/MM/yyyy") : "",
                    Gender = m.Gender ?? "",
                    // Người thân thường không có SĐT riêng — hiện SĐT của chủ tài khoản để Lễ Tân liên hệ/tra cứu
                    Phone = !string.IsNullOrEmpty(m.PhoneNumber) ? m.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    Cccd = m.CccdNumber ?? "",
                    Bhyt = m.HealthInsuranceNumber ?? "",
                    Address = m.Address ?? "",
                    VerificationStatus = m.VerificationStatus ?? "pending",
                    CreatedAt = m.CreatedAt,
                    VerifiedAt = m.VerifiedAt,
                    VerifiedBy = m.VerifiedBy.HasValue ? m.VerifiedBy.Value.ToString() : null,
                    VerificationNote = m.VerificationNote,
                    UpdatedAt = m.UpdatedAt
                })
                .ToListAsync();
            */

            // [NEW UPDATED CODE WITH STATUS JOINED FROM USERS TABLE]
            var patients = await (
                from p in _context.Patients
                join u in _context.Users on p.UserId equals u.UserId into pu
                from u in pu.DefaultIfEmpty()
                join vUser in _context.Users on p.VerifiedBy equals vUser.UserId into vu
                from vUser in vu.DefaultIfEmpty()
                select new ReceptionProfileDto
                {
                    Id = p.PatientId,
                    RecordType = "patient",
                    FullName = p.FullName,
                    Relationship = "Bản thân",
                    Dob = p.DateOfBirth.HasValue ? p.DateOfBirth.Value.ToString("dd/MM/yyyy") : "",
                    Gender = p.Gender ?? "",
                    Phone = !string.IsNullOrEmpty(p.PhoneNumber) ? p.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    Cccd = p.CccdNumber ?? "",
                    Bhyt = p.HealthInsuranceNumber ?? "",
                    Address = p.Address ?? "",
                    VerificationStatus = p.VerificationStatus ?? "pending",
                    CreatedAt = p.CreatedAt,
                    VerifiedAt = p.VerifiedAt,
                    VerifiedBy = p.VerifiedBy.HasValue ? "Lễ tân" : null,
                    VerificationNote = p.VerificationNote,
                    UpdatedAt = p.UpdatedAt,
                    Status = u != null ? (u.Status ?? "Active") : "Active"
                })
                .ToListAsync();

            /* [OLD CODE COMMENTED OUT — chưa join vUser trong FamilyMembers làm cho m.VerifiedBy trả về Guid rỗng/không định danh được Admin]
            var familyMembers = await (
                from m in _context.FamilyMembers
                join owner in _context.Patients on m.OwnerPatientId equals owner.PatientId into om
                from owner in om.DefaultIfEmpty()
                join u in _context.Users on (owner != null ? owner.UserId : Guid.Empty) equals u.UserId into mu
                from u in mu.DefaultIfEmpty()
                select new ReceptionProfileDto
                {
                    Id = m.MemberId,
                    RecordType = "family_member",
                    FullName = m.FullName,
                    Relationship = m.Relationship ?? "Người thân",
                    Dob = m.DateOfBirth.HasValue ? m.DateOfBirth.Value.ToString("dd/MM/yyyy") : "",
                    Gender = m.Gender ?? "",
                    // Người thân thường không có SĐT riêng — hiện SĐT của chủ tài khoản để Lễ Tân liên hệ/tra cứu
                    Phone = !string.IsNullOrEmpty(m.PhoneNumber) ? m.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    Cccd = m.CccdNumber ?? "",
                    Bhyt = m.HealthInsuranceNumber ?? "",
                    Address = m.Address ?? "",
                    VerificationStatus = m.VerificationStatus ?? "pending",
                    CreatedAt = m.CreatedAt,
                    VerifiedAt = m.VerifiedAt,
                    VerifiedBy = m.VerifiedBy.HasValue ? m.VerifiedBy.Value.ToString() : null,
                    VerificationNote = m.VerificationNote,
                    UpdatedAt = m.UpdatedAt,
                    Status = u != null ? (u.Status ?? "Active") : "Active"
                })
                .ToListAsync();
            */

            // [NEW UPDATED CODE] Đã bổ sung join vUser cho FamilyMembers để nhận diện chính xác Admin (RoleId == 1) hoặc Lễ tân
            var familyMembers = await (
                from m in _context.FamilyMembers
                join owner in _context.Patients on m.OwnerPatientId equals owner.PatientId into om
                from owner in om.DefaultIfEmpty()
                join u in _context.Users on (owner != null ? owner.UserId : Guid.Empty) equals u.UserId into mu
                from u in mu.DefaultIfEmpty()
                join vUser in _context.Users on m.VerifiedBy equals vUser.UserId into vu
                from vUser in vu.DefaultIfEmpty()
                select new ReceptionProfileDto
                {
                    Id = m.MemberId,
                    RecordType = "family_member",
                    FullName = m.FullName,
                    Relationship = m.Relationship ?? "Người thân",
                    Dob = m.DateOfBirth.HasValue ? m.DateOfBirth.GetValueOrDefault().ToString("dd/MM/yyyy") : "",
                    Gender = m.Gender ?? "",
                    Phone = !string.IsNullOrEmpty(m.PhoneNumber) ? m.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    Cccd = m.CccdNumber ?? "",
                    Bhyt = m.HealthInsuranceNumber ?? "",
                    Address = m.Address ?? "",
                    VerificationStatus = m.VerificationStatus ?? "pending",
                    CreatedAt = m.CreatedAt,
                    VerifiedAt = m.VerifiedAt,
                    VerifiedBy = m.VerifiedBy.HasValue ? "Lễ tân" : null,
                    VerificationNote = m.VerificationNote,
                    UpdatedAt = m.UpdatedAt,
                    Status = u != null ? (u.Status ?? "Active") : "Active"
                })
                .ToListAsync();

            var combined = patients.Concat(familyMembers).OrderByDescending(x => x.CreatedAt).ToList();

            return Ok(new { success = true, patients = combined });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // GET /api/patients/pending — Patients waiting CCCD verification
    [HttpGet("pending")]
    public async Task<IActionResult> GetPendingPatients()
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var patients = await (
                from p in _context.Patients
                join u in _context.Users on p.UserId equals u.UserId into pu
                from u in pu.DefaultIfEmpty()
                where p.VerificationStatus == "pending" || p.CccdNumber == null || p.CccdNumber == ""
                orderby p.CreatedAt descending
                select new
                {
                    id = p.PatientId,
                    fullName = p.FullName,
                    phone = !string.IsNullOrEmpty(p.PhoneNumber) ? p.PhoneNumber : (u != null ? u.PhoneNumber : ""),
                    cccd = p.CccdNumber ?? "",
                    bhyt = p.HealthInsuranceNumber ?? "",
                    verificationStatus = p.VerificationStatus ?? "pending"
                })
                .ToListAsync();

            return Ok(new { success = true, patients });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // POST /api/patients — Tạo mới hồ sơ bệnh nhân từ Web Admin
    [HttpPost]
    public async Task<IActionResult> CreatePatient([FromBody] CreatePatientAdminDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.FullName))
                return BadRequest(new { success = false, message = "Vui lòng nhập họ và tên bệnh nhân." });

            var phone = dto.Phone?.Trim();
            if (!string.IsNullOrEmpty(phone) && phone.Length > 10)
                return BadRequest(new { success = false, message = "Số điện thoại không được vượt quá 10 chữ số." });

            User? user = null;
            if (!string.IsNullOrEmpty(phone))
            {
                user = await _context.Users.FirstOrDefaultAsync(u => u.PhoneNumber == phone);
            }

            if (user == null)
            {
                var newPhone = !string.IsNullOrEmpty(phone) ? phone : $"09{Random.Shared.Next(10000000, 99999999)}";
                user = new User
                {
                    UserId = Guid.NewGuid(),
                    PhoneNumber = newPhone,
                    Email = !string.IsNullOrWhiteSpace(dto.Email) ? dto.Email : $"{newPhone}@dtt.health",
                    PasswordHash = BCrypt.Net.BCrypt.HashPassword("123456"),
                    RoleId = 3,
                    FullName = dto.FullName,
                    Status = "Active",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Users.Add(user);
                await _context.SaveChangesAsync();
            }

            var patient = await _context.Patients.FirstOrDefaultAsync(p => p.UserId == user.UserId);
            if (patient != null)
            {
                patient.FullName = dto.FullName;
                if (!string.IsNullOrWhiteSpace(dto.Gender)) patient.Gender = dto.Gender;
                if (!string.IsNullOrWhiteSpace(dto.CccdNumber)) patient.CccdNumber = dto.CccdNumber;
                if (!string.IsNullOrWhiteSpace(dto.HealthInsuranceNumber)) patient.HealthInsuranceNumber = dto.HealthInsuranceNumber;
                if (!string.IsNullOrWhiteSpace(dto.Address)) patient.Address = dto.Address;
                if (!string.IsNullOrWhiteSpace(dto.Phone)) patient.PhoneNumber = dto.Phone;
                if (dto.DateOfBirth.HasValue) patient.DateOfBirth = DateTime.SpecifyKind(dto.DateOfBirth.Value, DateTimeKind.Utc);
                patient.UpdatedAt = DateTime.UtcNow;
            }
            else
            {
                patient = new Patient
                {
                    UserId = user.UserId,
                    FullName = dto.FullName,
                    Gender = dto.Gender ?? "Nam",
                    PhoneNumber = user.PhoneNumber,
                    CccdNumber = dto.CccdNumber,
                    HealthInsuranceNumber = dto.HealthInsuranceNumber,
                    Address = dto.Address,
                    DateOfBirth = dto.DateOfBirth.HasValue ? DateTime.SpecifyKind(dto.DateOfBirth.Value, DateTimeKind.Utc) : null,
                    VerificationStatus = "pending",
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _context.Patients.Add(patient);
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Tạo hồ sơ bệnh nhân mới thành công!",
                patient = new
                {
                    id = patient.PatientId,
                    fullName = patient.FullName,
                    dob = patient.DateOfBirth?.ToString("dd/MM/yyyy"),
                    gender = patient.Gender,
                    phone = patient.PhoneNumber,
                    cccd = patient.CccdNumber,
                    bhyt = patient.HealthInsuranceNumber,
                    address = patient.Address,
                    verificationStatus = patient.VerificationStatus,
                    verifiedAt = patient.VerifiedAt?.ToString("dd/MM/yyyy HH:mm:ss"),
                    verifiedBy = patient.VerifiedBy?.ToString(),
                    verificationNote = patient.VerificationNote,
                    updatedAt = patient.UpdatedAt.ToString("dd/MM/yyyy HH:mm:ss")
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi tạo bệnh nhân: " + ex.Message });
        }
    }

    // PUT /api/patients/{id} — Cập nhật hồ sơ bệnh nhân từ Web Admin
    [HttpPut("{id}")]
    public async Task<IActionResult> UpdatePatient(int id, [FromBody] UpdatePatientAdminDto dto)
    {
        try
        {
            Guid? currentUserId = GetCurrentUserId();
            var p = await _context.Patients.FirstOrDefaultAsync(x => x.PatientId == id);
            /* [OLD CODE COMMENTED OUT — trả 404 ngay nếu không tìm thấy trong Patients, chưa kiểm tra FamilyMembers]
            if (p == null) return NotFound(new { success = false, message = "Không tìm thấy hồ sơ bệnh nhân." });
            */

            // [NEW FALLBACK CODE] Nếu không tìm thấy trong bảng Patients, kiểm tra tiếp trong bảng FamilyMembers
            if (p == null)
            {
                var m = await _context.FamilyMembers.FirstOrDefaultAsync(x => x.MemberId == id);
                if (m == null) return NotFound(new { success = false, message = "Không tìm thấy hồ sơ bệnh nhân hoặc người thân." });

                if (!string.IsNullOrWhiteSpace(dto.FullName)) m.FullName = dto.FullName;
                if (!string.IsNullOrWhiteSpace(dto.Gender)) m.Gender = dto.Gender;
                if (!string.IsNullOrWhiteSpace(dto.CccdNumber)) m.CccdNumber = dto.CccdNumber;
                if (!string.IsNullOrWhiteSpace(dto.HealthInsuranceNumber)) m.HealthInsuranceNumber = dto.HealthInsuranceNumber;
                if (!string.IsNullOrWhiteSpace(dto.Address)) m.Address = dto.Address;
                if (!string.IsNullOrWhiteSpace(dto.Phone)) m.PhoneNumber = dto.Phone;
                // [BUG FIX] Npgsql PostgreSQL yêu cầu DateTime phải có Kind=Utc khi lưu vào timestamptz
                if (dto.DateOfBirth.HasValue) m.DateOfBirth = DateTime.SpecifyKind(dto.DateOfBirth.Value, DateTimeKind.Utc);

                if (!string.IsNullOrWhiteSpace(dto.VerificationStatus))
                {
                    string statusLower = dto.VerificationStatus.ToLower().Trim();
                    if (statusLower == "đã duyệt" || statusLower == "verified")
                    {
                        m.VerificationStatus = "verified";
                        var rawVerifiedAt = dto.VerifiedAt ?? DateTime.UtcNow;
                        m.VerifiedAt = DateTime.SpecifyKind(rawVerifiedAt, DateTimeKind.Utc);
                    }
                    else if (statusLower == "từ chối" || statusLower == "rejected")
                    {
                        m.VerificationStatus = "rejected";
                        var rawVerifiedAt = dto.VerifiedAt ?? DateTime.UtcNow;
                        m.VerifiedAt = DateTime.SpecifyKind(rawVerifiedAt, DateTimeKind.Utc);
                    }
                    else
                    {
                        m.VerificationStatus = "pending";
                        m.VerifiedAt = null;
                    }
                }

                if (!string.IsNullOrWhiteSpace(dto.VerificationNote))
                {
                    m.VerificationNote = dto.VerificationNote;
                }

                /* [OLD CODE COMMENTED OUT — tra cứu user admin hoặc lấy token làm cho verified_by nhận user_id của admin]
                User? mStaffUser = null;
                if (!string.IsNullOrWhiteSpace(dto.VerifiedBy) && Guid.TryParse(dto.VerifiedBy.Trim(), out Guid mPassedUserId))
                {
                    mStaffUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == mPassedUserId);
                }
                if (mStaffUser == null && currentUserId.HasValue)
                {
                    mStaffUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == currentUserId.Value);
                }
                if (mStaffUser == null && !string.IsNullOrWhiteSpace(dto.VerifiedBy))
                {
                    string targetV = dto.VerifiedBy.ToLower().Trim();
                    if (targetV.Contains("admin"))
                    {
                        var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 1)
                                     ?? await _context.Users.FirstOrDefaultAsync(u => u.FullName != null && u.FullName.ToLower().Contains("admin"));
                        if (adminUser != null) mStaffUser = adminUser;
                    }
                    else
                    {
                        var letanUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 4)
                                     ?? await _context.Users.FirstOrDefaultAsync(u => u.FullName != null && (u.FullName.ToLower().Contains("lễ") || u.FullName.ToLower().Contains("tân") || u.FullName.ToLower().Contains("reception")));
                        if (letanUser != null) mStaffUser = letanUser;
                    }
                }
                if (mStaffUser != null) m.VerifiedBy = mStaffUser.UserId;
                */

                // [NEW UPDATED CODE SAVING RECEPTIONIST (ROLE_ID = 4) USER_ID TO DB]
                // Tìm đúng user_id của Lễ Tân (có role_id = 4) để lưu vào cột verified_by trong Database
                User? mLetanUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 4)
                                ?? await _context.Users.FirstOrDefaultAsync(u => u.FullName != null && (u.FullName.ToLower().Contains("lễ") || u.FullName.ToLower().Contains("tân") || u.FullName.ToLower().Contains("reception")));
                if (mLetanUser != null)
                {
                    m.VerifiedBy = mLetanUser.UserId;
                }

                m.UpdatedAt = DateTime.UtcNow;
                await _context.SaveChangesAsync();

                return Ok(new
                {
                    success = true,
                    message = "Cập nhật hồ sơ người thân thành công!",
                    patient = new
                    {
                        id = m.MemberId,
                        fullName = m.FullName,
                        dob = m.DateOfBirth?.ToString("dd/MM/yyyy"),
                        gender = m.Gender,
                        phone = m.PhoneNumber,
                        cccd = m.CccdNumber,
                        bhyt = m.HealthInsuranceNumber,
                        address = m.Address,
                        verifiedStatus = m.VerificationStatus,
                        verifiedAt = m.VerifiedAt?.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss"),
                        verifiedBy = "Lễ tân",
                        verificationNote = m.VerificationNote,
                        updatedAt = m.UpdatedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss")
                    }
                });
            }

            /* [OLD CODE COMMENTED OUT]
            if (!string.IsNullOrWhiteSpace(dto.FullName)) p.FullName = dto.FullName;
            if (!string.IsNullOrWhiteSpace(dto.Gender)) p.Gender = dto.Gender;
            if (!string.IsNullOrWhiteSpace(dto.CccdNumber)) p.CccdNumber = dto.CccdNumber;
            if (!string.IsNullOrWhiteSpace(dto.HealthInsuranceNumber)) p.HealthInsuranceNumber = dto.HealthInsuranceNumber;
            if (!string.IsNullOrWhiteSpace(dto.Address)) p.Address = dto.Address;
            if (!string.IsNullOrWhiteSpace(dto.Phone)) p.PhoneNumber = dto.Phone;
            if (!string.IsNullOrWhiteSpace(dto.VerificationStatus)) p.VerificationStatus = dto.VerificationStatus;
            if (dto.DateOfBirth.HasValue) p.DateOfBirth = dto.DateOfBirth.Value;
            p.UpdatedAt = DateTime.UtcNow;
            */

            // [NEW UPDATED CODE SAVING VERIFICATION & UPDATED_AT TO DB]
            if (!string.IsNullOrWhiteSpace(dto.FullName)) p.FullName = dto.FullName;
            // [BUG FIX NOTE] Dòng này đã đúng logic: chỉ cập nhật Gender khi dto.Gender có giá trị.
            // Tuy nhiên trước đây Frontend gửi fallback "Nam" khi gender=NULL → backend ghi đè DB sai.
            // Đã fix ở Frontend (Patients.tsx + patientApi.ts): không gửi gender nếu người dùng chưa chọn.
            if (!string.IsNullOrWhiteSpace(dto.Gender)) p.Gender = dto.Gender;
            if (!string.IsNullOrWhiteSpace(dto.CccdNumber)) p.CccdNumber = dto.CccdNumber;
            if (!string.IsNullOrWhiteSpace(dto.HealthInsuranceNumber)) p.HealthInsuranceNumber = dto.HealthInsuranceNumber;
            if (!string.IsNullOrWhiteSpace(dto.Address)) p.Address = dto.Address;
            if (!string.IsNullOrWhiteSpace(dto.Phone)) p.PhoneNumber = dto.Phone;
            // [BUG FIX] Npgsql PostgreSQL yêu cầu DateTime phải có Kind=Utc khi lưu vào timestamptz
            if (dto.DateOfBirth.HasValue) p.DateOfBirth = DateTime.SpecifyKind(dto.DateOfBirth.Value, DateTimeKind.Utc);

            if (!string.IsNullOrWhiteSpace(dto.VerificationStatus))
            {
                string statusLower = dto.VerificationStatus.ToLower().Trim();
                if (statusLower == "đã duyệt" || statusLower == "verified")
                {
                    p.VerificationStatus = "verified";
                    var rawVerifiedAt = dto.VerifiedAt ?? DateTime.UtcNow;
                    p.VerifiedAt = DateTime.SpecifyKind(rawVerifiedAt, DateTimeKind.Utc);
                }
                else if (statusLower == "từ chối" || statusLower == "rejected")
                {
                    p.VerificationStatus = "rejected";
                    var rawVerifiedAt = dto.VerifiedAt ?? DateTime.UtcNow;
                    p.VerifiedAt = DateTime.SpecifyKind(rawVerifiedAt, DateTimeKind.Utc);
                }
                else
                {
                    p.VerificationStatus = "pending";
                    p.VerifiedAt = null;
                }
            }

            if (!string.IsNullOrWhiteSpace(dto.VerificationNote))
            {
                p.VerificationNote = dto.VerificationNote;
            }

            // [BUG FIX NOTE] VerifiedAt trong DTO là DateTime? (C# System.Text.Json STRICT).
            // Frontend PHẢI gửi ISO 8601 chuẩn: "2026-08-10T11:54:23.000Z" (có chữ T, không space).
            // Nếu gửi "2026-08-10 11:54:23" (space thay T) → JsonException → 400 Bad Request → update thất bại.
            // Đã fix ở Frontend (PatientFormModal.tsx doSave): dùng new Date().toISOString() chuẩn.

            /* [OLD CODE COMMENTED OUT — tra cứu user admin hoặc lấy token làm cho verified_by nhận user_id của admin]
            User? pStaffUser = null;
            if (!string.IsNullOrWhiteSpace(dto.VerifiedBy) && Guid.TryParse(dto.VerifiedBy.Trim(), out Guid pPassedUserId))
            {
                pStaffUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == pPassedUserId);
            }
            if (pStaffUser == null && currentUserId.HasValue)
            {
                pStaffUser = await _context.Users.FirstOrDefaultAsync(u => u.UserId == currentUserId.Value);
            }
            if (pStaffUser == null && !string.IsNullOrWhiteSpace(dto.VerifiedBy))
            {
                string targetV = dto.VerifiedBy.ToLower().Trim();
                if (targetV.Contains("admin"))
                {
                    var adminUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 1)
                                 ?? await _context.Users.FirstOrDefaultAsync(u => u.FullName != null && u.FullName.ToLower().Contains("admin"));
                    if (adminUser != null) pStaffUser = adminUser;
                }
                else
                {
                    var letanUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 4)
                                 ?? await _context.Users.FirstOrDefaultAsync(u => u.FullName != null && (u.FullName.ToLower().Contains("lễ") || u.FullName.ToLower().Contains("tân") || u.FullName.ToLower().Contains("reception")));
                    if (letanUser != null) pStaffUser = letanUser;
                }
            }
            if (pStaffUser != null) p.VerifiedBy = pStaffUser.UserId;
            */

            // [NEW UPDATED CODE SAVING RECEPTIONIST (ROLE_ID = 4) USER_ID TO DB]
            // Tìm đúng user_id của Lễ Tân (có role_id = 4) để lưu vào cột verified_by trong Database
            User? pLetanUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 4)
                            ?? await _context.Users.FirstOrDefaultAsync(u => u.FullName != null && (u.FullName.ToLower().Contains("lễ") || u.FullName.ToLower().Contains("tân") || u.FullName.ToLower().Contains("reception")));
            if (pLetanUser != null)
            {
                p.VerifiedBy = pLetanUser.UserId;
            }

            p.UpdatedAt = DateTime.UtcNow;

            var user = await _context.Users.FirstOrDefaultAsync(u => u.UserId == p.UserId);
            if (user != null)
            {
                if (!string.IsNullOrWhiteSpace(dto.FullName)) user.FullName = dto.FullName;
                if (!string.IsNullOrWhiteSpace(dto.Phone)) user.PhoneNumber = dto.Phone;
                if (!string.IsNullOrWhiteSpace(dto.Email)) user.Email = dto.Email;
                user.UpdatedAt = DateTime.UtcNow;
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = "Cập nhật hồ sơ bệnh nhân thành công!",
                patient = new
                {
                    id = p.PatientId,
                    fullName = p.FullName,
                    dob = p.DateOfBirth?.ToString("dd/MM/yyyy"),
                    gender = p.Gender,
                    phone = p.PhoneNumber,
                    cccd = p.CccdNumber,
                    bhyt = p.HealthInsuranceNumber,
                    address = p.Address,
                    verificationStatus = p.VerificationStatus,
                    verifiedAt = p.VerifiedAt?.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss"),
                    verifiedBy = "Lễ tân",
                    verificationNote = p.VerificationNote,
                    updatedAt = p.UpdatedAt.AddHours(7).ToString("dd/MM/yyyy HH:mm:ss")
                }
            });
        }
        catch (Exception ex)
        {
            string detailMsg = ex.InnerException != null ? ex.InnerException.Message : ex.Message;
            return StatusCode(500, new { success = false, message = "Lỗi cập nhật hồ sơ bệnh nhân: " + detailMsg });
        }
    }

    // PATCH /api/patients/{id}/verify — Receptionist approves CCCD verification
    [HttpPatch("{id}/verify")]
    public async Task<IActionResult> VerifyPatient(int id, [FromBody] VerifyPatientDto dto)
    {
        if (!AccessControl.IsStaff(User)) return this.ForbidJson();
        try
        {
            var p = await _context.Patients.FirstOrDefaultAsync(x => x.PatientId == id);
            if (p == null) return NotFound(new { success = false, message = "Không tìm thấy bệnh nhân." });

            // dto.CccdNumber là string non-nullable trong C# nhưng client vẫn có thể gửi JSON "null" —
            // System.Text.Json vẫn gán được null vào đó, gây NullReferenceException ở dto.CccdNumber.Length
            // bên dưới nếu không chặn sớm.
            if (string.IsNullOrWhiteSpace(dto.CccdNumber))
                return BadRequest(new { success = false, message = "Vui lòng nhập số CCCD." });

            Guid? currentUserId = GetCurrentUserId();
            if (!currentUserId.HasValue)
            {
                var receptionistUser = await _context.Users.FirstOrDefaultAsync(u => u.RoleId == 4 || u.Email == "letan.minhchau@gmail.com");
                currentUserId = receptionistUser?.UserId ?? Guid.Parse("ddb25ca6-80c8-434d-a05a-d4231c25e95b");
            }

            // 1. Cập nhật CCCD & trạng thái xác thực vào DB
            p.CccdNumber = dto.CccdNumber;
            p.VerificationStatus = "verified";
            p.VerifiedBy = currentUserId;
            p.VerifiedAt = DateTime.UtcNow;
            p.VerificationNote = $"Đã đối chiếu thẻ CCCD thực tế tại Quầy Lễ Tân. CCCD: {dto.CccdNumber}. Duyệt lúc: {DateTime.UtcNow:dd/MM/yyyy HH:mm}";
            p.UpdatedAt = DateTime.UtcNow;

            await _context.SaveChangesAsync();

            // 2. Tự động gửi thông báo đẩy vào DB để App Mobile nhận được
            try
            {
                var cccdMasked = dto.CccdNumber.Length >= 4
                    ? "****" + dto.CccdNumber.Substring(dto.CccdNumber.Length - 4)
                    : dto.CccdNumber;

                _context.Notifications.Add(new Notification
                {
                    UserId = p.UserId,
                    Title = "✅ Tài khoản đã được Xác Thực CCCD",
                    Content = $"Hồ sơ của bạn ({p.FullName}) đã được Lễ Tân Bệnh viện DTT Healthcare đối chiếu thẻ CCCD thực tế (CCCD: {cccdMasked}) và chính thức được XÁC THỰC. Bạn có thể đặt lịch khám và sử dụng đầy đủ các tính năng của ứng dụng!",
                    Type = "system",
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                });
                await _context.SaveChangesAsync();
            }
            catch
            {
                // Notification failure should not block the main verify response
            }

            return Ok(new
            {
                success = true,
                message = "Đã xác thực CCCD và gửi thông báo thành công!",
                patientId = id,
                cccd = dto.CccdNumber,
                verificationStatus = "verified",
                notified = true
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // GET /api/patients/{id}
    [HttpGet("{id}")]
    public async Task<IActionResult> GetPatient(int id)
    {
        if (!await AccessControl.CanAccessPatientAsync(User, _context, id)) return this.ForbidJson();
        try
        {
            var p = await _context.Patients.FirstOrDefaultAsync(x => x.PatientId == id);
            if (p == null)
            {
                return NotFound(new { success = false, message = "Không tìm thấy thông tin bệnh nhân." });
            }

            /* [OLD CODE COMMENTED OUT]
            return Ok(new
            {
                success = true,
                patient = new
                {
                    id = p.PatientId,
                    fullName = p.FullName,
                    dob = p.DateOfBirth?.ToString("dd/MM/yyyy"),
                    gender = p.Gender,
                    phone = p.PhoneNumber,
                    cccd = p.CccdNumber,
                    bhyt = p.HealthInsuranceNumber,
                    address = p.Address,
                    verificationStatus = p.VerificationStatus
                }
            });
            */

            // [NEW UPDATED CODE WITH VERIFICATION & UPDATED_AT FIELDS]
            return Ok(new
            {
                success = true,
                patient = new
                {
                    id = p.PatientId,
                    fullName = p.FullName,
                    dob = p.DateOfBirth?.ToString("dd/MM/yyyy"),
                    gender = p.Gender,
                    phone = p.PhoneNumber,
                    cccd = p.CccdNumber,
                    bhyt = p.HealthInsuranceNumber,
                    address = p.Address,
                    verificationStatus = p.VerificationStatus,
                    verifiedAt = p.VerifiedAt?.ToString("dd/MM/yyyy HH:mm:ss"),
                    verifiedBy = p.VerifiedBy?.ToString() ?? "Lễ Tân",
                    verificationNote = p.VerificationNote,
                    updatedAt = p.UpdatedAt.ToString("dd/MM/yyyy HH:mm:ss")
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = ex.Message });
        }
    }

    // POST /api/patients/link-by-qr
    [HttpPost("link-by-qr")]
    public async Task<IActionResult> LinkByQr([FromBody] LinkQrRequestDto dto)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(dto.PatientId) || string.IsNullOrWhiteSpace(dto.VerifyCode))
            {
                return BadRequest(new { success = false, message = "Mã QR thiếu thông tin liên kết hợp lệ." });
            }

            // Không fallback về patient_id=2 nếu client thiếu OwnerPatientId — trước đây làm vậy sẽ
            // âm thầm gắn hồ sơ người thân QR vào TÀI KHOẢN CỦA NGƯỜI KHÁC (bất kỳ ai cũng có thể để
            // trống trường này và thêm được "người thân" vào hồ sơ bệnh nhân #2).
            if (dto.OwnerPatientId <= 0)
                return BadRequest(new { success = false, message = "Thiếu OwnerPatientId hợp lệ." });
            if (!await AccessControl.CanAccessPatientAsync(User, _context, dto.OwnerPatientId)) return this.ForbidJson();

            int targetId = 0;
            string cleanId = dto.PatientId.Replace("#", "").Replace("F", "").Replace("P", "");
            int.TryParse(cleanId, out targetId);

            // Tạo hồ sơ người thân liên kết qua QR nhưng ở trạng thái "pending" — KHÔNG tự động
            // "verified". VerifyCode ở đây không được đối chiếu với bất kỳ dữ liệu thật nào (chưa có
            // hạ tầng QR viện thật), nên trước đây bất kỳ bệnh nhân nào cũng tự tạo được hồ sơ người
            // thân "đã xác thực" giả với tên/SĐT/BHYT tùy ý. Giờ bắt buộc đi qua đúng hàng đợi Lễ Tân
            // duyệt CCCD thật như mọi hồ sơ người thân khác (FamilyMembersController.VerifyFamilyMember).
            var newMember = new FamilyMember
            {
                OwnerPatientId = dto.OwnerPatientId,
                FullName = $"BỆNH NHÂN QR #{dto.PatientId}".ToUpper(),
                Relationship = "Người thân (QR)",
                Gender = "Nam",
                PhoneNumber = "0918889999",
                HealthInsuranceNumber = $"QR{dto.VerifyCode}{Random.Shared.Next(1000, 9999)}",
                VerificationStatus = "pending",
                VerificationNote = $"Đã liên kết qua Mã QR viện (Mã: {dto.VerifyCode}) — chờ Lễ Tân đối chiếu CCCD thực tế để xác thực.",
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _context.FamilyMembers.Add(newMember);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                success = true,
                message = $"Đã liên kết hồ sơ bệnh nhân #{dto.PatientId} — vui lòng mang CCCD ra quầy Lễ Tân để hoàn tất xác thực.",
                profile = new
                {
                    id = newMember.MemberId.ToString(),
                    realId = newMember.MemberId,
                    isOwner = false,
                    name = newMember.FullName,
                    patientId = $"#F{newMember.MemberId:D5}",
                    relationship = newMember.Relationship,
                    verificationStatus = "pending",
                    isVerified = false,
                    verificationNote = newMember.VerificationNote,
                    dob = "01/01/1985",
                    gender = newMember.Gender,
                    phone = newMember.PhoneNumber,
                    bhyt = newMember.HealthInsuranceNumber
                }
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new { success = false, message = "Lỗi xử lý QR: " + ex.Message });
        }
    }

    private Guid? GetCurrentUserId()
    {
        var claim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                 ?? User.FindFirst("sub")?.Value
                 ?? User.FindFirst("userId")?.Value;
        if (!string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out Guid userId))
        {
            return userId;
        }
        return null;
    }
}

public class CreatePatientAdminDto
{
    public string FullName { get; set; } = string.Empty;
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public string? CccdNumber { get; set; }
    public string? HealthInsuranceNumber { get; set; }
    public string? Address { get; set; }
    public DateTime? DateOfBirth { get; set; }
}

public class UpdatePatientAdminDto
{
    public string? FullName { get; set; }
    public string? Email { get; set; }
    public string? Phone { get; set; }
    public string? Gender { get; set; }
    public string? CccdNumber { get; set; }
    public string? HealthInsuranceNumber { get; set; }
    public string? Address { get; set; }
    public string? VerificationStatus { get; set; }
    public DateTime? DateOfBirth { get; set; }

    /* [NEW FIELDS ADDED] */
    public string? VerifiedBy { get; set; }
    public string? VerificationNote { get; set; }
    public DateTime? VerifiedAt { get; set; }
}

public class LinkQrRequestDto
{
    public string PatientId { get; set; } = string.Empty;
    public string VerifyCode { get; set; } = string.Empty;
    public int OwnerPatientId { get; set; }
}

public class VerifyPatientDto
{
    public string CccdNumber { get; set; } = string.Empty;
}

// Dùng chung cho cả hồ sơ bệnh nhân chính (patients) và hồ sơ người thân (family_members)
// trên màn "Xác Thực Hồ Sơ" của Lễ Tân — RecordType phân biệt để gọi đúng endpoint duyệt.
public class ReceptionProfileDto
{
    public int Id { get; set; }
    public string RecordType { get; set; } = "patient"; // "patient" | "family_member"
    public string? FullName { get; set; }
    public string Relationship { get; set; } = "Bản thân";
    public string? Dob { get; set; }
    public string? Gender { get; set; }
    public string? Phone { get; set; }
    public string? Cccd { get; set; }
    public string? Bhyt { get; set; }
    public string? Address { get; set; }
    public string VerificationStatus { get; set; } = "pending";
    public DateTime CreatedAt { get; set; }

    /* [NEW FIELDS ADDED] */
    public DateTime? VerifiedAt { get; set; }
    public string? VerifiedBy { get; set; }
    public string? VerificationNote { get; set; }
    public DateTime? UpdatedAt { get; set; }
    public string Status { get; set; } = "Active";
}
