using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class SanPhamController : ControllerBase
    {
        private readonly AppDbContext _context;

        public SanPhamController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/SanPham
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ModelSanPham>>> GetSanPhams()
        {
            return await _context.SanPhams.ToListAsync();
        }

        // API tìm sản phẩm theo mã vạch - ĐÃ SỬA
        [HttpGet("FindProductByBarcode")]
        public async Task<IActionResult> FindProductByBarcode([FromQuery] string barcodeContent)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(barcodeContent))
                {
                    return BadRequest(new { success = false, message = "Mã vạch không được để trống" });
                }

                string raw = barcodeContent.Trim();
                string formatted = $"/barcodes/{raw}.png";
                Console.WriteLine($"🔍 Tìm mã vạch: raw={raw}, formatted={formatted}");

                // 1️⃣ Tìm trong ChiTietDonNhap
                var resultCT = await (from ct in _context.ChiTietDonNhaps
                                      join sp in _context.SanPhams on ct.MaSanPham equals sp.MaSanPham
                                      join dn in _context.DonNhapHangs on ct.MaDonNhap equals dn.MaDonNhap
                                      join ncc in _context.NhaCungCaps on dn.MaNCC equals ncc.MaNCC into nccGroup
                                      from ncc in nccGroup.DefaultIfEmpty()
                                      where ct.MaVach == raw || ct.MaVach == formatted
                                      select new
                                      {
                                          sp.MaSanPham,
                                          sp.TenSanPham,
                                          sp.MaSKU,
                                          sp.LoaiSanPham,
                                          sp.DonViTinh,
                                          GiaNhap = ct.GiaNhap,
                                          sp.GiaBan,
                                          SoLuongNhap = ct.SoLuong,
                                          MaDonNhap = ct.MaDonNhap,
                                          NgayDatHang = dn.NgayDatHang,
                                          NhaCungCap = ncc != null ? new
                                          {
                                              ncc.MaNCC,
                                              ncc.TenNCC,
                                              ncc.NguoiLienHe,
                                              ncc.SoDienThoai,
                                              ncc.Email,
                                              ncc.DiaChi
                                          } : null,
                                          BarcodePath = ct.MaVach
                                      }).FirstOrDefaultAsync();

                if (resultCT != null)
                {
                    Console.WriteLine($"✅ Tìm thấy trong ChiTietDonNhap: {resultCT.MaSanPham}");
                    return Ok(new { success = true, product = resultCT, type = "ChiTietDonNhap" });
                }

                // 2️⃣ Tìm trong SanPham
                var resultSP = await (from sp in _context.SanPhams
                                      where sp.MaVach == raw || sp.MaVach == formatted
                                      select new
                                      {
                                          sp.MaSanPham,
                                          sp.TenSanPham,
                                          sp.MaSKU,
                                          sp.LoaiSanPham,
                                          sp.DonViTinh,
                                          sp.GiaBan,
                                          BarcodePath = sp.MaVach
                                      }).FirstOrDefaultAsync();

                if (resultSP != null)
                {
                    Console.WriteLine($"✅ Tìm thấy trong SanPham: {resultSP.MaSanPham}");
                    return Ok(new { success = true, product = resultSP, type = "SanPham" });
                }

                // 3️⃣ Fallback (LIKE)
                var fallbackResult = await (from sp in _context.SanPhams
                                            where sp.MaVach.Contains(raw) || sp.MaSanPham == raw
                                            select new
                                            {
                                                sp.MaSanPham,
                                                sp.TenSanPham,
                                                sp.MaSKU,
                                                sp.LoaiSanPham,
                                                sp.DonViTinh,
                                                sp.GiaBan,
                                                BarcodePath = sp.MaVach
                                            }).FirstOrDefaultAsync();

                if (fallbackResult != null)
                {
                    Console.WriteLine($"✅ Tìm thấy fallback: {fallbackResult.MaSanPham}");
                    return Ok(new { success = true, product = fallbackResult, type = "Fallback" });
                }

                Console.WriteLine($"❌ Không tìm thấy sản phẩm với mã: {raw}");
                return NotFound(new { success = false, message = "Không tìm thấy sản phẩm với mã vạch này" });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"💥 Lỗi server: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Lỗi server: {ex.Message}" });
            }
        }

        // API cũ để tương thích
        [HttpGet("mavach/{maVach}")]
        public async Task<IActionResult> GetSanPhamByMaVach(string maVach)
        {
            var result = await FindProductByBarcode(maVach);
            return result;
        }
        // GET: api/SanPham/{id}
        [HttpGet("{id}")]
        public async Task<ActionResult<ModelSanPham>> GetSanPham(string id)
        {
            var sp = await _context.SanPhams.FindAsync(id);
            if (sp == null)
                return NotFound(new { message = "Không tìm thấy sản phẩm." });
            return sp;
        }

        // POST: api/SanPham
        [HttpPost]
        public async Task<ActionResult<ModelSanPham>> PostSanPham(ModelSanPham sp)
        {
            if (await _context.SanPhams.AnyAsync(x => x.MaSanPham == sp.MaSanPham))
                return BadRequest(new { message = "Mã sản phẩm đã tồn tại." });

            sp.NgayTao = DateTime.Now;
            sp.NgayCapNhat = DateTime.Now;

            _context.SanPhams.Add(sp);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetSanPham), new { id = sp.MaSanPham }, sp);
        }

        // PUT: api/SanPham/{id}
        [HttpPut("{id}")]
        public async Task<IActionResult> PutSanPham(string id, ModelSanPham sp)
        {
            if (id != sp.MaSanPham)
                return BadRequest(new { message = "Mã sản phẩm không khớp." });

            var existing = await _context.SanPhams.FindAsync(id);
            if (existing == null)
                return NotFound(new { message = "Không tìm thấy sản phẩm." });

            existing.TenSanPham = sp.TenSanPham;
            existing.MaSKU = sp.MaSKU;
            existing.LoaiSanPham = sp.LoaiSanPham;
            existing.DonViTinh = sp.DonViTinh;
            existing.GiaBan = sp.GiaBan;
            existing.GiaNhap = sp.GiaNhap;
            existing.SoLuongNhap = sp.SoLuongNhap;
            existing.SoLuongXuat = sp.SoLuongXuat;
            existing.NgayCapNhat = DateTime.Now;

            await _context.SaveChangesAsync();
            return Ok(new { message = "Cập nhật sản phẩm thành công." });
        }
        // DELETE: api/SanPham/{id}
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteSanPham(string id)
        {
            var sp = await _context.SanPhams.FindAsync(id);
            if (sp == null)
            {
                return NotFound(new { message = "Không tìm thấy sản phẩm để xóa." });
            }

            _context.SanPhams.Remove(sp);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Xóa sản phẩm thành công." });
        }

    }
}
