using System.Collections.Generic;

namespace SistemaPDV.Models;

public class Produto : ISincronizavel<int>
{
    public int Id { get; set; }
    public int? IdExterno { get; set; }
    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendenteSync;

    public string? Sku { get; set; }
    public string? CodigoBarras { get; set; }
    public required string Nome { get; set; }
    public string? NomeOriginal { get; set; }
    public string? Fabricante { get; set; }
    public string? Referencia { get; set; }
    public int? GrupoId { get; set; }

    public int EstoqueAtual { get; set; }
    public string? UnidadeMedida { get; set; }
    public decimal? Peso { get; set; }

    public decimal PrecoVenda { get; set; }
    public decimal? PrecoCompra { get; set; }
    public decimal? MargemLucro { get; set; }

    // Fiscal essencial (retail genérico) — deliberadamente sem o bloco "especifico"
    // (combustível: ANP/GLP/CIDE) nem campos de restaurante (self_service, KDS,
    // comanda, combo) presentes no schema completo da API: fora de escopo do
    // protótipo, ver docs/APRENDIZADOS.md e softcomshop-api-contract na memória.
    public string? Ncm { get; set; }
    public string? Cest { get; set; }
    public string? CodigoBeneficioFiscal { get; set; }
    public int StatusFiscal { get; set; }
    public string? CodigoNfe { get; set; }

    public bool Vender { get; set; } = true;
    public bool RestricaoIdade { get; set; }
    public bool Hortifruit { get; set; }
    public string? Observacao { get; set; }

    public decimal? PromocaoPreco { get; set; }
    public string? PromocaoValidade { get; set; }
    public int? PromocaoQuantidade { get; set; }

    public ICollection<ImagemProduto> Imagens { get; set; } = new List<ImagemProduto>();
}
