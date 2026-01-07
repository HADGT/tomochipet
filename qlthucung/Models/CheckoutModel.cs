using System;

namespace qlthucung.Models
{
    public class CheckoutModel
    {
        public string FullName { get; set; }        // Họ và tên
        public string Username { get; set; }        // Tên đăng nhập (hoặc số điện thoại)
        public string Email { get; set; }           // Email
        public string PhoneNumber { get; set; }     // Số điện thoại
        public DateTime BirthDate { get; set; }       // Ngày sinh
        public string Tinh { get; set; }            // Tỉnh/Thành phố
        public string Xa { get; set; }              // Xã/Phường
        public string SoNha { get; set; }           // Số nhà
        public string PaymentMethod { get; set; }   // Phương thức thanh toán
    }
}
