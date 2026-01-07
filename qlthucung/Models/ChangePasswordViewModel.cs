using System.ComponentModel.DataAnnotations;

namespace qlthucung.Models
{
    public class ChangePasswordViewModel
    {
        [Required]
        public string Email { get; set; }

        [Required]
        public string Token { get; set; }

        [Required(ErrorMessage = "Vui lòng nhập mật khẩu mới.")]
        [DataType(DataType.Password)]
        [StringLength(100, ErrorMessage = "Mật khẩu tối thiểu {2} ký tự.", MinimumLength = 6)]
        public string NewPassword { get; set; }

        [Required(ErrorMessage = "Vui lòng xác nhận mật khẩu mới.")]
        [DataType(DataType.Password)]
        [Compare("NewPassword", ErrorMessage = "Xác nhận mật khẩu không khớp.")]
        public string ConfirmPassword { get; set; }
    }
}
