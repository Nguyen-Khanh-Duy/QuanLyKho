using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("ChiTietDonNhap")]
    public class ModelChiTietDonNhap
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int STT { get; set; }

        [Key]
        [Column(TypeName = "nvarchar(50)")]
        public string MaChiTiet { get; set; } = Guid.NewGuid().ToString("N");

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaDonNhap { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaSanPham { get; set; }

        [Required]
        public int SoLuong { get; set; }

        [Required]
        public decimal GiaNhap { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public decimal ThanhTien { get; set; }
        public string? MaVach { get; set; }

    }
}