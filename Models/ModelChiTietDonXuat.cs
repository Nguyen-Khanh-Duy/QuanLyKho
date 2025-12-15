using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("ChiTietDonXuat")]
    public class ModelChiTietDonXuat
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int STT { get; set; }

        [Key]
        [Column(TypeName = "nvarchar(50)")]
        public string MaChiTiet { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaDonXuat { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaSanPham { get; set; }

        [Required]
        public int SoLuong { get; set; }

        [Required]
        public decimal GiaBan { get; set; }

        [DatabaseGenerated(DatabaseGeneratedOption.Computed)]
        public decimal ThanhTien { get; set; }
    }
}
