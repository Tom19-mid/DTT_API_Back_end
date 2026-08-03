using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using DTT_Backend_API.Data;

namespace DTT_Backend_API.Helpers;

// Kiểm tra quyền truy cập dữ liệu bệnh nhân: role_id=3 (Patient) chỉ được đọc/sửa
// đúng hồ sơ của chính mình (chống IDOR); các role nhân viên y tế (Admin/Doctor/
// Receptionist/Nurse/LabTech/Pharmacist) được truy cập mọi hồ sơ theo đúng nghiệp vụ
// khám chữa bệnh bình thường.
public static class AccessControl
{
    public static async Task<bool> CanAccessPatientAsync(ClaimsPrincipal principal, AppDbContext context, int patientId)
    {
        if (principal.FindFirst("role_id")?.Value != "3") return true;

        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!Guid.TryParse(userIdClaim, out var userId)) return false;

        return await context.Patients.AnyAsync(p => p.UserId == userId && p.PatientId == patientId);
    }

    // Dành cho các endpoint chỉ có số điện thoại (không có patientId) như đổi mật khẩu.
    public static bool IsSelfByPhone(ClaimsPrincipal principal, string phone)
    {
        if (principal.FindFirst("role_id")?.Value != "3") return true;
        return principal.FindFirst(ClaimTypes.MobilePhone)?.Value == phone;
    }

    // Dành cho endpoint chỉ dành riêng cho nhân viên y tế (vd: liệt kê toàn bộ bệnh nhân,
    // duyệt xác thực CCCD) — bệnh nhân (role_id=3) tuyệt đối không được gọi.
    public static bool IsStaff(ClaimsPrincipal principal)
    {
        return principal.FindFirst("role_id")?.Value != "3";
    }

    // Dành cho các bản ghi gắn trực tiếp với users.user_id (vd: notifications) thay vì patients.patient_id.
    public static bool CanAccessUserId(ClaimsPrincipal principal, Guid targetUserId)
    {
        if (principal.FindFirst("role_id")?.Value != "3") return true;
        var userIdClaim = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        return Guid.TryParse(userIdClaim, out var callerId) && callerId == targetUserId;
    }

    // Dành cho endpoint keyed theo appointmentId (hóa đơn/ước tính phí...) — tra ngược
    // patient_id của appointment rồi áp dụng đúng quy tắc CanAccessPatientAsync.
    public static async Task<bool> CanAccessAppointmentAsync(ClaimsPrincipal principal, AppDbContext context, int appointmentId)
    {
        if (principal.FindFirst("role_id")?.Value != "3") return true;
        var appt = await context.Appointments.FirstOrDefaultAsync(a => a.AppointmentId == appointmentId);
        if (appt == null) return true; // để controller tự trả 404 thay vì 403 gây hiểu nhầm
        return await CanAccessPatientAsync(principal, context, appt.PatientId);
    }
}
