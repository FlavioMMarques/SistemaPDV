namespace SistemaPDV.Models;

public class Empresa : ISincronizavel<int>
{
    public int Id { get; set; }
    public int? IdExterno { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendenteSync;

    public required string RazaoSocial { get; set; }
    public string? NomeFantasia { get; set; }
    public required string Cnpj { get; set; }
    public string? Email { get; set; }

    public string? Cep { get; set; }
    public string? Endereco { get; set; }
    public string? Numero { get; set; }
    public string? Complemento { get; set; }
    public string? Bairro { get; set; }
    public string? Cidade { get; set; }
    public string? Uf { get; set; }

    public bool ModuloFiscal { get; set; }
    public int NfceSerie { get; set; }
    public int NfceNumeroCaixa { get; set; }
    public int NfceAmbiente { get; set; }
    public int NfceModelo { get; set; }
    public int NfceProximoNumero { get; set; }

    // Nunca guarda o certificado A1/senha em texto puro — protegido via DPAPI quando
    // catalog-sync popular esse campo (ver SPEC-data-layer.md, Open Questions #4).
    public string? CertificadoProtegido { get; set; }

    public override string ToString() => $"Empresa {Id}: {RazaoSocial} ({Cnpj})";
}
