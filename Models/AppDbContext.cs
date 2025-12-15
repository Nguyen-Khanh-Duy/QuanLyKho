using Microsoft.EntityFrameworkCore;

namespace QuanlykhoAPI.Models
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options)
            : base(options)
        {
        }
     
        // EF Core DbSet
        public DbSet<ModelSanPham> SanPhams { get; set; } = null!;
        public DbSet<ModelNhaCungCap> NhaCungCaps { get; set; } = null!;
        public DbSet<ModelKhoHang> KhoHangs { get; set; } = null!;
        public DbSet<ModelDonNhapHang> DonNhapHangs { get; set; } = null!;
        public DbSet<ModelDonXuatHang> DonXuatHangs { get; set; } = null!;
        public DbSet<ModelChiTietDonNhap> ChiTietDonNhaps { get; set; } = null!;
        public DbSet<ModelChiTietDonXuat> ChiTietDonXuats { get; set; } = null!;
        public DbSet<ModelKhachHang> KhachHangs { get; set; } = null!;
        public DbSet<ModelLichSuXuatNhap> LichSuXuatNhaps { get; set; } = null!;
        public DbSet<ModelNguoiDung> NguoiDungs { get; set; } = null!;
        public virtual DbSet<LearnedPattern> LearnedPatterns { get; set; }
        // Các DbSet khác của bạn (ví dụ Products, Orders, Users...)
        public DbSet<ChatHistory> ChatHistories { get; set; }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Cấu hình cho LearnedPattern
            modelBuilder.Entity<LearnedPattern>(entity =>
            {
                entity.ToTable("LearnedPatterns");
                entity.HasKey(e => e.Id);
                entity.Property(e => e.Keyword).HasMaxLength(100).IsRequired();
                entity.Property(e => e.Pattern).HasMaxLength(500).IsRequired();
                entity.Property(e => e.LearnCount).HasDefaultValue(0);
                entity.Property(e => e.LastUsed).HasColumnType("datetime");
            });
        }
    }
}
