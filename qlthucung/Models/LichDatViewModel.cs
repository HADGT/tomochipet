using System.Collections.Generic;
using System;
using System.ComponentModel.DataAnnotations.Schema;

namespace qlthucung.Models
{
    public class LichDatViewModel
    {
        public DateTime Ngay { get; set; }
        public int SoLuong { get; set; }
        [NotMapped]
        public List<DichVu> Dv { get; set; }
    }
}
