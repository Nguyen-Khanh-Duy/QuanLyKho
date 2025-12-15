using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace QuanlykhoAPI.Models
{
    public class ChatHistory
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }   // ✅ Khóa chính tự tăng

        [Required]
        public string MaNguoiDung { get; set; }  // ✅ Mã người dùng (liên kết với bảng NguoiDung)

        [Required]
        public string Question { get; set; }  // ✅ Câu hỏi

        [Required]
        public string Response { get; set; }  // ✅ Trả lời từ AI

        public string? ContextUsed { get; set; }  // Ngữ cảnh (nếu có)

        public DateTime Timestamp { get; set; } = DateTime.Now;  // ✅ Tự động lưu thời gian
    }
}
