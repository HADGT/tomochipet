using System;

namespace qlthucung.Models
{
    public class NguoiDungViewModel
    {
        public string FullName { get; set; }
        public string UserName { get; set; }
        public DateTime BirthDate { get; set; }
        public string Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public bool LockoutEnabled { get; set; }
        public int AccessFailedCount { get; set; }
        public string Role { get; set; }
    }
}
