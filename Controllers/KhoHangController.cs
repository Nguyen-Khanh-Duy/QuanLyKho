using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class KhoHangController : ControllerBase
    {
        private readonly AppDbContext _context;

        public KhoHangController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/KhoHang
        [HttpGet]
        public ActionResult<IEnumerable<ModelKhoHang>> GetAll()
        {
            return Ok(_context.KhoHangs.ToList());
        }

        // GET: api/KhoHang/{maKho}
        [HttpGet("{maKho}")]
        public async Task<ActionResult<ModelKhoHang>> GetById(string maKho)
        {
            var kho = await _context.KhoHangs.FirstOrDefaultAsync(x => x.MaKho == maKho);
            if (kho == null)
                return NotFound(new { message = "Không tìm thấy kho." });

            return kho;
        }

        // POST: api/KhoHang
        [HttpPost]
        public async Task<ActionResult<ModelKhoHang>> Create(ModelKhoHang kho)
        {
            if (await _context.KhoHangs.AnyAsync(x => x.MaKho == kho.MaKho))
            {
                return BadRequest(new { message = "Mã kho đã tồn tại." });
            }

            kho.NgayTao = DateTime.Now;
            _context.KhoHangs.Add(kho);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { maKho = kho.MaKho }, kho);
        }

        // PUT: api/KhoHang/{maKho}
        [HttpPut("{maKho}")]
        public async Task<IActionResult> Update(string maKho, ModelKhoHang kho)
        {
            if (maKho != kho.MaKho)
            {
                return BadRequest(new { message = "Mã kho không khớp." });
            }

            var existing = await _context.KhoHangs.FirstOrDefaultAsync(x => x.MaKho == maKho);
            if (existing == null)
            {
                return NotFound(new { message = "Không tìm thấy kho." });
            }

            // Cập nhật dữ liệu
            existing.MaSanPham = kho.MaSanPham;
            existing.TenSanPham = kho.TenSanPham;
            existing.SoLuongTon = kho.SoLuongTon;
            existing.NgayNhapGanNhat = kho.NgayNhapGanNhat;
            existing.NgayBanGanNhat = kho.NgayBanGanNhat;
            existing.SoLuongSapHetHan = kho.SoLuongSapHetHan;
            // giữ nguyên MaKho và NgayTao

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật thành công." });
        }

        // DELETE: api/KhoHang/{maKho}
        [HttpDelete("{maKho}")]
        public async Task<IActionResult> Delete(string maKho)
        {
            var kho = await _context.KhoHangs.FirstOrDefaultAsync(x => x.MaKho == maKho);
            if (kho == null)
            {
                return NotFound(new { message = "Không tìm thấy kho." });
            }

            _context.KhoHangs.Remove(kho);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Xóa thành công." });
        }
    }
}
