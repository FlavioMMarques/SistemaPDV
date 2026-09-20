using System;
using System.Collections.Generic;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;

namespace SistemaPDV.ViewModels;

// Clientes e produtos do banco local (leitura + busca) e o formulário de criar
// cliente. Tudo local: criar grava PendenteSync e o cliente aparece na hora com
// 🟡 Pendente — o envio pra API é do serviço de sincronização, nunca desta tela
// (por isso funciona offline e não trava esperando rede).
//
// A busca é manual (Enter/botão), mesma decisão de ListaPedidosViewModel: sem
// debounce automático, o comportamento é previsível e testável.
public class CadastrosViewModel : ViewModelBase, IAtualizavelPorSincronizacao
{
    private readonly CadastroLocalService cadastroLocalService;

    private IReadOnlyList<ClienteResumo> clientes = Array.Empty<ClienteResumo>();
    private IReadOnlyList<ProdutoResumo> produtos = Array.Empty<ProdutoResumo>();
    private string busca = string.Empty;
    private string buscaAplicada = string.Empty;
    private string novoNome = string.Empty;
    private string novoCpfCnpj = string.Empty;
    private string? mensagemForm;
    private bool mensagemFormEhErro;

    public CadastrosViewModel(CadastroLocalService cadastroLocalService)
    {
        this.cadastroLocalService = cadastroLocalService;

        BuscarCommand = ReactiveCommand.CreateFromTask(BuscarAsync);

        var podeCriar = this.WhenAnyValue(vm => vm.NovoNome, nome => !string.IsNullOrWhiteSpace(nome));
        CriarClienteCommand = ReactiveCommand.CreateFromTask(CriarClienteAsync, podeCriar);
    }

    public IReadOnlyList<ClienteResumo> Clientes
    {
        get => clientes;
        private set
        {
            this.RaiseAndSetIfChanged(ref clientes, value);
            this.RaisePropertyChanged(nameof(MensagemVazioClientes));
            this.RaisePropertyChanged(nameof(ClientesCortados));
        }
    }

    public IReadOnlyList<ProdutoResumo> Produtos
    {
        get => produtos;
        private set
        {
            this.RaiseAndSetIfChanged(ref produtos, value);
            this.RaisePropertyChanged(nameof(MensagemVazioProdutos));
            this.RaisePropertyChanged(nameof(ProdutosCortados));
        }
    }

    public string Busca
    {
        get => busca;
        set => this.RaiseAndSetIfChanged(ref busca, value);
    }

    public string NovoNome
    {
        get => novoNome;
        set => this.RaiseAndSetIfChanged(ref novoNome, value);
    }

    public string NovoCpfCnpj
    {
        get => novoCpfCnpj;
        set => this.RaiseAndSetIfChanged(ref novoCpfCnpj, value);
    }

    public string? MensagemForm
    {
        get => mensagemForm;
        private set
        {
            this.RaiseAndSetIfChanged(ref mensagemForm, value);
            RaiseMensagemFormChanged();
        }
    }

    public bool MensagemFormEhErro
    {
        get => mensagemFormEhErro;
        private set
        {
            this.RaiseAndSetIfChanged(ref mensagemFormEhErro, value);
            RaiseMensagemFormChanged();
        }
    }

    // Duas "fatias" da mesma mensagem, só pra tela poder colorir e esconder cada uma
    // com um binding simples (vermelho pro erro, verde pro sucesso).
    public string? MensagemFormErro => MensagemFormEhErro ? MensagemForm : null;
    public string? MensagemFormSucesso => MensagemFormEhErro ? null : MensagemForm;

    // Estado vazio nunca é tela em branco (spec, "Convenções de UI"): null = tem
    // itens (a tela esconde a mensagem), texto = o que mostrar no lugar da lista.
    public string? MensagemVazioClientes => Clientes.Count > 0 ? null : MensagemVazio("cliente");
    public string? MensagemVazioProdutos => Produtos.Count > 0 ? null : MensagemVazio("produto");

    // O serviço corta em LimiteLista; a tela avisa pra ninguém achar que aquilo é tudo.
    public bool ClientesCortados => Clientes.Count >= CadastroLocalService.LimiteLista;
    public bool ProdutosCortados => Produtos.Count >= CadastroLocalService.LimiteLista;
    public string AvisoLimite => $"Mostrando os primeiros {CadastroLocalService.LimiteLista} — use a busca para encontrar os demais.";

    public ReactiveCommand<Unit, Unit> BuscarCommand { get; }
    public ReactiveCommand<Unit, Unit> CriarClienteCommand { get; }

    public Task IniciarAsync() => BuscarAsync();

    // Chamado pelo Shell quando um ciclo de sincronização mexeu no banco. Recarrega com a
    // busca JÁ APLICADA (a do último Enter/botão), não com o que o operador está digitando
    // agora — senão a lista mudaria de filtro sozinha no meio da digitação. O formulário
    // de novo cliente nem é tocado.
    public Task AtualizarAposSincronizacaoAsync() => CarregarAsync();

    private Task BuscarAsync()
    {
        buscaAplicada = Busca;
        return CarregarAsync();
    }

    private async Task CarregarAsync()
    {
        // Duas consultas em sequência (e não Task.WhenAll): cada uma abre seu próprio
        // contexto, mas em SQLite o ganho de paralelizar é nenhum e a ordem fica simples.
        Clientes = await cadastroLocalService.ListarClientesAsync(buscaAplicada);
        Produtos = await cadastroLocalService.ListarProdutosAsync(buscaAplicada);
    }

    private async Task CriarClienteAsync()
    {
        var resultado = await cadastroLocalService.CriarClienteAsync(NovoNome, NovoCpfCnpj);

        if (!resultado.Sucesso)
        {
            // Mantém o que o operador digitou pra ele corrigir sem redigitar.
            MensagemFormEhErro = true;
            MensagemForm = resultado.Mensagem;
            return;
        }

        MensagemFormEhErro = false;
        MensagemForm = "Cliente salvo. Ele será enviado à API na próxima sincronização.";
        NovoNome = string.Empty;
        NovoCpfCnpj = string.Empty;

        // Busca ativa poderia esconder o cliente recém-criado — limpa pra ele aparecer.
        Busca = string.Empty;
        await BuscarAsync();
    }

    private void RaiseMensagemFormChanged()
    {
        this.RaisePropertyChanged(nameof(MensagemFormErro));
        this.RaisePropertyChanged(nameof(MensagemFormSucesso));
    }

    private string MensagemVazio(string item) => string.IsNullOrWhiteSpace(buscaAplicada)
        ? $"Nenhum {item} cadastrado ainda — sincronize com a API ou cadastre o primeiro."
        : $"Nenhum {item} encontrado para essa busca.";
}
