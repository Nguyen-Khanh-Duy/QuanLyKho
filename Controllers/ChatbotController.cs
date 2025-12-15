using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;
using Quanlykhohang.Services;

namespace Quanlykhohang.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChatbotController : ControllerBase
    {
        private static Dictionary<string, List<string>> _userConversations = new();
        private readonly AIService _ai;
        private readonly AppDbContext _context;

        public ChatbotController(AIService ai, AppDbContext context)
        {
            _ai = ai;
            _context = context;
        }

        // ==========================
        // 🧠 Gửi tin nhắn tới AI
        // ==========================
        [HttpPost("Ask")]
        public async Task<IActionResult> Ask([FromBody] ChatMessageRequest req)
        {
            try
            {
                Console.WriteLine($"🔍 [ChatbotController] Bắt đầu xử lý Ask...");

                if (req == null || string.IsNullOrWhiteSpace(req.Message))
                {
                    Console.WriteLine("❌ [ChatbotController] Câu hỏi rỗng");
                    return BadRequest(new { success = false, message = "Câu hỏi không được để trống" });
                }

                Console.WriteLine($"🤖 [ChatbotController] Nhận câu hỏi: {req.Message}");

                // ✅ Lấy mã người dùng
                string maNguoiDung = HttpContext.Request.Headers["MaNguoiDung"].FirstOrDefault()
                              ?? HttpContext.Session.GetString("MaNguoiDung")
                              ?? "guest";

                Console.WriteLine($"👤 [ChatbotController] User: {maNguoiDung}");

                // ✅ Kiểm tra kết nối database
                try
                {
                    var dbConnected = await _context.Database.CanConnectAsync();
                    Console.WriteLine($"📊 [ChatbotController] Database connected: {dbConnected}");

                    if (!dbConnected)
                    {
                        return StatusCode(500, new { success = false, message = "Không thể kết nối database" });
                    }
                }
                catch (Exception dbEx)
                {
                    Console.WriteLine($"❌ [ChatbotController] Database error: {dbEx.Message}");
                    return StatusCode(500, new { success = false, message = $"Lỗi database: {dbEx.Message}" });
                }

                // ✅ Gọi AI để lấy trả lời
                string reply;
                try
                {
                    Console.WriteLine($"🤖 [ChatbotController] Đang gọi AI Service...");
                    reply = await _ai.AskAsync(req.Message, maNguoiDung);
                    Console.WriteLine($"✅ [ChatbotController] AI trả lời thành công");
                }
                catch (Exception aiEx)
                {
                    Console.WriteLine($"❌ [ChatbotController] AI Service error: {aiEx.Message}");
                    Console.WriteLine($"❌ [ChatbotController] AI StackTrace: {aiEx.StackTrace}");

                    // Fallback response
                    reply = "Xin lỗi, hiện tôi đang gặp sự cố kỹ thuật. Vui lòng thử lại sau.";
                }

                // ✅ Lưu lịch sử chat vào DB
                try
                {
                    var history = new ChatHistory
                    {
                        MaNguoiDung = maNguoiDung,
                        Question = req.Message,
                        Response = reply,
                        ContextUsed = null,
                        Timestamp = DateTime.Now
                    };

                    _context.ChatHistories.Add(history);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"💾 [ChatbotController] Đã lưu lịch sử vào database");
                }
                catch (Exception saveEx)
                {
                    Console.WriteLine($"⚠️ [ChatbotController] Không thể lưu lịch sử: {saveEx.Message}");
                    // Vẫn trả về reply cho user dù không lưu được lịch sử
                }

                Console.WriteLine($"✅ [ChatbotController] Hoàn thành xử lý");
                return Ok(new { success = true, reply = reply });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [ChatbotController] LỖI TỔNG: {ex.Message}");
                Console.WriteLine($"💥 [ChatbotController] StackTrace: {ex.StackTrace}");

                if (ex.InnerException != null)
                {
                    Console.WriteLine($"💥 [ChatbotController] Inner Exception: {ex.InnerException.Message}");
                }

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Lỗi server: {ex.Message}",
                    reply = "Xin lỗi, đã xảy ra lỗi hệ thống. Vui lòng thử lại sau."
                });
            }
        }
        public async Task<string> AskAsync(string message, string userId)
        {
            if (!_userConversations.ContainsKey(userId))
                _userConversations[userId] = new List<string>();

            _userConversations[userId].Add($"User: {message}");

            var history = string.Join("\n", _userConversations[userId]);
            var prompt = $"Bạn là trợ lý AI quản lý kho. Dưới đây là cuộc trò chuyện trước:\n{history}\n\nNgười dùng hỏi: {message}";

            // FIX: Use the correct method from AIService
            var reply = await _ai.AskAsync(prompt, userId);
            _userConversations[userId].Add($"AI: {reply}");

            return reply;
        }

        // ==========================
        // 📜 Xem lịch sử chat theo người dùng
        // ==========================
        [HttpGet("History")]
        public async Task<IActionResult> GetHistory()
        {
            try
            {
                Console.WriteLine($"🔍 [ChatbotController] Lấy lịch sử chat...");

                string maNguoiDung = HttpContext.Session.GetString("MaNguoiDung") ?? "guest";
                string vaiTro = HttpContext.Session.GetString("VaiTro") ?? "User";

                Console.WriteLine($"👤 [ChatbotController] User: {maNguoiDung}, Role: {vaiTro}");

                IQueryable<ChatHistory> query = _context.ChatHistories;

                // Nếu KHÔNG phải Admin => chỉ xem lịch sử của chính mình
                if (!string.Equals(vaiTro, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(c => c.MaNguoiDung == maNguoiDung);
                }

                var historyList = await query
                    .OrderByDescending(c => c.Timestamp)
                    .Take(50) // Giới hạn 50 bản ghi
                    .Select(c => new
                    {
                        maNguoiDung = c.MaNguoiDung,
                        question = c.Question,
                        response = c.Response,
                        timestamp = c.Timestamp.ToString("dd/MM/yyyy HH:mm:ss")
                    })
                    .ToListAsync();

                Console.WriteLine($"✅ [ChatbotController] Tìm thấy {historyList.Count} bản ghi lịch sử");

                return Ok(new { success = true, history = historyList });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [ChatbotController] Lỗi GetHistory: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Lỗi khi lấy lịch sử: {ex.Message}"
                });
            }
        }

        //// ==========================
        //// 📷 Upload ảnh cho AI xử lý
        //// ==========================
        [HttpPost("UploadImage")]
        public async Task<IActionResult> UploadImage(IFormFile image)
        {
            try
            {
                Console.WriteLine($"🔍 [ChatbotController] Bắt đầu xử lý upload ảnh...");

                if (image == null || image.Length == 0)
                {
                    Console.WriteLine("❌ [ChatbotController] Không có ảnh được tải lên");
                    return BadRequest(new { success = false, message = "Không có ảnh được tải lên." });
                }

                Console.WriteLine($"📤 [ChatbotController] Nhận ảnh: {image.FileName}, Size: {image.Length} bytes");

                string maNguoiDung = HttpContext.Session.GetString("MaNguoiDung") ?? "guest";

                // ✅ Kiểm tra kích thước ảnh (tối đa 5MB)
                if (image.Length > 5 * 1024 * 1024)
                {
                    Console.WriteLine("❌ [ChatbotController] Ảnh quá lớn");
                    return BadRequest(new { success = false, message = "Kích thước ảnh quá lớn. Vui lòng chọn ảnh nhỏ hơn 5MB." });
                }

                // ✅ Kiểm tra định dạng hợp lệ
                var allowedExtensions = new[] { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp" };
                var fileExtension = Path.GetExtension(image.FileName).ToLowerInvariant();
                if (!allowedExtensions.Contains(fileExtension))
                {
                    Console.WriteLine($"❌ [ChatbotController] Định dạng không hỗ trợ: {fileExtension}");
                    return BadRequest(new { success = false, message = "Định dạng ảnh không được hỗ trợ." });
                }

                // ✅ Đọc dữ liệu ảnh
                using var memoryStream = new MemoryStream();
                await image.CopyToAsync(memoryStream);
                var imageData = memoryStream.ToArray();

                Console.WriteLine($"🖼️ [ChatbotController] Đã đọc ảnh: {imageData.Length} bytes");

                // ✅ Gọi AI xử lý ảnh
                string reply;
                try
                {
                    Console.WriteLine($"🤖 [ChatbotController] Đang gọi AI xử lý ảnh...");
                    reply = await _ai.ProcessImageAndCreateOrder(imageData, image.FileName, maNguoiDung);
                    Console.WriteLine($"✅ [ChatbotController] AI xử lý ảnh thành công");
                }
                catch (Exception aiEx)
                {
                    Console.WriteLine($"❌ [ChatbotController] AI xử lý ảnh lỗi: {aiEx.Message}");
                    reply = $"❌ Lỗi xử lý ảnh: {aiEx.Message}";
                }

                // ✅ Lưu lịch sử upload ảnh
                try
                {
                    var history = new ChatHistory
                    {
                        MaNguoiDung = maNguoiDung,
                        Question = $"Upload ảnh: {image.FileName}",
                        Response = reply,
                        ContextUsed = null,
                        Timestamp = DateTime.Now
                    };

                    _context.ChatHistories.Add(history);
                    await _context.SaveChangesAsync();
                    Console.WriteLine($"💾 [ChatbotController] Đã lưu lịch sử ảnh");
                }
                catch (Exception saveEx)
                {
                    Console.WriteLine($"⚠️ [ChatbotController] Không thể lưu lịch sử ảnh: {saveEx.Message}");
                }

                Console.WriteLine($"✅ [ChatbotController] Hoàn thành xử lý ảnh");
                return Ok(new { success = true, reply = reply });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [ChatbotController] Lỗi UploadImage: {ex.Message}");
                Console.WriteLine($"💥 [ChatbotController] StackTrace: {ex.StackTrace}");

                return StatusCode(500, new
                {
                    success = false,
                    message = $"Lỗi khi xử lý ảnh: {ex.Message}"
                });
            }
        }

        // ==========================
        // 🩺 Kiểm tra kết nối và trạng thái AI
        // ==========================
        [HttpGet("Test")]
        public async Task<IActionResult> TestConnection()
        {
            try
            {
                Console.WriteLine($"🔍 [ChatbotController] Kiểm tra kết nối...");

                // Kiểm tra kết nối database
                var dbConnected = await _context.Database.CanConnectAsync();
                var historyCount = await _context.ChatHistories.CountAsync();
                var productCount = await _context.SanPhams.CountAsync();

                // Kiểm tra AI service
                string aiTestResult;
                try
                {
                    aiTestResult = await _ai.TestConnectionAsync();
                }
                catch (Exception aiEx)
                {
                    aiTestResult = $"Lỗi AI Service: {aiEx.Message}";
                }

                var result = new
                {
                    success = true,
                    message = "Chatbot API đang hoạt động",
                    database = dbConnected ? "✅ Connected" : "❌ Disconnected",
                    totalHistory = historyCount,
                    totalProducts = productCount,
                    aiService = aiTestResult,
                    timestamp = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss")
                };

                Console.WriteLine($"✅ [ChatbotController] Kết quả test: {System.Text.Json.JsonSerializer.Serialize(result)}");

                return Ok(result);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [ChatbotController] Lỗi TestConnection: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Lỗi kiểm tra kết nối: {ex.Message}"
                });
            }
        }

        // ==========================
        // 🗑️ Xóa lịch sử chat
        // ==========================
        [HttpDelete("ClearHistory")]
        public async Task<IActionResult> ClearHistory()
        {
            try
            {
                Console.WriteLine($"🔍 [ChatbotController] Xóa lịch sử...");

                string maNguoiDung = HttpContext.Session.GetString("MaNguoiDung") ?? "guest";
                string vaiTro = HttpContext.Session.GetString("VaiTro") ?? "User";

                Console.WriteLine($"👤 [ChatbotController] User: {maNguoiDung}, Role: {vaiTro}");

                IQueryable<ChatHistory> query = _context.ChatHistories;

                // Nếu KHÔNG phải Admin => chỉ xóa lịch sử của chính mình
                if (!string.Equals(vaiTro, "Admin", StringComparison.OrdinalIgnoreCase))
                {
                    query = query.Where(c => c.MaNguoiDung == maNguoiDung);
                }

                var recordsToDelete = await query.ToListAsync();
                int deletedCount = recordsToDelete.Count;

                if (deletedCount > 0)
                {
                    _context.ChatHistories.RemoveRange(recordsToDelete);
                    await _context.SaveChangesAsync();

                    Console.WriteLine($"✅ [ChatbotController] Đã xóa {deletedCount} bản ghi lịch sử");
                    return Ok(new
                    {
                        success = true,
                        message = $"Đã xóa {deletedCount} bản ghi lịch sử trò chuyện."
                    });
                }

                Console.WriteLine($"ℹ️ [ChatbotController] Không có lịch sử để xóa");
                return Ok(new
                {
                    success = true,
                    message = "Không có lịch sử trò chuyện để xóa."
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 [ChatbotController] Lỗi ClearHistory: {ex.Message}");
                return StatusCode(500, new
                {
                    success = false,
                    message = $"Lỗi khi xóa lịch sử: {ex.Message}"
                });
            }
        }
    }

    // Model cho request gửi lên API
    public class ChatMessageRequest
    {
        public string Message { get; set; }
    }
}