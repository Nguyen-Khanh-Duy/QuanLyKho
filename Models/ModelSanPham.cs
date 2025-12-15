using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("SanPham")]
    public class ModelSanPham
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int STT { get; set; }
        [Key]
        [Column(TypeName = "nvarchar(50)")]
        public string MaSanPham { get; set; }

        [Required]
        public string TenSanPham { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaSKU { get; set; }

        public string LoaiSanPham { get; set; }
        public string DonViTinh { get; set; }
        public decimal GiaBan { get; set; }
        public decimal GiaNhap { get; set; }
        public int SoLuongNhap { get; set; }
        public int SoLuongXuat { get; set; } // New column added
        public DateTime NgayTao { get; set; } = DateTime.Now;
        public DateTime NgayCapNhat { get; set; } = DateTime.Now;
        public string? MaVach { get; set; }
    }
}
