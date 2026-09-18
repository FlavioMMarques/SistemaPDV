using System.Text.Json;
using SistemaPDV.Services.Sales.Dtos;

namespace SistemaPDV.Tests;

public class VendaApiDtoTests
{
    [Fact]
    public void SerializaComNomesDeCampoEmSnakeCase()
    {
        var request = new VendaRequestDto
        {
            Guid = "11111111-1111-1111-1111-111111111111",
            DataHora = 1758000000,
            EmpresaId = 1,
            UsuarioId = 2,
            FuncionarioId = 2,
            ClienteId = 1,
            CaixaData = "2026-09-18 08:00:00",
            CaixaTurno = 1,
            CaixaFuncoesId = null,
            Produtos = { new VendaProdutoRequestDto { ProdutoId = 10, Preco = 9.90m, Quantidade = 2 } },
            Pagamentos = { new VendaPagamentoRequestDto { FormaPagamentoId = 5, ValorPagamento = 19.80m } },
        };

        var json = JsonSerializer.Serialize(request);

        Assert.Contains("\"guid\"", json);
        Assert.Contains("\"data_hora\":1758000000", json);
        Assert.Contains("\"caixa_funcoes_id\":null", json);
        Assert.Contains("\"produto_id\":10", json);
        Assert.Contains("\"forma_pagamento_id\":5", json);
    }

    [Fact]
    public void DeserializaRespostaDeSucessoExtraiId()
    {
        var json = """{ "data": { "id": 42 } }""";

        var resposta = JsonSerializer.Deserialize<VendaRespostaDto>(json);

        Assert.Equal(42, resposta?.Data?.Id);
    }
}
