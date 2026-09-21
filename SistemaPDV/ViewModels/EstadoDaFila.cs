namespace SistemaPDV.ViewModels;

// Como a fila outbox está AGORA, numa palavra — o cartão de estado do painel lateral mostra ícone + título + detalhe (nunca só cor).
public enum EstadoDaFila
{
    // Nada esperando para ir à API.
    TudoEnviado,

    // Há itens pendentes e nenhum envio em curso (o próximo ciclo de 30 s, ou "Disparar agora", leva).
    Aguardando,

    // Um ciclo está enviando neste momento.
    Sincronizando,

    // Sem internet: o que está na fila sai quando ela voltar.
    SemConexao,
}

// Um chip do painel: o que está pendente, por tipo ("🧾 Vendas 2"). Só os tipos com quantidade maior que zero viram chip.
public sealed record PendenciaChip(string Icone, string Rotulo, int Quantidade);
