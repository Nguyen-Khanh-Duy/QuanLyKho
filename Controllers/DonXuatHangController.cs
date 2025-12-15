using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DonXuatHangController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DonXuatHangController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/DonXuatHang
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ModelDonXuatHang>>> GetAll()
        {
            return await _context.DonXuatHangs.ToListAsync();
        }

        // GET: api/DonXuatHang/5
        [HttpGet("{id}")]
        public async Task<ActionResult<ModelDonXuatHang>> GetById(int id)
        {
            var donXuat = await _context.DonXuatHangs.FindAsync(id);
            if (donXuat == null)
                return NotFound();

            return Ok(donXuat);
        }

        // POST: api/DonXuatHang
        [HttpPost]
        public async Task<ActionResult<ModelDonXuatHang>> Create(ModelDonXuatHang donXuat)
        {
            donXuat.NgayTao = DateTime.Now;
            _context.DonXuatHangs.Add(donXuat);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetById), new { id = donXuat.STT }, donXuat);
        }

        // PUT: api/DonXuatHang/5
        [HttpPut("{id}")]
        public async Task<ActionResult<ModelDonXuatHang>> Update(int id, ModelDonXuatHang donXuat)
        {
            var existing = await _context.DonXuatHangs.FindAsync(id);
            if (existing == null)
                return NotFound();

            // Cập nhật dữ liệu
            existing.MaDonXuat = donXuat.MaDonXuat;
            existing.NgayXuatHang = donXuat.NgayXuatHang;
            existing.TrangThai = donXuat.TrangThai;
            existing.TongTien = donXuat.TongTien;
            existing.MaNguoiDung = donXuat.MaNguoiDung;
            existing.MaKhachHang = donXuat.MaKhachHang;
            existing.TenKhachHang = donXuat.TenKhachHang;
            existing.DiaChi = donXuat.DiaChi;
            existing.SoDienThoai = donXuat.SoDienThoai;
            existing.DiscountPercent = donXuat.DiscountPercent;
            existing.VatPercent = donXuat.VatPercent;

            await _context.SaveChangesAsync();
            return Ok(existing);
        }
    }
}
