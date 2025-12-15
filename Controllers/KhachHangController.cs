using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KhachHangController : ControllerBase
    {
        private readonly AppDbContext _context;

        public KhachHangController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/KhachHang
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ModelKhachHang>>> GetKhachHangs()
        {
            return await _context.KhachHangs.ToListAsync();
        }
        // GET: api/KhachHang/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<ModelKhachHang>> GetById(string id)
        {
            var kh = await _context.KhachHangs.FindAsync(id);
            if (kh == null)
                return NotFound(new { message = "Không tìm thấy khách hàng." });

            return Ok(kh);
        }

        // POST: api/KhachHang
        [HttpPost]
        public async Task<ActionResult<ModelKhachHang>> Create(ModelKhachHang kh)
        {
            if (await _context.KhachHangs.AnyAsync(x => x.MaKhachHang == kh.MaKhachHang))
            {
                return BadRequest(new { message = "Mã khách hàng đã tồn tại." });
            }

            kh.NgayTao = DateTime.Now;
            _context.KhachHangs.Add(kh);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = kh.MaKhachHang }, kh);
        }

        // PUT: api/KhachHang/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, ModelKhachHang kh)
        {
            if (id != kh.MaKhachHang)
                return BadRequest(new { message = "Mã khách hàng không khớp." });

            var existing = await _context.KhachHangs.FindAsync(id);
            if (existing == null)
                return NotFound(new { message = "Không tìm thấy khách hàng." });

            existing.TenKhachHang = kh.TenKhachHang;
            existing.SoDienThoai = kh.SoDienThoai;
            existing.DiaChi = kh.DiaChi;
            existing.MaDonHang = kh.MaDonHang;
            // Giữ nguyên MaKhachHang, NgayTao

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật thành công." });
        }

        // DELETE: api/KhachHang/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var kh = await _context.KhachHangs.FindAsync(id);
            if (kh == null)
                return NotFound(new { message = "Không tìm thấy khách hàng." });

            _context.KhachHangs.Remove(kh);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Xóa thành công." });
        }

    }
}
