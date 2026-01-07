using System.Collections.Generic;

namespace qlthucung.Models
{
    public class Level2ProductsViewModel
    {
        public List<CategoryVm> Level2 { get; set; }
        public Dictionary<int, List<SanPham>> ProductsByL2 { get; set; }
    }
}
