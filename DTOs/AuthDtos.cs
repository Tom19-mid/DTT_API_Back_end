using System.ComponentModel.DataAnnotations;

namespace DTT_Backend_API.DTOs;

public class LoginRequestDto
{
    [Required(ErrorMessage = "Vui lòng nhập số điện thoại")]
    [RegularExpression(@"^(0[3|5|7|8|9][0-9]{8}|0[0-9]{9})$", ErrorMessage = "Số điện thoại phải có 10 chữ số và bắt đầu bằng số 0 (Ví dụ: 0901234567)")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự")]
    public string Password { get; set; } = string.Empty;
}

public class RegisterRequestDto
{
    [Required(ErrorMessage = "Vui lòng nhập họ và tên")]
    [MinLength(2, ErrorMessage = "Họ và tên phải có ít nhất 2 ký tự")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập số điện thoại")]
    [RegularExpression(@"^(0[3|5|7|8|9][0-9]{8}|0[0-9]{9})$", ErrorMessage = "Số điện thoại phải có 10 chữ số hợp lệ (Ví dụ: 0901234567)")]
    public string Phone { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập địa chỉ email")]
    [EmailAddress(ErrorMessage = "Định dạng email không hợp lệ (Ví dụ: ten@gmail.com)")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Vui lòng nhập mật khẩu")]
    [MinLength(6, ErrorMessage = "Mật khẩu phải có ít nhất 6 ký tự")]
    public string Password { get; set; } = string.Empty;
}

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public int PatientId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string VerificationStatus { get; set; } = "pending";
    public string? OtpCode { get; set; }
}

public class DoctorAuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public int DoctorId { get; set; }
    public string FullName { get; set; } = string.Empty;
    public string Degree { get; set; } = string.Empty;
    public string ClinicRoom { get; set; } = string.Empty;
    public int SpecialtyId { get; set; }
    public string SpecialtyName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
}
