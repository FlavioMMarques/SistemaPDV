using System;
using System.Collections.Generic;

namespace SistemaPDV.Services.Sync;

public class ResultadoSincronizacaoRecurso
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public int Quantidade { get; private init; }

    // O que a etapa conta sobre cada item que tratou (só o lote de vendas preenche): vira linhas do log da fila outbox.
    public IReadOnlyList<DetalheDeSincronizacao> Detalhes { get; private init; } = Array.Empty<DetalheDeSincronizacao>();

    public static ResultadoSincronizacaoRecurso ComSucesso(int quantidade, IReadOnlyList<DetalheDeSincronizacao>? detalhes = null) =>
        new() { Sucesso = true, Quantidade = quantidade, Detalhes = detalhes ?? Array.Empty<DetalheDeSincronizacao>() };

    public static ResultadoSincronizacaoRecurso ComFalha(string mensagem) =>
        new() { Sucesso = false, Mensagem = mensagem };
}
