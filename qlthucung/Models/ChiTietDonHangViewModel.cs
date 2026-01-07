using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace qlthucung.Models
{
    public class ChiTietDonHangViewModel
    {
        public DonHang DonHang { get; set; }
        [NotMapped]
        public List<ChiTietDonHangItemViewModel> ChiTietDonHangs { get; set; }
        public KhachHang KhachHang { get; set; }
        public MoMoPayment? GiaoDich { get; set; }
    }
}
