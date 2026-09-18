using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Caixa;

// Login local, sem rede: compara o hash da chave digitada contra o PdvKeyHash já
// sincronizado por catalog-sync — nunca guarda nem recebe a chave em claro de volta.
public class LoginOperadorService
{
    private readonly Func<AppDbContext> contextFactory;

    public LoginOperadorService(Func<AppDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<Funcionario?> AutenticarAsync(string pdvKeyDigitado, CancellationToken ct = default)
    {
        var hash = PdvKeyHasher.Hash(pdvKeyDigitado);
        if (hash is null)
            return null;

        await using var context = contextFactory();
        return await context.Funcionarios.FirstOrDefaultAsync(f => f.PdvKeyHash == hash && !f.Desativado, ct);
    }
}
