using System;
using System.Collections.Generic;

namespace SistemaPDV.Services.Sync;

public class ResultadoBusca<T>
{
    public bool Sucesso { get; private init; }
    public string? Mensagem { get; private init; }
    public IReadOnlyList<T> Itens { get; private init; } = Array.Empty<T>();
    public long? DateSync { get; private init; }

    public static ResultadoBusca<T> ComSucesso(IReadOnlyList<T> itens, long? dateSync) =>
        new() { Sucesso = true, Itens = itens, DateSync = dateSync };

    public static ResultadoBusca<T> ComFalha(string mensagem) =>
        new() { Sucesso = false, Mensagem = mensagem };
}
