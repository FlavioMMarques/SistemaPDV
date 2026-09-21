using ReactiveUI;

namespace SistemaPDV.ViewModels;

public enum ToastTipo
{
    Neutro,
    Sucesso,
    Erro,
}

// Um aviso temporário no canto da tela ("Internet desconectada…", "Sabonete adicionado ao cupom"). Some sozinho: primeiro
// vira Saindo (a tela faz o esmaecer), depois a ToastCentral o retira da lista.
public class Toast : ReactiveObject
{
    private bool saindo;

    public Toast(string texto, string icone, ToastTipo tipo, string? chave)
    {
        Texto = texto;
        Icone = icone;
        Tipo = tipo;
        Chave = chave;
    }

    public string Texto { get; }
    public string Icone { get; }
    public ToastTipo Tipo { get; }

    // Avisos da mesma família ("item adicionado") trocam de lugar em vez de empilhar: quem bipa 10 produtos seguidos vê
    // um aviso só, sempre o último. Null = nunca substitui nem é substituído.
    public string? Chave { get; }

    public bool EhSucesso => Tipo == ToastTipo.Sucesso;
    public bool EhErro => Tipo == ToastTipo.Erro;

    public bool Saindo
    {
        get => saindo;
        internal set => this.RaiseAndSetIfChanged(ref saindo, value);
    }
}
