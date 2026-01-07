using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace qlthucung.Models
{
    public class SanPhamListViewModel
    {
        [NotMapped]
        public List<SanPham> SanPhams { get; set; } = new List<SanPham>();
    }
}
