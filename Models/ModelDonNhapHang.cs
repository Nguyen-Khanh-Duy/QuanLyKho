using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("DonNhapHang")]
    public class ModelDonNhapHang
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int STT { get; set; }

        [Key]
        [Column(TypeName = "nvarchar(50)")]
        public string MaDonNhap { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaNCC { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(50)")]
        public string MaNguoiDung { get; set; }

        public DateTime NgayDatHang { get; set; } = DateTime.Now;

        [Column(TypeName = "nvarchar(50)")]
        public string TrangThai { get; set; }

        public decimal TongTien { get; set; } = 0;

        public DateTime NgayTao { get; set; } = DateTime.Now;

    }
}
