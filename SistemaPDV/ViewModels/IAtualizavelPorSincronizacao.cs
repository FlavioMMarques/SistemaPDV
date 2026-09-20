using System.Threading.Tasks;

namespace SistemaPDV.ViewModels;

// Telas que listam dados que a sincronização em background pode mudar (status 🟡 -> 🟢,
// itens novos do catálogo) implementam isto; o Shell chama quando um ciclo mexeu no
// banco. Fica no Shell (e não em cada ViewModel assinando um evento) porque os
// ViewModels são recriados a cada navegação — uma assinatura por tela vazaria.
//
// Só entra quem pode recarregar sem atrapalhar o operador: lista de pedidos e cadastros
// sim; a tela de venda não (recarregar o catálogo no meio de um carrinho seria pior que
// deixar desatualizado).
public interface IAtualizavelPorSincronizacao
{
    Task AtualizarAposSincronizacaoAsync();
}
