using Microsoft.EntityFrameworkCore;

namespace HajjVR.Data
{
    public class HajjDbContext : DbContext
    {
        public HajjDbContext(DbContextOptions<HajjDbContext> options) : base(options) { }

        // Tambahkan DbSet di sini nanti kalau mau buat tabel, contoh:
        // public DbSet<NamaModel> NamaTabel { get; set; }
    }
}