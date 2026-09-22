using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;

namespace SistemaPDV.ViewModels;

// O modal "Cadastrar Produto no Banco Local" (aberto por "＋ Novo Produto" em Cadastros): nome, código, categoria, preço de venda e de custo e estoque
// inicial. Só grava local (PendenteSync) — o envio à API é do outbox (CatalogSyncService.SincronizarProdutoNovoAsync), então
// funciona offline. Filho do CadastrosViewModel (que já é grande): quem abre é o pai; ao salvar, o pai é avisado por
// `aoSalvar` para recarregar a lista.
public class ProdutoFormViewModel : ViewModelBase
{
    private readonly CadastroLocalService cadastroLocalService;
    private readonly Func<Task> aoSalvar;

    private bool aberto;
    private string nome = string.Empty;
    private string codigo = string.Empty;
    private string preco = string.Empty;
    private string precoCusto = string.Empty;
    private string estoque = string.Empty;
    private IReadOnlyList<CategoriaResumo> categorias = Array.Empty<CategoriaResumo>();
    private CategoriaResumo? categoriaSelecionada;
    private string? mensagem;

    public ProdutoFormViewModel(CadastroLocalService cadastroLocalService, Func<Task> aoSalvar)
    {
        this.cadastroLocalService = cadastroLocalService;
        this.aoSalvar = aoSalvar;

        // Nome, categoria e preço são o mínimo que a API aceita; o resto (código e estoque) é opcional.
        var podeSalvar = this.WhenAnyValue(vm => vm.Nome, vm => vm.CategoriaSelecionada, vm => vm.Preco,
            (n, categoria, p) => !string.IsNullOrWhiteSpace(n) && categoria is not null && !string.IsNullOrWhiteSpace(p));
        SalvarCommand = ReactiveCommand.CreateFromTask(SalvarAsync, podeSalvar);
        CancelarCommand = ReactiveCommand.Create(Fechar);
    }

    // O modal sempre abre limpo; a tela de trás fica desabilitada enquanto está aberto (ver CadastrosViewModel.ModalAberto).
    public bool Aberto
    {
        get => aberto;
        private set => this.RaiseAndSetIfChanged(ref aberto, value);
    }

    public string Nome
    {
        get => nome;
        set => this.RaiseAndSetIfChanged(ref nome, value);
    }

    // "SKU / Código": vira código de barras (8 a 14 dígitos) ou referência (até 20 caracteres) — ver CadastroLocalService.CriarProdutoAsync.
    public string Codigo
    {
        get => codigo;
        set => this.RaiseAndSetIfChanged(ref codigo, value);
    }

    public string Preco
    {
        get => preco;
        set => this.RaiseAndSetIfChanged(ref preco, value);
    }

    // Opcional: o custo fica no cadastro e vai à API junto com o produto quando informado.
    public string PrecoCusto
    {
        get => precoCusto;
        set => this.RaiseAndSetIfChanged(ref precoCusto, value);
    }

    public string Estoque
    {
        get => estoque;
        set => this.RaiseAndSetIfChanged(ref estoque, value);
    }

    public IReadOnlyList<CategoriaResumo> Categorias
    {
        get => categorias;
        private set
        {
            this.RaiseAndSetIfChanged(ref categorias, value);
            this.RaisePropertyChanged(nameof(SemCategorias));
        }
    }

    // Sem a escolha o botão fica desabilitado: a API exige uma categoria (grupo) que já exista lá; nada é adivinhado.
    public CategoriaResumo? CategoriaSelecionada
    {
        get => categoriaSelecionada;
        set => this.RaiseAndSetIfChanged(ref categoriaSelecionada, value);
    }

    // Nenhum grupo sincronizado ainda (1ª vez no aparelho, ou a API não tem grupos): o modal avisa em vez de mostrar um combo vazio.
    public bool SemCategorias => Categorias.Count == 0;

    // Erro de validação do serviço (aparece dentro do modal, que segue aberto com o que foi digitado).
    public string? Mensagem
    {
        get => mensagem;
        private set => this.RaiseAndSetIfChanged(ref mensagem, value);
    }

    public ReactiveCommand<Unit, Unit> SalvarCommand { get; }
    public ReactiveCommand<Unit, Unit> CancelarCommand { get; }

    public async Task AbrirAsync()
    {
        Limpar();
        Categorias = await cadastroLocalService.ListarCategoriasAsync();
        Aberto = true;
    }

    private void Fechar()
    {
        Aberto = false;
        Limpar();
    }

    private void Limpar()
    {
        Nome = string.Empty;
        Codigo = string.Empty;
        Preco = string.Empty;
        PrecoCusto = string.Empty;
        Estoque = string.Empty;
        CategoriaSelecionada = null;
        Mensagem = null;
    }

    private async Task SalvarAsync()
    {
        var resultado = await cadastroLocalService.CriarProdutoAsync(
            new NovoProdutoDados(Nome, Codigo, CategoriaSelecionada?.GrupoId, Preco, Estoque, PrecoCusto));

        if (!resultado.Sucesso)
        {
            // Mantém o modal aberto e o que o operador digitou pra ele corrigir sem redigitar.
            Mensagem = resultado.Mensagem;
            return;
        }

        Fechar();
        await aoSalvar();
    }
}
