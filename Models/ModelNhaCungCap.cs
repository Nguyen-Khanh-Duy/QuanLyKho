using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("NhaCungCap")]
    public class ModelNhaCungCap
    {
      
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
            public int STT { get; set; }
            [Key]

           [Column(TypeName = "nvarchar(50)")]
            public string MaNCC { get; set; }

            [Required]
            [Column(TypeName = "nvarchar(255)")]
            public string TenNCC { get; set; }

            [Column(TypeName = "nvarchar(255)")]
            public string NguoiLienHe { get; set; }

            [Column(TypeName = "nvarchar(20)")]
            public string SoDienThoai { get; set; }

            [Column(TypeName = "nvarchar(255)")]
            public string Email { get; set; }

            [Column(TypeName = "nvarchar(255)")]
            public string DiaChi { get; set; }

            public DateTime NgayTao { get; set; } = DateTime.Now;
        
    }
}
