using Microsoft.AspNetCore.Mvc;
using QuanlykhoAPI.Models;  // chứa ModelDonNhapHang
using System.Collections.Generic;
using System.Linq;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DonNhapHangController : ControllerBase
    {
        private readonly AppDbContext _context;

        public DonNhapHangController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/DonNhapHang
        [HttpGet]
        public ActionResult<IEnumerable<ModelDonNhapHang>> GetAll()
        {
            return Ok(_context.DonNhapHangs.ToList());
        }

        // GET: api/DonNhapHang/5
        [HttpGet("{id}")]
        public ActionResult<ModelDonNhapHang> GetById(int id)
        {
            var donNhap = _context.DonNhapHangs.Find(id);
            if (donNhap == null)
                return NotFound();

            return Ok(donNhap);
        }

        // POST: api/DonNhapHang
        [HttpPost]
        public ActionResult<ModelDonNhapHang> Create(ModelDonNhapHang donNhap)
        {
            if (donNhap == null)
                return BadRequest();

            _context.DonNhapHangs.Add(donNhap);
            _context.SaveChanges();

            return CreatedAtAction(nameof(GetById), new { id = donNhap.STT }, donNhap);
        }

        // PUT: api/DonNhapHang/5
        [HttpPut("{id}")]
        public ActionResult<ModelDonNhapHang> Update(int id, ModelDonNhapHang donNhap)
        {
            var existing = _context.DonNhapHangs.Find(id);
            if (existing == null)
                return NotFound();

            // Cập nhật dữ liệu
            existing.MaDonNhap = donNhap.MaDonNhap;
            existing.MaNCC = donNhap.MaNCC;
            existing.MaNguoiDung = donNhap.MaNguoiDung;
            existing.NgayDatHang = donNhap.NgayDatHang;
            existing.TrangThai = donNhap.TrangThai;
            existing.TongTien = donNhap.TongTien;
            existing.NgayTao = donNhap.NgayTao;

            _context.SaveChanges();
            return Ok(existing);
        }
    }
}
