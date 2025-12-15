using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("KhachHang")]
    public class ModelKhachHang
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int STT { get; set; }

        [Key]
        [Column(TypeName = "nvarchar(50)")]
        public string MaKhachHang { get; set; }

        [Required]
        public string TenKhachHang { get; set; }

        [Column(TypeName = "nvarchar(20)")]
        public string? SoDienThoai { get; set; }

        [Column(TypeName = "nvarchar(255)")]
        public string? DiaChi { get; set; }

        [Column(TypeName = "nvarchar(50)")]
        public string? MaDonHang { get; set; }

        public DateTime NgayTao { get; set; } = DateTime.Now;
    }
}
