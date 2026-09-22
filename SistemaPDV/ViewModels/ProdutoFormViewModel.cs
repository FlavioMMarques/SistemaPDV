using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;

namespace SistemaPDV.ViewModels;

// O modal "Cadastrar Produto no Banco Local" (aberto por "＋ Novo Produto" em Cadastros): nome, código de barras, referência,
// categoria, unidade de medida, preço de venda e de custo. Sem campo de estoque — a API não grava estoque no cadastro (o
// produto chega com estoque 0 na empresa; ver
// CatalogSyncService.SincronizarProdutoNovoAsync e APRENDIZADOS). Só grava local (PendenteSync) — o envio à API é do outbox, então
// funciona offline. Filho do CadastrosViewModel (que já é grande): quem abre é o pai; ao salvar, o pai é avisado por
// `aoSalvar` para recarregar a lista.
public class ProdutoFormViewModel : ViewModelBase
{
    private readonly CadastroLocalService cadastroLocalService;
    private readonly Func<Task> aoSalvar;

    private bool aberto;
    private string nome = string.Empty;
    private string codigoBarras = string.Empty;
    private string referencia = string.Empty;
    private string preco = string.Empty;
    private string precoCusto = string.Empty;
    private string unidadeMedida = string.Empty;
    private IReadOnlyList<CategoriaResumo> categorias = Array.Empty<CategoriaResumo>();
    private CategoriaResumo? categoriaSelecionada;
    private IReadOnlyList<string> unidadesDeMedida = Array.Empty<string>();
    private string? mensagem;

    public ProdutoFormViewModel(CadastroLocalService cadastroLocalService, Func<Task> aoSalvar)
    {
        this.cadastroLocalService = cadastroLocalService;
        this.aoSalvar = aoSalvar;

        // Nome, categoria e preço são o mínimo que a API aceita; o resto (código e custo) é opcional.
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

    // Opcional: 8 a 14 dígitos (EAN-8 a GTIN-14) — ver CadastroLocalService.CriarProdutoAsync.
    public string CodigoBarras
    {
        get => codigoBarras;
        set => this.RaiseAndSetIfChanged(ref codigoBarras, value);
    }

    // Opcional: até 20 caracteres, livre (letras e números).
    public string Referencia
    {
        get => referencia;
        set => this.RaiseAndSetIfChanged(ref referencia, value);
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

    // Opcional; combo editável — sugere as já usadas (UnidadesDeMedida) mas aceita digitar uma nova.
    public string UnidadeMedida
    {
        get => unidadeMedida;
        set => this.RaiseAndSetIfChanged(ref unidadeMedida, value);
    }

    // Unidades já usadas em algum produto (UN, KG, PC…), carregadas ao abrir o modal — poupa o operador de lembrar o código certo.
    public IReadOnlyList<string> UnidadesDeMedida
    {
        get => unidadesDeMedida;
        private set => this.RaiseAndSetIfChanged(ref unidadesDeMedida, value);
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
        UnidadesDeMedida = await cadastroLocalService.ListarUnidadesDeMedidaAsync();
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
        CodigoBarras = string.Empty;
        Referencia = string.Empty;
        Preco = string.Empty;
        PrecoCusto = string.Empty;
        UnidadeMedida = string.Empty;
        CategoriaSelecionada = null;
        Mensagem = null;
    }

    private async Task SalvarAsync()
    {
        var resultado = await cadastroLocalService.CriarProdutoAsync(
            new NovoProdutoDados(Nome, CodigoBarras, Referencia, CategoriaSelecionada?.GrupoId, Preco, PrecoCusto, UnidadeMedida));

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
