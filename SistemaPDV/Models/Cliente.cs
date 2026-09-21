using System;

namespace SistemaPDV.Models;

public class Cliente : ISincronizavel<int>, IOutboxRetentavel
{
    public int Id { get; set; }
    public int? IdExterno { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendenteSync;

    // Só usado quando o cliente é criado localmente e empurrado pra API (Task 48) —
    // mesmo papel de Caixa/Venda.UltimoErroSync: sem isso, um cliente em FalhaSync
    // ficaria indiagnosticável. Truncado (ver ErroApiExtractor.TamanhoMaximo).
    public string? UltimoErroSync { get; set; }

    // Espera crescente/teto de retentativas do envio (ver PoliticaRetentativa).
    public int TentativasEnvio { get; set; }
    public DateTime? ProximaTentativaEm { get; set; }

    public required string Nome { get; set; }
    public string? RazaoSocial { get; set; }
    public TipoPessoa Pessoa { get; set; } = TipoPessoa.Fisica;
    public string? CpfCnpj { get; set; }
    public string? Rg { get; set; }
    public string? InscricaoEstadual { get; set; }
    public string? InscricaoMunicipal { get; set; }
    public string? ContribuinteIcms { get; set; }
    public int IndicadorFinalidade { get; set; }
    public bool Bloqueado { get; set; }
    public string? Observacao { get; set; }

    // A API nunca devolveu um valor não-nulo pra essas duas nos exemplos que vimos —
    // DateOnly é a leitura mais correta pro conceito ("data de nascimento"/"data de
    // fundação" não têm hora), mas fica marcado como suposição a confirmar quando
    // catalog-sync de fato receber um valor real da API pra converter.
    public DateOnly? DataNascimento { get; set; }
    public DateOnly? DataFundacao { get; set; }

    public string? ContatoNome { get; set; }
    public string? ContatoDdd { get; set; }
    public string? ContatoTelefone { get; set; }
    public string? ContatoEmail { get; set; }

    public string? Cep { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? PontoReferencia { get; set; }
    public string? Cidade { get; set; }
    public string? CidadeId { get; set; }

    // Código da cidade no IBGE (7 dígitos, ex: 2507507 = João Pessoa) — o "c_cidade" que a API pede no endereço do cliente novo, vindo do
    // ViaCEP. É diferente do CidadeId acima (número interno da API, ex: 1336 para João Pessoa). Clientes que vieram da API trazem o
    // "codigo_cidade" aqui.
    public string? CodigoCidade { get; set; }
    public string? Uf { get; set; }

    public string? TipoClienteId { get; set; }
    public string? TipoClienteNome { get; set; }
    public int? FuncionarioId { get; set; }
    public string? FuncionarioNome { get; set; }

    public TabelaPreco? TabelaPreco { get; set; }
}
