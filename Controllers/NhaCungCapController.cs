using Microsoft.AspNetCore.Mvc;     // DbContext
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;     // ModelNhaCungCap

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class NhaCungCapController : ControllerBase
    {
        private readonly AppDbContext _context;

        public NhaCungCapController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/NhaCungCap
        [HttpGet]
        public ActionResult<IEnumerable<ModelNhaCungCap>> GetAll()
        {
            return Ok(_context.NhaCungCaps.ToList());
        }
        // POST: api/NhaCungCap
        [HttpPost]
        public async Task<ActionResult<ModelNhaCungCap>> Create(ModelNhaCungCap ncc)
        {
            if (await _context.NhaCungCaps.AnyAsync(x => x.MaNCC == ncc.MaNCC))
            {
                return BadRequest(new { message = "Mã nhà cung cấp đã tồn tại." });
            }

            ncc.NgayTao = DateTime.Now;
            _context.NhaCungCaps.Add(ncc);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = ncc.MaNCC }, ncc);
        }

        // GET: api/NhaCungCap/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<ModelNhaCungCap>> GetById(string id)
        {
            var ncc = await _context.NhaCungCaps.FindAsync(id);
            if (ncc == null)
                return NotFound(new { message = "Không tìm thấy nhà cung cấp." });

            return ncc;
        }

        // PUT: api/NhaCungCap/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> Update(string id, ModelNhaCungCap ncc)
        {
            if (id != ncc.MaNCC)
            {
                return BadRequest(new { message = "Mã NCC không khớp." });
            }

            var existing = await _context.NhaCungCaps.FindAsync(id);
            if (existing == null)
            {
                return NotFound(new { message = "Không tìm thấy nhà cung cấp." });
            }

            existing.TenNCC = ncc.TenNCC;
            existing.NguoiLienHe = ncc.NguoiLienHe;
            existing.SoDienThoai = ncc.SoDienThoai;
            existing.Email = ncc.Email;
            existing.DiaChi = ncc.DiaChi;
            // giữ nguyên MaNCC và NgayTao

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật thành công." });
        }

        // DELETE: api/NhaCungCap/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> Delete(string id)
        {
            var ncc = await _context.NhaCungCaps.FindAsync(id);
            if (ncc == null)
            {
                return NotFound(new { message = "Không tìm thấy nhà cung cấp." });
            }

            _context.NhaCungCaps.Remove(ncc);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Xóa thành công." });
        }

    }
}
