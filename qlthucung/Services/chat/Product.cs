using qlthucung.Models;
using System.Collections.Generic;

namespace qlthucung.Services.chat
{
    public class Product
    {
        public string ProductUrl { get; set; }
        public int Id { get; set; }
        public string Name { get; set; }
        public decimal Price { get; set; }
        public string Type { get; set; }   // "SanPham" | "DichVu"
    }
}
