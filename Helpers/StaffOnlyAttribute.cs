using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace DTT_Backend_API.Helpers;

// Gắn ở mức class cho các controller/action mà bệnh nhân (role_id=3) tuyệt đối không
// được gọi (vd: quy trình chỉ định/trả kết quả Xét nghiệm-Siêu âm của Bác sĩ/KTV).
public class StaffOnlyAttribute : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext context)
    {
        if (!AccessControl.IsStaff(context.HttpContext.User))
        {
            // Dùng ObjectResult kèm body JSON thay vì ForbidResult() trần (body rỗng) — cùng lý do
            // với ControllerExtensions.ForbidJson(): app mobile/WinForms gọi .json() để đọc lỗi,
            // body rỗng sẽ khiến việc parse lỗi thất bại thay vì hiện đúng thông báo.
            context.Result = new ObjectResult(new { success = false, message = "Bạn không có quyền thực hiện thao tác này." })
            {
                StatusCode = 403
            };
        }
    }
}
