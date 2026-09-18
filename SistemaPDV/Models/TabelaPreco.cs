namespace SistemaPDV.Models;

// Owned type (não tem DbSet próprio, não tem Id independente que importe pra fora do
// Cliente) — é um valor que descreve o cliente, não uma entidade com vida própria.
public class TabelaPreco
{
    public int? IdExterno { get; set; }

    // Obrigatório (não string?) de propósito: um owned type opcional precisa de pelo
    // menos uma propriedade não anulável pro EF Core conseguir distinguir "Cliente
    // tem TabelaPreco com campos vazios" de "Cliente não tem TabelaPreco nenhum" —
    // com tudo anulável, as duas situações ficam idênticas no banco. Bate com a API
    // real, que sempre manda uma descrição (ex: "PADRAO").
    public required string Descricao { get; set; }
}
