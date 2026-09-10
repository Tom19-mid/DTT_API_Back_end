using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DTT_Backend_API.Helpers;

// Gắn cho các action có thể LEO THANG ĐẶC QUYỀN (tạo/đổi RoleId của tài khoản khác) — khác
// [StaffOnly] (chỉ loại trừ Bệnh nhân, mọi nhân viên khác đều qua được), attribute này CHỈ cho phép
// đúng role_id=1 (Admin). Xem AccessControl.IsAdmin để biết lý do cần tách riêng.
public class AdminOnlyAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!AccessControl.IsAdmin(context.HttpContext.User))
        {
            context.Result = new ObjectResult(new { success = false, message = "Chỉ Quản trị viên (Admin) mới có quyền thực hiện thao tác này." })
            {
                StatusCode = 403
            };
        }
    }
}
