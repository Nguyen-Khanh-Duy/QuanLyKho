using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class LichSuXuatNhapController : ControllerBase
    {
        private readonly AppDbContext _context;

        public LichSuXuatNhapController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/LichSuXuatNhap
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ModelLichSuXuatNhap>>> GetLichSu()
        {
            return await _context.LichSuXuatNhaps.ToListAsync();
        }  
    }
}
