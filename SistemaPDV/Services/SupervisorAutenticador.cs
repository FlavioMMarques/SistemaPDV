using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;

namespace SistemaPDV.Services;

// Confere a chave (pdv_key) digitada contra os SUPERVISORES ativos — usado por toda ação que só um supervisor pode
// autorizar (descartar venda, abrir as Configurações). Um lugar só, pra a regra não divergir entre os fluxos.
public static class SupervisorAutenticador
{
    // Devolve o supervisor dono da chave, ou null. Chave errada, chave de quem não é supervisor e supervisor desativado
    // dão o MESMO resultado (null): quem chama mostra uma mensagem só, sem revelar se a chave existe. bcrypt é lento
    // de propósito, então a conferência roda fora da thread de UI.
    public static async Task<Funcionario?> AutenticarAsync(AppDbContext context, string? chave, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(chave))
            return null;

        var supervisores = await context.Funcionarios
            .Where(f => f.Supervisor && !f.Desativado && f.PdvKeyHash != null)
            .ToListAsync(ct);

        return await Task.Run(() => supervisores.FirstOrDefault(f => PdvKeyHasher.Verificar(chave, f.PdvKeyHash)), ct);
    }
}
