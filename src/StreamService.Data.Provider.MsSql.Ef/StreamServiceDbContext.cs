using Microsoft.EntityFrameworkCore;
using System.Reflection;
using System.Threading.Tasks;

namespace LT.DigitalOffice.StreamService.Data.Provider.MsSql.Ef
{
  public class StreamServiceDbContext : DbContext, IDataProvider
  {
    public StreamServiceDbContext(DbContextOptions<StreamServiceDbContext> options)
    : base(options)
    {
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
      modelBuilder.ApplyConfigurationsFromAssembly(Assembly.Load("LT.DigitalOffice.StreamService.Models.Db"));
    }

    public void EnsureDeleted()
    {
      Database.EnsureDeleted();
    }

    public bool IsInMemory()
    {
      return Database.IsInMemory();
    }

    public object MakeEntityDetached(object obj)
    {
      Entry(obj).State = EntityState.Detached;

      return Entry(obj).State;
    }

    public void Save()
    {
      SaveChanges();
    }

    public async Task SaveAsync()
    {
      await SaveChangesAsync();
    }
  }
}
