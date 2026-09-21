using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using ReactiveUI;

namespace SistemaPDV.ViewModels;

// Fila dos avisos temporários da tela. Quem tem o que avisar chama Publicar; a tela (ShellView) só mostra Ativos.
public class ToastCentral
{
    // Quanto o aviso fica visível, e quanto dura o esmaecer antes de sair da lista (o mesmo tempo da transição no XAML).
    public static readonly TimeSpan DuracaoPadrao = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan DuracaoDoEsmaecer = TimeSpan.FromMilliseconds(300);

    // Mais que isso empilhado vira ruído (e tampa a tela): o mais antigo sai.
    private const int MaximoVisivel = 4;

    private readonly IScheduler? relogio;

    // relogio: só os testes passam um (um HistoricalScheduler, para avançar o tempo sem esperar). Em produção os prazos
    // correm num relógio de fundo e a retirada da lista volta para a thread da interface (a lista é ligada à tela).
    public ToastCentral(IScheduler? relogio = null)
    {
        this.relogio = relogio;
    }

    public ObservableCollection<Toast> Ativos { get; } = new();

    public Toast Publicar(string texto, string icone, ToastTipo tipo = ToastTipo.Neutro, string? chave = null, TimeSpan? duracao = null)
    {
        var toast = new Toast(texto, icone, tipo, chave);

        if (chave is not null)
        {
            foreach (var anterior in Ativos.Where(t => t.Chave == chave).ToList())
                Ativos.Remove(anterior);
        }

        Ativos.Add(toast);
        while (Ativos.Count > MaximoVisivel)
            Ativos.RemoveAt(0);

        // Toast já removido (substituído, ou expulso pelo limite) faz esses passos virarem no-op.
        Agendar(duracao ?? DuracaoPadrao, () => toast.Saindo = true);
        Agendar((duracao ?? DuracaoPadrao) + DuracaoDoEsmaecer, () => Ativos.Remove(toast));
        return toast;
    }

    private void Agendar(TimeSpan espera, Action acao)
    {
        var timer = relogio is null
            ? Observable.Timer(espera, TaskPoolScheduler.Default).ObserveOn(RxSchedulers.MainThreadScheduler)
            : Observable.Timer(espera, relogio);
        timer.Subscribe(_ => acao());
    }
}
