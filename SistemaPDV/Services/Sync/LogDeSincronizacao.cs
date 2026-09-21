using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Subjects;

namespace SistemaPDV.Services.Sync;

public enum NivelAtividade
{
    Info,
    Sucesso,
    Aviso,
    Erro,
}

// Que papel a linha tem no ciclo de envio: o cabeçalho ("Disparando sincronização de N itens..."), o resumo do fim ("Ciclo concluído em
// 1,2 s: 3 enviados") ou um item qualquer (uma venda enviada, uma conexão que caiu) — o painel agrupa e destaca por isso.
public enum MarcaDeLog
{
    Nenhuma,
    InicioDeCiclo,
    FimDeCiclo,
}

// Uma linha do "log de execução" da fila outbox, escrita para o OPERADOR ler ("Pedido #1003 sincronizado com sucesso!"). Não é o
// log técnico (esse é o arquivo do Registro, com exceção e detalhe): aqui só entra o que faz sentido mostrar na tela, e nunca
// token, senha nem detalhe de exceção. O motivo de uma recusa da API aparece como a API o devolveu — o mesmo texto que a lista de
// pedidos já mostra em cada venda em falha —, e o corpo bruto de uma resposta de autenticação que falhou fica de fora.
public sealed record EntradaDeLog(DateTime Quando, NivelAtividade Nivel, string Texto, MarcaDeLog Marca = MarcaDeLog.Nenhuma)
{
    // O painel desenha cada tipo de linha de um jeito (o cabeçalho e o resumo de um ciclo se destacam dos itens).
    public bool EhItem => Marca == MarcaDeLog.Nenhuma;
    public bool EhInicioDeCiclo => Marca == MarcaDeLog.InicioDeCiclo;
    public bool EhFimDeCiclo => Marca == MarcaDeLog.FimDeCiclo;
}

// O que uma etapa do envio conta sobre cada item que tratou (uma linha por venda enviada ou recusada, por exemplo).
public sealed record DetalheDeSincronizacao(NivelAtividade Nivel, string Texto);

// As últimas atividades da sincronização em segundo plano, na memória (some ao fechar o app — para o histórico existe o arquivo
// de log). Quem sincroniza chama Registrar de qualquer thread; a tela recebe pelo Novas e se atualiza na própria thread.
public sealed class LogDeSincronizacao
{
    // Mais que isso é ruído: a tela mostra as mais recentes e o arquivo de log guarda o resto.
    public const int Capacidade = 100;

    private readonly TimeProvider relogio;
    private readonly object trava = new();
    private readonly LinkedList<EntradaDeLog> entradas = new();   // a mais recente primeiro
    private readonly Subject<EntradaDeLog> novas = new();

    public LogDeSincronizacao(TimeProvider? relogio = null)
    {
        this.relogio = relogio ?? TimeProvider.System;
    }

    // Da mais recente para a mais antiga.
    public IReadOnlyList<EntradaDeLog> Recentes
    {
        get
        {
            lock (trava)
                return entradas.ToList();
        }
    }

    public IObservable<EntradaDeLog> Novas => novas;

    public void Registrar(NivelAtividade nivel, string texto, MarcaDeLog marca = MarcaDeLog.Nenhuma)
    {
        var entrada = new EntradaDeLog(relogio.GetLocalNow().DateTime, nivel, texto, marca);

        lock (trava)
        {
            entradas.AddFirst(entrada);
            while (entradas.Count > Capacidade)
                entradas.RemoveLast();

            // Dentro da trava: dois Registrar simultâneos (ciclo + verificação de conexão) não podem chamar OnNext ao mesmo
            // tempo (um Subject do Rx não aceita) nem entregar fora de ordem.
            novas.OnNext(entrada);
        }
    }
}

// O que o envio da fila está fazendo AGORA: o painel mostra "Sincronizando… (Vendas)" com uma barra de progresso e desabilita o botão
// "Disparar" enquanto dura. Só o ciclo que de fato tem o que enviar (e conseguiu autenticar) marca Sincronizando; o ciclo ocioso de
// 30 s não pisca a tela.
public sealed record AndamentoDoEnvio(bool Sincronizando, string? Etapa)
{
    public static readonly AndamentoDoEnvio Ocioso = new(false, null);
}
