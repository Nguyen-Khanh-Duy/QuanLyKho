using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;
using System.Security.Cryptography;
using System.Text;

[Route("api/[controller]")]
[ApiController]
public class NguoiDungController : ControllerBase
{
    private readonly AppDbContext _context;

    public NguoiDungController(AppDbContext context)
    {
        _context = context;
    }

    // GET: api/NguoiDung
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ModelNguoiDung>>> GetNguoiDungs()
    {
        return await _context.NguoiDungs.ToListAsync();
    }

    // GET api/NguoiDung/U001
    [HttpGet("{maNguoiDung}")]
    public async Task<ActionResult<ModelNguoiDung>> GetNguoiDung(string maNguoiDung)
    {
        var user = await _context.NguoiDungs.FirstOrDefaultAsync(u => u.MaNguoiDung == maNguoiDung);
        if (user == null)
            return NotFound();

        return user;
    }

    // POST: api/NguoiDung
    [HttpPost]
    public async Task<ActionResult<ModelNguoiDung>> PostNguoiDung(ModelNguoiDung nguoiDung)
    {
        if (await _context.NguoiDungs.AnyAsync(x => x.MaNguoiDung == nguoiDung.MaNguoiDung))
            return BadRequest(new { message = "Mã người dùng đã tồn tại." });

        nguoiDung.NgayTao = DateTime.Now;
        _context.NguoiDungs.Add(nguoiDung);
        await _context.SaveChangesAsync();

        return CreatedAtAction(nameof(GetNguoiDung), new { id = nguoiDung.MaNguoiDung }, nguoiDung);
    }

    // PUT: api/NguoiDung/{id}
    [HttpPut("{id}")]
    public async Task<IActionResult> PutNguoiDung(string id, ModelNguoiDung nguoiDung)
    {
        if (id != nguoiDung.MaNguoiDung)
            return BadRequest(new { message = "Mã người dùng không khớp." });

        var existingNguoiDung = await _context.NguoiDungs.FindAsync(id);
        if (existingNguoiDung == null)
            return NotFound(new { message = "Không tìm thấy người dùng." });

        existingNguoiDung.HoTen = nguoiDung.HoTen;
        existingNguoiDung.Email = nguoiDung.Email;
        existingNguoiDung.MatKhauHash = nguoiDung.MatKhauHash;
        existingNguoiDung.VaiTro = nguoiDung.VaiTro;

        await _context.SaveChangesAsync();
        return Ok(new { message = "Cập nhật thành công." });
    }

    // DELETE: api/NguoiDung/{id}
    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteNguoiDung(string id)
    {
        var nguoiDung = await _context.NguoiDungs.FindAsync(id);
        if (nguoiDung == null)
            return NotFound(new { message = "Không tìm thấy người dùng." });

        _context.NguoiDungs.Remove(nguoiDung);
        await _context.SaveChangesAsync();
        return Ok(new { message = "Xóa thành công." });
    }
    // ------------------- LOGIN -------------------
    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
            return BadRequest(new { message = "Email và mật khẩu không được để trống." });

        var user = await _context.NguoiDungs.FirstOrDefaultAsync(u => u.Email == request.Email);
        if (user == null)
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng." });

        // So sánh trực tiếp mật khẩu
        if (user.MatKhauHash != request.Password)
            return Unauthorized(new { message = "Email hoặc mật khẩu không đúng." });

        if (user.VaiTro != "Admin" && user.VaiTro != "QuanLy" && user.VaiTro != "NhanVien")
            return Forbid("Bạn không có quyền đăng nhập.");

        return Ok(new
        {
            message = "Đăng nhập thành công.",
            user.MaNguoiDung,
            user.HoTen,
            user.Email,
            user.VaiTro
        });
    }

    // ------------------- RESET PASSWORD -------------------
    [HttpPost("reset-password")]
    public async Task<IActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
    {
        if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.NewPassword))
            return BadRequest(new { message = "Email và mật khẩu mới không được để trống." });

        var user = await _context.NguoiDungs.FirstOrDefaultAsync(nd => nd.Email == request.Email);
        if (user == null)
            return NotFound(new { message = "Không tìm thấy người dùng." });

        // Lưu mật khẩu mới trực tiếp
        user.MatKhauHash = request.NewPassword;
        await _context.SaveChangesAsync();

        return Ok(new { message = "Mật khẩu đã được đặt lại thành công!" });
    }

    [HttpPut("update-info/{id}")]
    public async Task<IActionResult> UpdateInfo(string id, [FromBody] UpdateInfoRequest request)
    {
        var user = await _context.NguoiDungs.FindAsync(id);
        if (user == null)
            return NotFound(new { message = "Không tìm thấy người dùng." });

        user.HoTen = request.HoTen;
        user.Email = request.Email;

        await _context.SaveChangesAsync();

        return Ok(new { message = "Cập nhật thông tin thành công." });
    }

    public class UpdateInfoRequest
    {
        public string HoTen { get; set; }
        public string Email { get; set; }
    }

    // ------------------- DTOs -------------------
    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    public class ForgotPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
    }

    public class ResetPasswordRequest
    {
        public string Email { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}