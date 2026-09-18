namespace SistemaPDV.Tests;

public class SmokeTests
{
    [Fact]
    public void ConexaoInMemoryAbreExecutaEFecha()
    {
        using var fixture = new SqliteInMemoryFixture();
        using var command = fixture.Connection.CreateCommand();
        command.CommandText = "SELECT 1";

        var resultado = command.ExecuteScalar();

        Assert.Equal(1L, resultado);
    }
}
