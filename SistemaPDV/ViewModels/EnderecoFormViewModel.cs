using System;
using System.Reactive;
using System.Reactive.Subjects;
using System.Threading.Tasks;
using ReactiveUI;
using SistemaPDV.Services;

namespace SistemaPDV.ViewModels;

// O bloco de endereço do modal "Cadastrar Cliente": CEP (com botão Buscar), endereço, número, complemento, bairro e cidade/UF.
// Buscar o CEP preenche o endereço e a cidade e guarda o CÓDIGO IBGE da cidade — o "c_cidade" que a API pede no lugar do nome. É só
// conveniência (precisa de internet): sem a consulta o operador digita o endereço e a cidade fica só local, sem código.
//
// O código pertence à cidade que a consulta trouxe: se o operador troca o texto de Cidade/UF depois, o código deixa de valer e
// NÃO vai para a API (ver CodigoCidadeParaSalvar) — um código de outra cidade seria pior que nenhum.
public class EnderecoFormViewModel : ViewModelBase
{
    private readonly CepService? cepService;
    private readonly Subject<Unit> cepEncontrado = new();

    private string cep = string.Empty;
    private string logradouro = string.Empty;
    private string numero = string.Empty;
    private string complemento = string.Empty;
    private string bairro = string.Empty;
    private string cidadeUf = string.Empty;
    private bool buscando;
    private string? mensagemCep;
    private bool mensagemCepEhErro;

    // O que a última consulta bem-sucedida trouxe: o código só vale enquanto Cidade/UF continuar igual ao texto da consulta.
    private string cidadeUfDaConsulta = string.Empty;
    private string codigoCidadeDaConsulta = string.Empty;

    public EnderecoFormViewModel(CepService? cepService)
    {
        this.cepService = cepService;

        var podeBuscar = this.WhenAnyValue(vm => vm.Cep, vm => vm.Buscando, (cepDigitado, ocupado) => !string.IsNullOrWhiteSpace(cepDigitado) && !ocupado);
        BuscarCepCommand = ReactiveCommand.CreateFromTask(BuscarAsync, podeBuscar);
    }

    // Sem o serviço (só nos testes de outras telas) a busca não existe e o botão fica desabilitado.
    public bool CepDisponivel => cepService is not null;

    public string Cep
    {
        get => cep;
        set => this.RaiseAndSetIfChanged(ref cep, value);
    }

    public string Logradouro
    {
        get => logradouro;
        set => this.RaiseAndSetIfChanged(ref logradouro, value);
    }

    public string Numero
    {
        get => numero;
        set => this.RaiseAndSetIfChanged(ref numero, value);
    }

    public string Complemento
    {
        get => complemento;
        set => this.RaiseAndSetIfChanged(ref complemento, value);
    }

    public string Bairro
    {
        get => bairro;
        set => this.RaiseAndSetIfChanged(ref bairro, value);
    }

    // "João Pessoa - PB". Já abre com a cidade da empresa (ver CadastrosViewModel.AbrirFormularioAsync), sem código.
    public string CidadeUf
    {
        get => cidadeUf;
        set
        {
            this.RaiseAndSetIfChanged(ref cidadeUf, value);
            this.RaisePropertyChanged(nameof(CodigoCidadeParaSalvar));
            this.RaisePropertyChanged(nameof(TextoCodigoCidade));
        }
    }

    public bool Buscando
    {
        get => buscando;
        private set => this.RaiseAndSetIfChanged(ref buscando, value);
    }

    // Uma mensagem só (erro em vermelho, aviso em verde): a tela mostra abaixo do CEP.
    public string? MensagemCep
    {
        get => mensagemCep;
        private set
        {
            this.RaiseAndSetIfChanged(ref mensagemCep, value);
            this.RaisePropertyChanged(nameof(MensagemCepErro));
            this.RaisePropertyChanged(nameof(MensagemCepAviso));
        }
    }

    public bool MensagemCepEhErro
    {
        get => mensagemCepEhErro;
        private set
        {
            this.RaiseAndSetIfChanged(ref mensagemCepEhErro, value);
            this.RaisePropertyChanged(nameof(MensagemCepErro));
            this.RaisePropertyChanged(nameof(MensagemCepAviso));
        }
    }

    public string? MensagemCepErro => MensagemCepEhErro ? MensagemCep : null;
    public string? MensagemCepAviso => MensagemCepEhErro ? null : MensagemCep;

    // O código IBGE que vai junto do cadastro: o da última consulta, SÓ se a cidade não foi trocada à mão depois. Vazio = a cidade
    // segue no banco local mas não é enviada (a API pede o código, não o nome).
    public string CodigoCidadeParaSalvar =>
        codigoCidadeDaConsulta.Length > 0 && string.Equals(CidadeUf.Trim(), cidadeUfDaConsulta, StringComparison.OrdinalIgnoreCase)
            ? codigoCidadeDaConsulta
            : string.Empty;

    // Mostrado sob o campo, para o operador ver que a cidade foi vinculada ao código (ou que não foi).
    public string TextoCodigoCidade => CodigoCidadeParaSalvar.Length > 0
        ? $"Código da cidade (IBGE): {CodigoCidadeParaSalvar} — vai para a API junto do endereço."
        : string.IsNullOrWhiteSpace(CidadeUf)
            ? "Busque o CEP para preencher o endereço e vincular o código da cidade."
            : "Cidade sem código: fica só neste aparelho. Busque o CEP para enviá-la à API.";

    public ReactiveCommand<Unit, Unit> BuscarCepCommand { get; }

    // A tela ouve isto para levar o cursor ao campo Número (o próximo passo depois de achar o CEP).
    public IObservable<Unit> CepEncontrado => cepEncontrado;

    // Volta tudo ao branco (o modal sempre abre limpo). Cidade/UF também: a próxima abertura traz de novo a da empresa.
    public void Limpar()
    {
        Cep = string.Empty;
        Logradouro = string.Empty;
        Numero = string.Empty;
        Complemento = string.Empty;
        Bairro = string.Empty;
        CidadeUf = string.Empty;
        cidadeUfDaConsulta = string.Empty;
        codigoCidadeDaConsulta = string.Empty;
        MensagemCepEhErro = false;
        MensagemCep = null;
        this.RaisePropertyChanged(nameof(CodigoCidadeParaSalvar));
        this.RaisePropertyChanged(nameof(TextoCodigoCidade));
    }

    private async Task BuscarAsync()
    {
        if (cepService is null)
        {
            MensagemCepEhErro = true;
            MensagemCep = "A consulta de CEP não está disponível. Preencha o endereço manualmente.";
            return;
        }

        MensagemCep = null;
        Buscando = true;
        try
        {
            var resultado = await cepService.BuscarAsync(Cep);
            if (!resultado.Sucesso)
            {
                // Nada é apagado: o que o operador já digitou continua, e ele preenche o resto à mão.
                MensagemCepEhErro = true;
                MensagemCep = resultado.Mensagem;
                return;
            }

            var e = resultado.Endereco!;
            Cep = $"{e.Cep[..5]}-{e.Cep[5..]}";

            // CEP "geral" (cidade pequena, um CEP só) vem sem logradouro/bairro: não apaga o que já estava digitado.
            if (e.Logradouro.Length > 0) Logradouro = e.Logradouro;
            if (e.Bairro.Length > 0) Bairro = e.Bairro;
            if (e.Complemento.Length > 0 && Complemento.Length == 0) Complemento = e.Complemento;

            var cidadeTexto = CadastroLocalService.CidadeUf(e.Cidade, e.Uf) ?? string.Empty;
            cidadeUfDaConsulta = cidadeTexto;
            codigoCidadeDaConsulta = e.CodigoCidade;
            CidadeUf = cidadeTexto;

            MensagemCepEhErro = false;
            MensagemCep = e.CodigoCidade.Length > 0
                ? "Endereço preenchido — confira e informe o número."
                : "Endereço preenchido, mas o CEP não trouxe o código da cidade: ela ficará só neste aparelho.";
            cepEncontrado.OnNext(Unit.Default);
        }
        finally
        {
            Buscando = false;
        }
    }
}
