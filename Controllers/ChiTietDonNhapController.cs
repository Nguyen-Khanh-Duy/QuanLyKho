using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChiTietDonNhapController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ChiTietDonNhapController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/ChiTietDonNhap
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ModelChiTietDonNhap>>> GetAll()
        {
            return await _context.ChiTietDonNhaps.ToListAsync();
        }
    }
}
