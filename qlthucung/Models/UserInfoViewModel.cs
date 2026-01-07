using System;

namespace qlthucung.Models
{
    public class UserInfoViewModel
    {
        public string FullName { get; set; }
        public string UserName { get; set; }
        public DateTime? BirthDate { get; set; }
        public string Email { get; set; }
        public bool EmailConfirmed { get; set; }
        public string PhoneNumber { get; set; }
        public bool PhoneNumberConfirmed { get; set; }
    }

}
