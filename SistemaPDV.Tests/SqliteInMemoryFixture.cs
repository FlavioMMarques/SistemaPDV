using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;

namespace SistemaPDV.Tests;

// Sqlite ":memory:" só existe enquanto a conexão está aberta — por isso a conexão
// fica guardada aqui e viva durante todo o teste, em vez de abrir/fechar por operação.
public sealed class SqliteInMemoryFixture : IDisposable
{
    public SqliteConnection Connection { get; }

    public SqliteInMemoryFixture()
    {
        Connection = new SqliteConnection("DataSource=:memory:");
        Connection.Open();
    }

    // Cada chamada devolve uma NOVA instância de AppDbContext ligada à mesma conexão
    // (mesmo schema/dados) — simula abrir um contexto novo pra reler o que outro
    // contexto salvou, do jeito que a aplicação real faz a cada operação.
    public AppDbContext CriarContexto()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(Connection)
            .Options;

        var context = new AppDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public void Dispose() => Connection.Dispose();
}
