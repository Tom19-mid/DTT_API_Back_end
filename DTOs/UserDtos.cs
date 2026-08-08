using System.ComponentModel.DataAnnotations;

namespace DTT_Backend_API.DTOs;

public class UserDto
{
    public Guid UserId { get; set; }
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int RoleId { get; set; }
    public string RoleCode { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

public class CreateUserDto
{
    [Required(ErrorMessage = "Vui lòng nhập số điện thoại.")]
    [StringLength(10, ErrorMessage = "Số điện thoại không được vượt quá 10 chữ số.")]
    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public int RoleId { get; set; } = 3;
    public string FullName { get; set; } = string.Empty;
}

public class UpdateUserDto
{
    [StringLength(10, ErrorMessage = "Số điện thoại không được vượt quá 10 chữ số.")]
    public string? Phone { get; set; }

    public string? Email { get; set; }
    public int? RoleId { get; set; }
    public string? FullName { get; set; }
    public string? Status { get; set; }
}

public class UpdateUserStatusDto
{
    public string Status { get; set; } = "Active";
}
