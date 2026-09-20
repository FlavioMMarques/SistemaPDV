using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Caixa;

// Login local, sem rede: confere a chave digitada contra os hashes bcrypt de pdv_key que
// catalog-sync já baixou — nunca guarda nem recebe a chave em claro de volta.
//
// O login não pede usuário (só a chave), então não há "o" funcionário pra conferir: tenta-se
// cada funcionário ativo com hash. bcrypt é lento DE PROPÓSITO (custo 10 ≈ dezenas de ms cada;
// dezenas de operadores = até ~1 s no pior caso), por isso a conferência roda fora da thread
// de UI — o login não pode congelar a janela.
public class LoginOperadorService
{
    private readonly Func<AppDbContext> contextFactory;

    public LoginOperadorService(Func<AppDbContext> contextFactory)
    {
        this.contextFactory = contextFactory;
    }

    public async Task<Funcionario?> AutenticarAsync(string pdvKeyDigitado, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(pdvKeyDigitado))
            return null;

        await using var context = contextFactory();
        var candidatos = await context.Funcionarios
            .Where(f => !f.Desativado && f.PdvKeyHash != null)
            .ToListAsync(ct);

        return await Task.Run(
            () => candidatos.FirstOrDefault(f => PdvKeyHasher.Verificar(pdvKeyDigitado, f.PdvKeyHash)),
            ct);
    }
}
