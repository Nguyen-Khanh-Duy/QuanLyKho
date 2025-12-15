using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    [Table("NguoiDung")]
    public class ModelNguoiDung
    {
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int STT { get; set; }

        [Key]
        [Column(TypeName = "nvarchar(50)")]
        public string MaNguoiDung { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(255)")]
        public string HoTen { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(255)")]
        public string Email { get; set; }

        [Required]
        [Column(TypeName = "nvarchar(255)")]
        public string MatKhauHash { get; set; }

        [Column(TypeName = "nvarchar(50)")]
        public string VaiTro { get; set; }

        public DateTime NgayTao { get; set; } = DateTime.Now;

    }
}
