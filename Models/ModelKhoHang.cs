using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{ 
        [Table("KhoHang")]
        public class ModelKhoHang
        {
  
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int STT { get; set; }

            [Key]
            [Column(TypeName = "nvarchar(50)")]
            public string MaKho { get; set; }

            [Required]
            [Column(TypeName = "nvarchar(50)")]
            public string MaSanPham { get; set; }

            [Required]
            [Column(TypeName = "nvarchar(100)")]   // Thêm cột Tên sản phẩm
            public string TenSanPham { get; set; }
        [Required]
            public int SoLuongTon { get; set; } = 0;

            public DateTime NgayNhapGanNhat { get; set; } = DateTime.Now;

            public DateTime? NgayBanGanNhat { get; set; }

            public DateTime NgayTao { get; set; } = DateTime.Now;


            public int SoLuongSapHetHan { get; set; } = 0;
        }
}
