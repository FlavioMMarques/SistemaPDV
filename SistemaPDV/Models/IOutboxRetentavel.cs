using System;

namespace SistemaPDV.Models;

// O que uma entidade de outbox (Caixa, Venda, Cliente criado localmente) guarda pra
// política de retentativa (ver PoliticaRetentativa): quantas vezes já tentou e quando
// pode tentar de novo. Persistido no banco de propósito — a espera e o teto precisam
// sobreviver a fechar e reabrir o app, senão reabrir "perdoaria" um erro permanente.
public interface IOutboxRetentavel
{
    int TentativasEnvio { get; set; }

    // UTC. null = pode tentar já (nunca falhou, ou já esgotou — quem diferencia é TentativasEnvio).
    DateTime? ProximaTentativaEm { get; set; }
}
