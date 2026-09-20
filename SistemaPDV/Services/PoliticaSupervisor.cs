namespace SistemaPDV.Services;

// Interruptor ÚNICO da exigência de chave de supervisor (descartar venda em falha e abrir as Configurações).
//
// DESLIGADO por decisão do usuário (2026-09-20): ainda não se sabe como obter a chave de supervisor no SoftcomShop,
// então esses pontos ficam abertos por enquanto. A lógica de chave continua toda implementada e testada (os serviços
// recebem o valor por construtor e a suíte cobre os dois modos) — para religar é só trocar para `true` aqui.
//
// Enquanto estiver desligado: o descarte grava quem PEDIU (o operador logado) mas não há supervisor autorizando
// (DescartadaPorId fica vazio), e as Configurações abrem direto pelo botão da barra.
public static class PoliticaSupervisor
{
    public const bool ExigirChave = false;
}
