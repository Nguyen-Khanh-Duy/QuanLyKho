using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using QuanlykhoAPI.Models;

namespace QuanlykhoAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ChiTietDonXuatController : ControllerBase
    {
        private readonly AppDbContext _context;

        public ChiTietDonXuatController(AppDbContext context)
        {
            _context = context;
        }

        // GET: api/ChiTietDonXuat
        [HttpGet]
        public async Task<ActionResult<IEnumerable<ModelChiTietDonXuat>>> GetAll()
        {
            return await _context.ChiTietDonXuats.ToListAsync();
        }
    }
}
