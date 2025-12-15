using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("LichSuXuatNhap")]
    public class ModelLichSuXuatNhap
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int STT { get; set; }

        [Key]
        [Column(TypeName = "nvarchar(50)")]
        public string MaGiaoDich { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaSanPham { get; set; }

        [Column(TypeName = "nvarchar(50)")]
        public string LoaiGiaoDich { get; set; }

        [Required]
        public int SoLuong { get; set; }

        public DateTime NgayGiaoDich { get; set; } = DateTime.Now;

        [Column(TypeName = "nvarchar(255)")]
        public string GhiChu { get; set; }

    }
}
