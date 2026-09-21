using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

namespace SistemaPDV.Services.Caixa;

// Um operador que pode aparecer na tela de login: só o que a lista precisa (nunca o hash da chave nem o CPF).
public record OperadorLogin(int Id, string Nome, bool Supervisor)
{
    // Inicial do avatar da lista e rótulo do perfil (os mesmos textos do chip de perfil dos Cadastros).
    public string Inicial => string.IsNullOrWhiteSpace(Nome) ? "?" : Nome.Trim()[..1].ToUpperInvariant();
    public string Perfil => Supervisor ? "Supervisor" : "Operador";
}

// Login local, sem rede: o operador escolhe o próprio nome na lista e digita a chave; ela é conferida contra o hash bcrypt
// de pdv_key DELE (que catalog-sync já baixou) — nunca guarda nem recebe a chave em claro de volta.
//
// A escolha do operador existe porque a API não garante chave única: com só a chave, dois operadores com a mesma chave
// entrariam como "o primeiro que bater". Conferir contra UM hash também evita o custo do bcrypt (lento DE PROPÓSITO,
// custo 10 ≈ dezenas de ms) multiplicado pelo número de operadores. Mesmo assim roda fora da thread de UI.
public class LoginOperadorService
{
    private readonly Func<AppDbContext> contextFactory;
    private readonly LimitadorDeTentativas limitador;

    public LoginOperadorService(Func<AppDbContext> contextFactory, TimeProvider? tempo = null)
    {
        this.contextFactory = contextFactory;
        limitador = new LimitadorDeTentativas(tempo);
    }

    // Quanto falta até o login aceitar outra tentativa (zero = pode tentar agora): depois de erros seguidos o app exige
    // uma espera crescente (ver LimitadorDeTentativas). A tela lê isto pra avisar o operador. O limite é do aparelho, não
    // de um operador: trocar de nome na lista não zera a espera.
    public TimeSpan EsperaRestante => limitador.EsperaRestante;

    // Quem consegue entrar: ativo e com hash de chave (sem hash não há como conferir), por ordem de nome.
    public async Task<IReadOnlyList<OperadorLogin>> ListarOperadoresAsync(CancellationToken ct = default)
    {
        await using var context = contextFactory();
        var funcionarios = await context.Funcionarios
            .Where(f => !f.Desativado && f.PdvKeyHash != null)
            .Select(f => new { f.Id, f.Nome, f.Supervisor })
            .ToListAsync(ct);

        return funcionarios
            .OrderBy(f => f.Nome, StringComparer.CurrentCultureIgnoreCase)
            .Select(f => new OperadorLogin(f.Id, f.Nome, f.Supervisor))
            .ToList();
    }

    public async Task<Funcionario?> AutenticarAsync(int funcionarioId, string pdvKeyDigitado, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(pdvKeyDigitado))
            return null;

        // Durante a espera nem confere a chave (nem a certa): não gasta bcrypt e não dá pista de acerto a quem tenta
        // adivinhar. A tentativa também não conta nem estende a espera.
        if (limitador.EsperaRestante > TimeSpan.Zero)
            return null;

        await using var context = contextFactory();
        var funcionario = await context.Funcionarios
            .FirstOrDefaultAsync(f => f.Id == funcionarioId && !f.Desativado && f.PdvKeyHash != null, ct);

        // Operador que sumiu ou foi desativado depois de a lista carregar conta como chave errada: mesma resposta, mesma
        // espera — a tela não revela qual dos dois foi.
        var acertou = funcionario is not null
            && await Task.Run(() => PdvKeyHasher.Verificar(pdvKeyDigitado, funcionario.PdvKeyHash), ct);

        if (acertou)
        {
            limitador.Zerar();
            return funcionario;
        }

        var espera = limitador.RegistrarFalha();
        if (espera > TimeSpan.Zero)
            Registro.Aviso("Auditoria", $"Login: chaves erradas em sequência — nova tentativa só depois de {LimitadorDeTentativas.Descrever(espera)}.");
        return null;
    }
}
