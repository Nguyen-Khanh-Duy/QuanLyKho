using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("DonXuatHang")]
    public class ModelDonXuatHang
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int STT { get; set; }

        [Key]
        [Column(TypeName = "nvarchar(50)")]
        public string MaDonXuat { get; set; } = string.Empty;

        public DateTime NgayXuatHang { get; set; } = DateTime.Now;

        [Column(TypeName = "nvarchar(50)")]
        public string TrangThai { get; set; }

        public decimal TongTien { get; set; } = 0;

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaNguoiDung { get; set; }

        public DateTime NgayTao { get; set; } = DateTime.Now;

        [Column(TypeName = "nvarchar(50)")]
        public string? MaKhachHang { get; set; }

        [Column(TypeName = "nvarchar(255)")]
        public string? TenKhachHang { get; set; }

        [Column(TypeName = "nvarchar(255)")]
        public string? DiaChi { get; set; }

        [Column(TypeName = "nvarchar(20)")]
        public string? SoDienThoai { get; set; }
        public decimal DiscountPercent { get; internal set; }
        public decimal VatPercent { get; internal set; }
    }
}
