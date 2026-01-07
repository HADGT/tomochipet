using System.Collections.Generic;
using System.ComponentModel.DataAnnotations.Schema;

namespace qlthucung.Models
{
    public class DanhMucViewModel
    {
        [NotMapped]
        public List<string> ParentNames { get; set; }   // Danh sách ParentID (có thể null)
        public string SelectedParentName { get; set; }  // ParentID được chọn
        public IEnumerable<DanhMuc> DanhMucs { get; set; }
        public string SearchTerm { get; set; }
        public int CurrentPage { get; set; }
        public int TotalPages { get; set; }
    }

}
