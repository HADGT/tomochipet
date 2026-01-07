using System.ComponentModel.DataAnnotations.Schema;
using System.ComponentModel.DataAnnotations;
using System;

namespace qlthucung.Models
{
    [Table("MoMoPayments")]
    public class MoMoPayment
    {
        [Key]
        public string PaymentId { get; set; }

        [Required]
        public int Madon { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal Amount { get; set; }

        [Required]
        [StringLength(100)]
        public string Magiaodich { get; set; }

        [Required]
        [StringLength(20)]
        public string Trangthaithanhtoan { get; set; }

        [StringLength(255)]
        public string Tinnhantrave { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        // Khóa ngoại tham chiếu đến bảng DonHang
        [ForeignKey("Madon")]
        public virtual DonHang DonHang { get; set; }
    }
}
