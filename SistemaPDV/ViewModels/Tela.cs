namespace SistemaPDV.ViewModels;

// AbrirCaixa e Dashboard ainda não têm ViewModel próprio (chegam nas Tasks 43 e 46)
// — até lá, ShellViewModel calcula corretamente qual dessas telas é a certa (testável
// via TelaAtual), mas CurrentViewModel fica nulo pra esses dois casos.
public enum Tela
{
    Configuracoes,
    Login,
    AbrirCaixa,
    Dashboard,
}
