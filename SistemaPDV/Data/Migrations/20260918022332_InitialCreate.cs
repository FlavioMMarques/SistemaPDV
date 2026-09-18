using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SistemaPDV.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Clientes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    RazaoSocial = table.Column<string>(type: "TEXT", nullable: true),
                    Pessoa = table.Column<string>(type: "TEXT", nullable: false),
                    CpfCnpj = table.Column<string>(type: "TEXT", maxLength: 14, nullable: true),
                    Rg = table.Column<string>(type: "TEXT", nullable: true),
                    InscricaoEstadual = table.Column<string>(type: "TEXT", nullable: true),
                    InscricaoMunicipal = table.Column<string>(type: "TEXT", nullable: true),
                    ContribuinteIcms = table.Column<string>(type: "TEXT", nullable: true),
                    IndicadorFinalidade = table.Column<int>(type: "INTEGER", nullable: false),
                    Bloqueado = table.Column<bool>(type: "INTEGER", nullable: false),
                    Observacao = table.Column<string>(type: "TEXT", nullable: true),
                    DataNascimento = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    DataFundacao = table.Column<DateOnly>(type: "TEXT", nullable: true),
                    ContatoNome = table.Column<string>(type: "TEXT", nullable: true),
                    ContatoDdd = table.Column<string>(type: "TEXT", nullable: true),
                    ContatoTelefone = table.Column<string>(type: "TEXT", nullable: true),
                    ContatoEmail = table.Column<string>(type: "TEXT", nullable: true),
                    Cep = table.Column<string>(type: "TEXT", nullable: true),
                    Endereco = table.Column<string>(type: "TEXT", nullable: true),
                    Numero = table.Column<string>(type: "TEXT", nullable: true),
                    Complemento = table.Column<string>(type: "TEXT", nullable: true),
                    Bairro = table.Column<string>(type: "TEXT", nullable: true),
                    PontoReferencia = table.Column<string>(type: "TEXT", nullable: true),
                    Cidade = table.Column<string>(type: "TEXT", nullable: true),
                    CidadeId = table.Column<string>(type: "TEXT", nullable: true),
                    Uf = table.Column<string>(type: "TEXT", nullable: true),
                    TipoClienteId = table.Column<string>(type: "TEXT", nullable: true),
                    TipoClienteNome = table.Column<string>(type: "TEXT", nullable: true),
                    FuncionarioId = table.Column<int>(type: "INTEGER", nullable: true),
                    FuncionarioNome = table.Column<string>(type: "TEXT", nullable: true),
                    TabelaPreco_IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    TabelaPreco_Descricao = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Clientes", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Empresas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    RazaoSocial = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    NomeFantasia = table.Column<string>(type: "TEXT", nullable: true),
                    Cnpj = table.Column<string>(type: "TEXT", maxLength: 14, nullable: false),
                    Email = table.Column<string>(type: "TEXT", nullable: true),
                    Cep = table.Column<string>(type: "TEXT", nullable: true),
                    Endereco = table.Column<string>(type: "TEXT", nullable: true),
                    Numero = table.Column<string>(type: "TEXT", nullable: true),
                    Complemento = table.Column<string>(type: "TEXT", nullable: true),
                    Bairro = table.Column<string>(type: "TEXT", nullable: true),
                    Cidade = table.Column<string>(type: "TEXT", nullable: true),
                    Uf = table.Column<string>(type: "TEXT", nullable: true),
                    ModuloFiscal = table.Column<bool>(type: "INTEGER", nullable: false),
                    NfceSerie = table.Column<int>(type: "INTEGER", nullable: false),
                    NfceNumeroCaixa = table.Column<int>(type: "INTEGER", nullable: false),
                    NfceAmbiente = table.Column<int>(type: "INTEGER", nullable: false),
                    NfceModelo = table.Column<int>(type: "INTEGER", nullable: false),
                    NfceProximoNumero = table.Column<int>(type: "INTEGER", nullable: false),
                    CertificadoProtegido = table.Column<string>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Empresas", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "FormasPagamento",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Tipo = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    Padrao = table.Column<bool>(type: "INTEGER", nullable: false),
                    CodigoNfce = table.Column<string>(type: "TEXT", maxLength: 10, nullable: true),
                    CodigoTransacaoSitef = table.Column<string>(type: "TEXT", maxLength: 20, nullable: true),
                    CarteiraDigital = table.Column<bool>(type: "INTEGER", nullable: false),
                    Ordem = table.Column<int>(type: "INTEGER", nullable: false),
                    PdvPos = table.Column<bool>(type: "INTEGER", nullable: false),
                    PreVenda = table.Column<bool>(type: "INTEGER", nullable: false),
                    AtalhoNumero = table.Column<string>(type: "TEXT", maxLength: 5, nullable: true),
                    PermissaoSupervisor = table.Column<bool>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormasPagamento", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Funcionarios",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    Cpf = table.Column<string>(type: "TEXT", maxLength: 11, nullable: true),
                    Supervisor = table.Column<bool>(type: "INTEGER", nullable: false),
                    Desativado = table.Column<bool>(type: "INTEGER", nullable: false),
                    PdvKeyHash = table.Column<string>(type: "TEXT", maxLength: 64, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Funcionarios", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Produtos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    Sku = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    CodigoBarras = table.Column<string>(type: "TEXT", maxLength: 50, nullable: true),
                    Nome = table.Column<string>(type: "TEXT", maxLength: 150, nullable: false),
                    NomeOriginal = table.Column<string>(type: "TEXT", nullable: true),
                    Fabricante = table.Column<string>(type: "TEXT", nullable: true),
                    Referencia = table.Column<string>(type: "TEXT", nullable: true),
                    GrupoId = table.Column<int>(type: "INTEGER", nullable: true),
                    EstoqueAtual = table.Column<int>(type: "INTEGER", nullable: false),
                    UnidadeMedida = table.Column<string>(type: "TEXT", nullable: true),
                    Peso = table.Column<decimal>(type: "TEXT", nullable: true),
                    PrecoVenda = table.Column<decimal>(type: "TEXT", nullable: false),
                    PrecoCompra = table.Column<decimal>(type: "TEXT", nullable: true),
                    MargemLucro = table.Column<decimal>(type: "TEXT", nullable: true),
                    Ncm = table.Column<string>(type: "TEXT", nullable: true),
                    Cest = table.Column<string>(type: "TEXT", nullable: true),
                    CodigoBeneficioFiscal = table.Column<string>(type: "TEXT", nullable: true),
                    StatusFiscal = table.Column<int>(type: "INTEGER", nullable: false),
                    CodigoNfe = table.Column<string>(type: "TEXT", nullable: true),
                    Vender = table.Column<bool>(type: "INTEGER", nullable: false),
                    RestricaoIdade = table.Column<bool>(type: "INTEGER", nullable: false),
                    Hortifruit = table.Column<bool>(type: "INTEGER", nullable: false),
                    Observacao = table.Column<string>(type: "TEXT", nullable: true),
                    PromocaoPreco = table.Column<decimal>(type: "TEXT", nullable: true),
                    PromocaoValidade = table.Column<string>(type: "TEXT", nullable: true),
                    PromocaoQuantidade = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Produtos", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "Caixas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    FuncionarioId = table.Column<int>(type: "INTEGER", nullable: false),
                    DataCaixa = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    Turno = table.Column<int>(type: "INTEGER", nullable: false),
                    DataAbertura = table.Column<DateTime>(type: "TEXT", nullable: false),
                    DataFechamento = table.Column<DateTime>(type: "TEXT", nullable: true),
                    TrocoInicial = table.Column<decimal>(type: "TEXT", nullable: false),
                    TrocoFinal = table.Column<decimal>(type: "TEXT", nullable: true),
                    Status = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Caixas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Caixas_Funcionarios_FuncionarioId",
                        column: x => x.FuncionarioId,
                        principalTable: "Funcionarios",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ImagemProduto",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Descricao = table.Column<string>(type: "TEXT", nullable: true),
                    ArquivoOriginal = table.Column<string>(type: "TEXT", nullable: false),
                    ArquivoThumbnail = table.Column<string>(type: "TEXT", nullable: true),
                    Tipo = table.Column<string>(type: "TEXT", nullable: true),
                    ProdutoId = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ImagemProduto", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ImagemProduto_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Vendas",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    IdExterno = table.Column<int>(type: "INTEGER", nullable: true),
                    SyncStatus = table.Column<string>(type: "TEXT", nullable: false),
                    DataHora = table.Column<DateTime>(type: "TEXT", nullable: false),
                    CaixaId = table.Column<int>(type: "INTEGER", nullable: false),
                    ClienteId = table.Column<int>(type: "INTEGER", nullable: true),
                    Desconto = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    UltimoErroSync = table.Column<string>(type: "TEXT", nullable: true),
                    TentativasEnvio = table.Column<int>(type: "INTEGER", nullable: false),
                    VendaIdExterno = table.Column<int>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Vendas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Vendas_Caixas_CaixaId",
                        column: x => x.CaixaId,
                        principalTable: "Caixas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Vendas_Clientes_ClienteId",
                        column: x => x.ClienteId,
                        principalTable: "Clientes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ItensVenda",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    ProdutoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Quantidade = table.Column<decimal>(type: "TEXT", precision: 18, scale: 3, nullable: false),
                    PrecoUnitario = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    DescontoItem = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false),
                    AcrescimoItem = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItensVenda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ItensVenda_Produtos_ProdutoId",
                        column: x => x.ProdutoId,
                        principalTable: "Produtos",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ItensVenda_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "PagamentosVenda",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    VendaId = table.Column<Guid>(type: "TEXT", nullable: false),
                    FormaPagamentoId = table.Column<int>(type: "INTEGER", nullable: false),
                    Valor = table.Column<decimal>(type: "TEXT", precision: 18, scale: 2, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PagamentosVenda", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PagamentosVenda_FormasPagamento_FormaPagamentoId",
                        column: x => x.FormaPagamentoId,
                        principalTable: "FormasPagamento",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_PagamentosVenda_Vendas_VendaId",
                        column: x => x.VendaId,
                        principalTable: "Vendas",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Caixas_DataCaixa_Turno_FuncionarioId",
                table: "Caixas",
                columns: new[] { "DataCaixa", "Turno", "FuncionarioId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Caixas_FuncionarioId",
                table: "Caixas",
                column: "FuncionarioId");

            migrationBuilder.CreateIndex(
                name: "IX_Caixas_IdExterno",
                table: "Caixas",
                column: "IdExterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Clientes_IdExterno",
                table: "Clientes",
                column: "IdExterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Empresas_IdExterno",
                table: "Empresas",
                column: "IdExterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormasPagamento_IdExterno",
                table: "FormasPagamento",
                column: "IdExterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Funcionarios_IdExterno",
                table: "Funcionarios",
                column: "IdExterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ImagemProduto_ProdutoId",
                table: "ImagemProduto",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVenda_ProdutoId",
                table: "ItensVenda",
                column: "ProdutoId");

            migrationBuilder.CreateIndex(
                name: "IX_ItensVenda_VendaId",
                table: "ItensVenda",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosVenda_FormaPagamentoId",
                table: "PagamentosVenda",
                column: "FormaPagamentoId");

            migrationBuilder.CreateIndex(
                name: "IX_PagamentosVenda_VendaId",
                table: "PagamentosVenda",
                column: "VendaId");

            migrationBuilder.CreateIndex(
                name: "IX_Produtos_IdExterno",
                table: "Produtos",
                column: "IdExterno",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_CaixaId",
                table: "Vendas",
                column: "CaixaId");

            migrationBuilder.CreateIndex(
                name: "IX_Vendas_ClienteId",
                table: "Vendas",
                column: "ClienteId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Empresas");

            migrationBuilder.DropTable(
                name: "ImagemProduto");

            migrationBuilder.DropTable(
                name: "ItensVenda");

            migrationBuilder.DropTable(
                name: "PagamentosVenda");

            migrationBuilder.DropTable(
                name: "Produtos");

            migrationBuilder.DropTable(
                name: "FormasPagamento");

            migrationBuilder.DropTable(
                name: "Vendas");

            migrationBuilder.DropTable(
                name: "Caixas");

            migrationBuilder.DropTable(
                name: "Clientes");

            migrationBuilder.DropTable(
                name: "Funcionarios");
        }
    }
}
