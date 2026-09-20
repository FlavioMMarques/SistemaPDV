using System;

namespace SistemaPDV.Services;

// Ponto de acesso ao log para quem não recebe um LogArquivo por construtor (serviços de sincronização, tratamento de
// erros, ViewModels). Mesma ideia de TratamentoDeErros: o App define o Destino ao abrir; sem Destino (testes, ou log
// que não pôde ser criado) tudo aqui é no-op — nunca escreve arquivo por acidente numa suíte de testes.
public static class Registro
{
    private static volatile LogArquivo? destino;

    public static LogArquivo? Destino
    {
        get => destino;
        set => destino = value;
    }

    public static void Info(string origem, string mensagem) => destino?.Registrar(NivelLog.Info, origem, mensagem);

    public static void Aviso(string origem, string mensagem) => destino?.Registrar(NivelLog.Aviso, origem, mensagem);

    public static void Erro(string origem, string mensagem, Exception? excecao = null) =>
        destino?.Registrar(NivelLog.Erro, origem, mensagem, excecao);
}
