using System.ComponentModel.DataAnnotations;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace qlthucung.Models
{
    [Table("SPDichVu")]
    public class SPDichVu
    {
        [Key]
        [Required]
        public int DichVuID { get; set; }

        [Required]
        [StringLength(255)]
        public string TenDichVu { get; set; }

        [StringLength(255)]
        public string MoTa { get; set; }

        [Required]
        [Column(TypeName = "decimal(18,2)")]
        public decimal? Gia { get; set; }

        public int ThoiGianDuKien { get; set; }
    }
}
