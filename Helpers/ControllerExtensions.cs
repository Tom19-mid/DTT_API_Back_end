using Microsoft.AspNetCore.Mvc;

namespace DTT_Backend_API.Helpers;

// Mọi controller trong dự án luôn trả JSON body kèm { message: "..." } cho lỗi (NotFound/BadRequest/
// Unauthorized...) NGOẠI TRỪ Forbid() mặc định của ASP.NET Core, vốn trả 403 với body HOÀN TOÀN RỖNG.
// Điều này khiến app mobile (gọi response.json() để đọc lỗi) và WinForms parse thất bại/nhận thông
// báo trống thay vì lỗi rõ ràng. Dùng ForbidJson() thay cho Forbid() để nhất quán 403 cũng có body.
public static class ControllerExtensions
{
    public static IActionResult ForbidJson(this ControllerBase controller, string message = "Bạn không có quyền thực hiện thao tác này.")
    {
        return controller.StatusCode(403, new { success = false, message });
    }
}
