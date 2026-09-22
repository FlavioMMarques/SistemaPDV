using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using SistemaPDV.Data;
using SistemaPDV.Models;
using SistemaPDV.Services.Sync.Dtos;

namespace SistemaPDV.Services.Sync;

// Outbox de produto criado no PDV (modal "Cadastrar Produto"): mesmo desenho do cliente novo. Em arquivo próprio (partial)
// porque CatalogSyncService já é o maior arquivo do projeto.
//
// Produto criado aqui nasce PendenteSync e sem IdExterno. Enviado com sucesso, a API devolve DOIS ids que a venda precisa:
// o do produto-base (`id` → Produto.ProdutoIdApi, o "produto_id" da venda) e o da grade da empresa
// (`produto_empresas[].produto_empresa_grade.id` → Produto.IdExterno, o "produto_empresa_grade_id" da venda). Enquanto o
// produto não sincroniza, a venda que o contém espera (MontadorDeRequisicaoDeVenda: "produto ainda não sincronizado").
public partial class CatalogSyncService
{
    public async Task<ResultadoSincronizacaoRecurso> SincronizarProdutoNovoAsync(int produtoId, string accessToken, CancellationToken ct = default)
    {
        await using var context = contextFactory();

        var produto = await context.Produtos.FirstOrDefaultAsync(p => p.Id == produtoId, ct);
        if (produto is null)
            return ResultadoSincronizacaoRecurso.ComFalha("Produto não encontrado.");

        // Já tem par na API (veio de lá, ou já foi enviado) — nada a empurrar.
        if (produto.IdExterno is not null)
            return ResultadoSincronizacaoRecurso.ComSucesso(0);

        var configuracao = await ObterConfiguracaoAsync(context, ct);
        var dominio = SoftcomAuthService.ExtrairDominio(configuracao.UrlApi);

        // Problema de configuração, não do produto: falha SEM marcar o produto (segue PendenteSync).
        if (!ConexaoSegura.Permitida(dominio))
            return ResultadoSincronizacaoRecurso.ComFalha(ConexaoSegura.MensagemRecusa);

        // A API exige um grupo ativo que já exista lá; sem categoria o cadastro nem sai daqui.
        if (produto.GrupoId is not { } grupoId || grupoId <= 0)
            return await MarcarFalhaProdutoAsync(context, produto, "Escolha a categoria do produto para enviá-lo.", ct);

        var corpo = new ProdutoNovoLoteRequestDto
        {
            Produtos =
            {
                new ProdutoNovoRequestDto
                {
                    Nome = produto.Nome.Trim(),
                    GrupoId = grupoId,
                    PrecoVenda = produto.PrecoVenda,
                    PrecoCompra = produto.PrecoCompra is > 0 ? produto.PrecoCompra : null,
                    CodigoBarras = string.IsNullOrWhiteSpace(produto.CodigoBarras) ? null : produto.CodigoBarras.Trim(),
                    Referencia = string.IsNullOrWhiteSpace(produto.Referencia) ? null : produto.Referencia.Trim(),
                },
            },
        };

        var resultado = await apiClient.EnviarAsync(
            HttpMethod.Post, SoftcomRotas.ProdutosCriar(dominio), corpo, accessToken, ct);

        if (resultado.Tipo == ResultadoEnvioTipo.ConexaoInsegura)
            return ResultadoSincronizacaoRecurso.ComFalha(resultado.Conteudo);

        if (resultado.Tipo != ResultadoEnvioTipo.Sucesso)
            return await MarcarFalhaProdutoAsync(context, produto, ErroApiExtractor.Extrair(resultado.Conteudo), ct);

        // Nunca confiar cegamente na resposta: sem os dois ids o produto não serve para vender.
        var resposta = SoftcomJson.TentarDesserializar<ProdutoNovoRespostaDto>(resultado.Conteudo);
        var criado = resposta?.Data?.Created.FirstOrDefault();
        var gradeId = criado is null ? 0 : EscolherGradeDaEmpresa(context, criado.ProdutoEmpresas);
        if (criado is not { Id: > 0 } || gradeId <= 0)
            return await MarcarFalhaProdutoAsync(context, produto,
                $"A resposta não trouxe os ids do produto: {ErroApiExtractor.Extrair(resultado.Conteudo)}", ct);

        produto.ProdutoIdApi = criado.Id;
        produto.IdExterno = gradeId;
        produto.SyncStatus = SyncStatus.Sincronizado;
        PoliticaRetentativa.Zerar(produto);
        produto.UltimoErroSync = null;
        await context.SaveChangesAsync(ct);
        return ResultadoSincronizacaoRecurso.ComSucesso(1);
    }

    // A resposta lista o produto por empresa; interessa a grade da empresa DESTE aparelho (a que a venda usa). Sem empresa
    // conhecida, ou sem casar, cai na primeira que tenha grade.
    private static int EscolherGradeDaEmpresa(AppDbContext context, List<ProdutoNovoEmpresaDto> empresas)
    {
        var empresaDoAparelho = context.Empresas.Select(e => e.IdExterno).FirstOrDefault();
        var escolhida = empresas.FirstOrDefault(e => empresaDoAparelho is { } id && e.EmpresaId == id && e.ProdutoEmpresaGrade is { Id: > 0 })
            ?? empresas.FirstOrDefault(e => e.ProdutoEmpresaGrade is { Id: > 0 });
        return escolhida?.ProdutoEmpresaGrade?.Id ?? 0;
    }

    public async Task<ResultadoSincronizacaoRecurso> SincronizarProdutosNovosPendentesAsync(string accessToken, CancellationToken ct = default)
    {
        List<int> produtoIds;
        await using (var context = contextFactory())
        {
            produtoIds = await context.Produtos
                .Where(p => p.IdExterno == null && p.SyncStatus != SyncStatus.Sincronizado)
                .Where(PoliticaRetentativa.Elegivel<Produto>(AgoraUtc))   // espera crescente/teto (PoliticaRetentativa)
                .Select(p => p.Id)
                .ToListAsync(ct);
        }

        var totalSincronizados = 0;
        foreach (var produtoId in produtoIds)
        {
            try
            {
                var resultado = await SincronizarProdutoNovoAsync(produtoId, accessToken, ct);
                if (resultado.Sucesso)
                    totalSincronizados += resultado.Quantidade;
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Um produto com falha inesperada não trava o lote: segue pendente e é tentado de novo no próximo ciclo.
                Registro.Erro("Envio", $"Exceção ao enviar o produto {produtoId}", ex);
            }
        }

        return ResultadoSincronizacaoRecurso.ComSucesso(totalSincronizados);
    }

    private Task<ResultadoSincronizacaoRecurso> MarcarFalhaProdutoAsync(
        AppDbContext context, Produto produto, string mensagem, CancellationToken ct)
    {
        var agora = AgoraUtc;
        return OutboxHelper.MarcarFalhaAsync(context, produto, mensagem, (p, m) =>
        {
            p.SyncStatus = SyncStatus.FalhaSync;
            p.UltimoErroSync = PoliticaRetentativa.RegistrarFalha(p, agora, m);
        }, ct);
    }
}
