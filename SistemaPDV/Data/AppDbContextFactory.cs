using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SistemaPDV.Data;

// Usada pelo `dotnet ef` (migrations add/database update) pra criar um AppDbContext
// em tempo de design, sem precisar rodar o app inteiro (Program.cs/Avalonia) pra isso.
public class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
        optionsBuilder.UseSqlite("Data Source=pdv.db");

        return new AppDbContext(optionsBuilder.Options);
    }
}
