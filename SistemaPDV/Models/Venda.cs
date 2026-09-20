using System;
using System.Collections.Generic;

namespace SistemaPDV.Models;

public class Venda : ISincronizavel<Guid>, IOutboxRetentavel
{
    public Guid Id { get; set; } = Guid.NewGuid();

    // Herdado de ISincronizavel<Guid>, mas sem uso real aqui: a API não devolve um id
    // "casável" nesse padrão pra venda — ela devolve um id de venda próprio. Fica
    // sempre null; o id real do servidor vive em VendaIdExterno. É um pequeno
    // atrito de Interface Segregation (Venda carrega um membro que não usa) aceito
    // conscientemente em troca de manter o mesmo contrato polimórfico das demais
    // entidades sincronizáveis — ver docs/APRENDIZADOS.md.
    public int? IdExterno { get; set; }

    public SyncStatus SyncStatus { get; set; } = SyncStatus.PendenteSync;

    public DateTime DataHora { get; set; }
    public int CaixaId { get; set; }
    public int? ClienteId { get; set; }
    public decimal Desconto { get; set; }

    public string? UltimoErroSync { get; set; }
    public int TentativasEnvio { get; set; }
    public DateTime? ProximaTentativaEm { get; set; }   // ver PoliticaRetentativa

    // Id numérico da venda devolvido pela API em caso de sucesso (200) — distinto de
    // IdExterno acima, de propósito, pra não confundir os dois conceitos.
    public int? VendaIdExterno { get; set; }

    public ICollection<ItemVenda> Itens { get; set; } = new List<ItemVenda>();
    public ICollection<PagamentoVenda> Pagamentos { get; set; } = new List<PagamentoVenda>();
}
