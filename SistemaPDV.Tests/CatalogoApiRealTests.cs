using System.Net;
using System.Net.Http;
using System.Runtime.Versioning;
using System.Text;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync;

namespace SistemaPDV.Tests;

// Contrato com a API SoftcomShop REAL. Estes testes existem porque a sincronização
// passou por 5 tarefas "verdes" contra um servidor falso permissivo e NUNCA funcionou de
// verdade (descoberto em 2026-09-20): 5 dos 7 caminhos estavam sem "softauth/", formas de
// pagamento vêm embrulhadas num array, `bloqueado` é booleano, `estoque` é texto e
// `pdv_key` é hash bcrypt. Aqui o servidor falso imita o real: rota fora de
// "softauth/api/v2/" = 500 {"error":""}, e as respostas têm as formas observadas.
[SupportedOSPlatform("windows")]
public class CatalogoApiRealTests
{
    private const string UrlApi = "https://exemplo.softcomshop.com.br/registrar?client_id=1";

    private static HttpResponseMessage Json(HttpStatusCode status, string json) => new(status)
    {
        Content = new StringContent(json, Encoding.UTF8, "application/json"),
    };

    private const string FormasPagamentoReal = """
        [{"current_page":1,"data":[
          {"id":5,"nome":"ESPÉCIE","tipo":"ESPECIE","padrao":true,"codigo_nfce":"01","codigo_transacao_sitef":null,"carteira_digital":false,"ordem":"1","pdv_pos":false,"pre_venda":false,"atalho_numero":"1","permissao_supervisor":false},
          {"id":6,"nome":"DUPLICATA","tipo":"DUPLICATA","padrao":true,"codigo_nfce":"99","codigo_transacao_sitef":null,"carteira_digital":false,"ordem":"4","pdv_pos":false,"pre_venda":true,"atalho_numero":"4","permissao_supervisor":false}
        ],"next_page_url":null,"total":2,"date_sync":1789924589}]
        """;

    private const string ClientesReal = """
        {"current_page":1,"data":[{"id":1,"pessoa":"FISICA","cpf_cnpj":"","inscricao_estadual":null,"inscricao_municipal":null,"rg":"","nome":"CONSUMIDOR","razao_social":null,
        "data_fundacao":null,"data_nascimento":null,"contribuinte_icms":"9","indicador_finalidade":1,"bloqueado":false,"endereco":null,"numero":null,"complemento":null,
        "ponto_referencia":null,"bairro":null,"cidade":null,"codigo_cidade":null,"cidade_id":null,"uf":null,"cep":"","contato_nome":"Balcao","contato_ddd":"11","contato_telefone":"99999999",
        "contato_email":null,"contato_nascimento":null,"observacao":"","area_id":null,"area_nome":null,"tipo_cliente_id":"1","tipo_cliente_nome":"CONSUMIDOR","funcionario_id":null,
        "funcionario_nome":null,"detalhe_financeiro":null,"id_estrangeiro":null,"tabela_preco":{"id":null,"descricao":"PADRAO"}}],
        "next_page_url":null,"total":1,"date_sync":1789924589}
        """;

    private const string ProdutosReal = """
        {"current_page":1,"data":[{"id":1,"empresa_id":1,"produto_id":1,"produto_empresa_id":1,"sku":"REF2L","sku_atributo":[],"codigo_barras_grade":"1","estoque":"15.000",
        "fabricante":null,"nome_original":"Refrigerante 2L","nome":"Refrigerante 2L","fabricante_id":null,"codigo_barras":null,"referencia":null,"grupo_id":1,"hortifruit":false,
        "restricao_idade":false,"observacao":null,"unidade_medida":"UN","peso":"1.00","margem_lucro":"30.0","ncm":"22021000","cest":null,"vender":true,"origem":"0",
        "preco_venda":"9.9000","preco_compra":"6.0000","tributos_estaduais":"0.00","status_fiscal":"0","controlar_estoque":true,"servico":false,"codigo_beneficio_fiscal":null,
        "vinculos_fiscais":[{"id":1},{"id":2}],"tabela_precos":[],"produto_imagem":[],"especifico":null,"balanca":false,"balanca_tara":"0","tipo_produto":"MERCADORIA"}],
        "next_page_url":null,"total":1,"date_sync":1789924589}
        """;

    private static async Task SemearConfiguracaoAsync(SqliteInMemoryFixture fixture)
    {
        await using var context = fixture.CriarContexto();
        context.ConfiguracoesSincronizacao.Add(new ConfiguracaoSincronizacao { UrlApi = UrlApi });
        await context.SaveChangesAsync();
    }

    // "Servidor" que se comporta como o real: só existe o que está sob softauth/api/v2/.
    private static HttpClient ServidorReal(List<string> visitadas, Func<string, string?>? respostas = null) =>
        FakeHttpMessageHandler.CriarHttpClient(requisicao =>
        {
            var caminho = requisicao.RequestUri!.AbsolutePath.TrimStart('/');
            visitadas.Add($"{requisicao.Method} {caminho}");

            if (!caminho.StartsWith("softauth/api/v2/") && !caminho.EndsWith("authentication/token"))
                return Json(HttpStatusCode.InternalServerError, """{"error":""}""");

            if (caminho.EndsWith("authentication/token"))
                return Json(HttpStatusCode.OK, """{"data":{"token":"t"}}""");

            var corpo = respostas?.Invoke(caminho) ?? caminho switch
            {
                var c when c.EndsWith("forma-pagamento/page/1") => FormasPagamentoReal,
                var c when c.EndsWith("clientes/clientes") => ClientesReal,
                var c when c.EndsWith("produtos/produtos") => ProdutosReal,
                var c when c.EndsWith("/funcionarios") => """{"current_page":1,"data":[],"next_page_url":null,"total":0,"date_sync":1789924589}""",
                var c when c.EndsWith("empresa/empresas/1") => """{"current_page":1,"data":[],"next_page_url":null,"total":0,"date_sync":1789924589}""",
                _ => "{}",
            };
            return Json(HttpStatusCode.OK, corpo);
        });

    private static CatalogSyncService CriarService(SqliteInMemoryFixture fixture, HttpClient httpClient) =>
        new(fixture.CriarContexto, new SoftcomApiClient(httpClient), new SegredoProtector(), new SoftcomAuthService(httpClient, new SegredoProtector()));

    [Fact]
    public void TodasAsRotasFicamSobSoftauthApiV2()
    {
        const string dominio = "https://x.com";
        var rotas = new[]
        {
            SoftcomRotas.FormasPagamento, SoftcomRotas.Clientes, SoftcomRotas.Produtos, SoftcomRotas.Funcionarios, SoftcomRotas.Empresa,
            SoftcomRotas.CaixaAbrir(dominio), SoftcomRotas.CaixaFechar(dominio), SoftcomRotas.Vendas(dominio), SoftcomRotas.ClientesCriar(dominio),
        };

        Assert.All(rotas, r => Assert.Contains("softauth/api/v2/", r));
    }

    [Fact]
    public async Task CatalogoInteiroSincronizaContraUmServidorQueSoConheceSoftauth()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var visitadas = new List<string>();
        var service = CriarService(fixture, ServidorReal(visitadas));

        var resultado = await service.SincronizarTudoAsync();

        Assert.True(resultado.TudoComSucesso, $"Auth={resultado.AutenticacaoSucesso}; F={resultado.FormasPagamento?.Mensagem}; C={resultado.Clientes?.Mensagem}; P={resultado.Produtos?.Mensagem}; Fu={resultado.Funcionarios?.Mensagem}; E={resultado.Empresa?.Mensagem}");
        Assert.All(visitadas.Where(v => !v.Contains("authentication")), v => Assert.Contains("softauth/api/v2/", v));
    }

    [Fact]
    public async Task FormasDePagamentoEmbrulhadasNumArrayNaRaizSaoLidas()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, ServidorReal(new List<string>()));

        var resultado = await service.SincronizarFormasPagamentoAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        Assert.Equal(2, resultado.Quantidade);
        using var leitura = fixture.CriarContexto();
        var especie = leitura.FormasPagamento.Single(f => f.IdExterno == 5);
        Assert.Equal("ESPÉCIE", especie.Nome);
        Assert.Equal(1, especie.Ordem);   // veio como texto "1"
    }

    [Fact]
    public async Task ClienteComBloqueadoBooleanoEhLido()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, ServidorReal(new List<string>()));

        var resultado = await service.SincronizarClientesAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        Assert.False(leitura.Clientes.Single().Bloqueado);
    }

    [Fact]
    public async Task ClienteBloqueadoTrueBooleanoViraBloqueado()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var bloqueado = ClientesReal.Replace("\"bloqueado\":false", "\"bloqueado\":true");
        var service = CriarService(fixture, ServidorReal(new List<string>(), c => c.EndsWith("clientes/clientes") ? bloqueado : null));

        await service.SincronizarClientesAsync("t");

        using var leitura = fixture.CriarContexto();
        Assert.True(leitura.Clientes.Single().Bloqueado);
    }

    [Fact]
    public async Task ProdutoComNumerosEmTextoEhLido()
    {
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var service = CriarService(fixture, ServidorReal(new List<string>()));

        var resultado = await service.SincronizarProdutosAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var produto = leitura.Produtos.Single();
        Assert.Equal(15, produto.EstoqueAtual);          // "15.000"
        Assert.Equal(9.90m, produto.PrecoVenda);         // "9.9000"
        Assert.True(produto.Vender);                     // true (booleano de verdade)
    }

    [Fact]
    public async Task EmpresaComCamposNulosComoNaApiRealEhLida()
    {
        // empresa_nfce_numero_caixa (e outros) vêm null na API real; o DTO esperava inteiro.
        using var fixture = new SqliteInMemoryFixture();
        await SemearConfiguracaoAsync(fixture);
        var empresa = """
            {"current_page":1,"data":[{"empresa_id":1,"empresa_nome":"Loja","empresa_fantasia":"Loja Teste","empresa_razao_social":"Loja Teste Ltda","empresa_cnpj":"11222333000181",
            "empresa_email":"a@b.com","empresa_inscricao_estadual":"123456789","empresa_inscricao_municipal":"123456","empresa_mensagem_pedido":null,"empresa_troca_prazo":null,
            "empresa_troca_mensagem":null,"empresa_mfe_chave_validador":null,"empresa_cep":"80000000","empresa_endereco":"Rua Teste","empresa_numero":"100","empresa_complemento":null,
            "empresa_bairro":"Centro","empresa_cidade":"Curitiba","empresa_c_cidade":"4106902","empresa_uf":"PR","empresa_c_uf":"41","empresa_pais":"BRASIL","empresa_c_pais":"1058",
            "empresa_csc_token":"x","empresa_csc_id":"000001","empresa_fone_ddd":"","empresa_fone":"","empresa_certificado":null,"empresa_certificado_senha":null,"empresa_certificado_validade":null,
            "empresa_logomarca":"x","empresa_logomarca_extensao":"png","empresa_regime_tributario":"1","empresa_nfce_valor_minimo":null,"empresa_modulo_fiscal":false,"empresa_mei":false,
            "empresa_sat":false,"taxa_servico":"0.00","versao_memoria_restaurante":"1","empresa_nfce_serie":0,"empresa_nfce_numero_caixa":null,"empresa_nfce_ambiente":2,
            "empresa_nfce_modelo":65,"empresa_nfce_proximo_numero":1}],"next_page_url":null,"total":1,"date_sync":1789924589}
            """;
        var service = CriarService(fixture, ServidorReal(new List<string>(), c => c.EndsWith("empresa/empresas/1") ? empresa : null));

        var resultado = await service.SincronizarEmpresaAsync("t");

        Assert.True(resultado.Sucesso, resultado.Mensagem);
        using var leitura = fixture.CriarContexto();
        var salva = leitura.Empresas.Single();
        Assert.Equal(1, salva.IdExterno);
        Assert.Equal(0, salva.NfceNumeroCaixa);   // null da API -> 0 local
        Assert.Equal(65, salva.NfceModelo);
    }
}
