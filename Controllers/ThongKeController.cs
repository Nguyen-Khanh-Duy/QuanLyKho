using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ThongKeController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ThongKeController(AppDbContext context)
        {
            _context = context;
        }

        // ✅ 1. Tổng quan
        [HttpGet("TongQuan")]
        public async Task<ActionResult> GetTongQuan()
        {
            var tongDonNhap = await _context.DonNhapHangs.CountAsync();
            var tongDonXuat = await _context.DonXuatHangs.CountAsync();

            var tongTienNhap = await _context.DonNhapHangs.SumAsync(x => (decimal?)x.TongTien) ?? 0;
            var tongTienXuat = await _context.DonXuatHangs.SumAsync(x => (decimal?)x.TongTien) ?? 0;

            var loiNhuan = tongTienXuat - tongTienNhap;

            return Ok(new
            {
                TongDonNhap = tongDonNhap,
                TongDonXuat = tongDonXuat,
                TongTienNhap = tongTienNhap,
                TongTienXuat = tongTienXuat,
                LoiNhuanTamTinh = loiNhuan
            });
        }

        // ✅ 2. Thống kê theo tháng
        [HttpGet("TheoThang/{nam}")]
        public async Task<ActionResult> GetTheoThang(int nam)
        {
            var nhap = await _context.DonNhapHangs
                .Where(x => x.NgayDatHang.Year == nam)
                .GroupBy(x => x.NgayDatHang.Month)
                .Select(g => new
                {
                    Thang = g.Key,
                    TongTienNhap = g.Sum(x => x.TongTien)
                }).ToListAsync();

            var xuat = await _context.DonXuatHangs
                .Where(x => x.NgayXuatHang.Year == nam)
                .GroupBy(x => x.NgayXuatHang.Month)
                .Select(g => new
                {
                    Thang = g.Key,
                    TongTienXuat = g.Sum(x => x.TongTien)
                }).ToListAsync();

            return Ok(new { Nam = nam, Nhap = nhap, Xuat = xuat });
        }

        // ✅ 3. Top sản phẩm bán chạy (theo số lượng xuất)
        [HttpGet("SanPhamBanChay")]
        public async Task<ActionResult> GetSanPhamBanChay(int top = 10)
        {
            var result = await (from ct in _context.ChiTietDonXuats
                                join sp in _context.SanPhams on ct.MaSanPham equals sp.MaSanPham
                                group ct by new { sp.MaSanPham, sp.TenSanPham } into g
                                orderby g.Sum(x => x.SoLuong) descending
                                select new
                                {
                                    g.Key.MaSanPham,
                                    g.Key.TenSanPham,
                                    SoLuongBan = g.Sum(x => x.SoLuong),
                                    DoanhThu = g.Sum(x => x.SoLuong * x.GiaBan)
                                }).Take(top).ToListAsync();

            return Ok(result);
        }

        // ✅ 4. Thống kê theo khách hàng (luôn hiện tất cả)
        [HttpGet("TheoKhachHang")]
        public async Task<ActionResult> GetTheoKhachHang()
        {
            var result = await (from kh in _context.KhachHangs
                                join dx in _context.DonXuatHangs on kh.MaKhachHang equals dx.MaKhachHang into gj
                                from dx in gj.DefaultIfEmpty()
                                group dx by new { kh.MaKhachHang, kh.TenKhachHang } into g
                                select new
                                {
                                    MaKhachHang = g.Key.MaKhachHang,
                                    TenKhachHang = g.Key.TenKhachHang,
                                    TongDon = g.Count(x => x != null),
                                    TongTienMua = g.Sum(x => (decimal?)(x != null ? x.TongTien : 0)) ?? 0
                                })
                                .OrderByDescending(x => x.TongTienMua)
                                .ToListAsync();

            return Ok(result);
        }

        // ✅ 5. Thống kê theo nhà cung cấp (luôn hiện tất cả)
        [HttpGet("TheoNhaCungCap")]
        public async Task<ActionResult> GetTheoNhaCungCap()
        {
            var result = await (from ncc in _context.NhaCungCaps
                                join dn in _context.DonNhapHangs on ncc.MaNCC equals dn.MaNCC into gj
                                from dn in gj.DefaultIfEmpty()
                                group dn by new { ncc.MaNCC, ncc.TenNCC } into g
                                select new
                                {
                                    MaNCC = g.Key.MaNCC,
                                    TenNCC = g.Key.TenNCC,
                                    TongDonNhap = g.Count(x => x != null),
                                    TongTienNhap = g.Sum(x => (decimal?)(x != null ? x.TongTien : 0)) ?? 0
                                })
                                .OrderByDescending(x => x.TongTienNhap)
                                .ToListAsync();

            return Ok(result);
        }

        // ✅ 6. Thống kê tồn kho
        [HttpGet("TonKho")]
        public async Task<ActionResult> GetTonKho()
        {
            var tonKho = await (from kho in _context.KhoHangs
                                join sp in _context.SanPhams on kho.MaSanPham equals sp.MaSanPham
                                select new
                                {
                                    sp.MaSanPham,
                                    sp.TenSanPham,
                                    kho.SoLuongTon,
                                    sp.DonViTinh,
                                    sp.GiaBan,
                                    GiaTriTon = kho.SoLuongTon * sp.GiaBan
                                }).ToListAsync();

            return Ok(tonKho);
        }
    }
}
