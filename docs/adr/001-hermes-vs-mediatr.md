# ADR 001 — Adoção do Proxos como substituto do MediatR

**Data:** 2026-03-17
**Status:** Proposta

---

## Contexto

O MediatR é amplamente usado como implementação do padrão Mediator/CQRS em projetos .NET da equipe.
A partir da **versão 13**, o MediatR adotou licença comercial paga para uso empresarial.
Manter o MediatR 12.x congela o projeto em uma versão que não receberá mais atualizações de segurança ou compatibilidade com futuras versões do .NET.

## Decisão

Substituir o MediatR pelo **Proxos** — biblioteca MIT criada internamente com API compatível.

## Alternativas consideradas

| Opção | Prós | Contras |
|---|---|---|
| Manter MediatR 12.x | Zero migração | Sem suporte; incompatível com .NET 11+ futuramente |
| Comprar licença MediatR 13+ | Suporte oficial | Custo por desenvolvedor; lock-in comercial |
| **Proxos (escolhido)** | MIT; performance superior; API quase idêntica | Biblioteca nova — menor maturidade |
| Implementar mediator próprio | Controle total | Alto custo de desenvolvimento e manutenção |

## Riscos e mitigações

| Risco | Probabilidade | Impacto | Mitigação |
|---|---|---|---|
| Bug não coberto por testes | Média | Alto | 54+ testes unitários; iniciar em serviços não críticos |
| Analyzer PRX001 falso positivo em projetos com camadas | Baixa (fixado na v1.0.2) | Baixo | Suprimir com `#pragma warning disable PRX001` se necessário |
| Incompatibilidade de API | Baixa | Médio | API é 99% compatível; principais diferenças documentadas abaixo |

## Diferenças de API em relação ao MediatR

### Registro (mudança obrigatória)

```csharp
// Antes (MediatR)
services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(...));

// Depois (Proxos)
services.AddProxos(cfg => cfg.RegisterServicesFromAssembly(...));
```

### Namespace (mudança obrigatória)

```csharp
// Antes
using MediatR;

// Depois
using Proxos;
```

### Interfaces idênticas — sem mudanças no código de negócio

```csharp
// Funciona igual nos dois
public record GetPedidoQuery(int Id) : IRequest<Pedido>;

public class GetPedidoHandler : IRequestHandler<GetPedidoQuery, Pedido>
{
    public Task<Pedido> Handle(GetPedidoQuery q, CancellationToken ct) => ...;
}

await mediator.Send(new GetPedidoQuery(id));
await mediator.Publish(new PedidoCriado(id));
```

### Funcionalidades exclusivas do Proxos (bônus)

```csharp
// Timeout declarativo — não existe no MediatR
[RequestTimeout(5000)]
public record RelatorioQuery : IRequest<Relatorio>;

// Ignorar behavior por request — não existe no MediatR
[IgnoreBehavior(typeof(AuditBehavior<,>))]
public record HealthCheckQuery : IRequest<bool>;

// Behavior condicional — mais explícito que generic constraint
public class CacheBehavior<TReq, TRes> : IConditionalBehavior<TReq, TRes>
    where TReq : ICacheableRequest
{
    public bool ShouldHandle(TReq request) => !request.BypassCache;
    // ...
}
```

## Plano de migração

### Fase 1 — Serviços não críticos (meses 1-3)
- APIs internas de relatório
- Background workers de processamento de dados
- Ferramentas administrativas internas

### Fase 2 — Avaliação (mês 4)
- Revisar incidentes e comportamentos inesperados da Fase 1
- Atualizar benchmarks com métricas reais de produção
- Decidir se avança para Fase 3

### Fase 3 — Expansão (meses 5+)
- APIs de domínio principal
- Serviços com SLA definido

### Fora do escopo desta ADR
- ❌ Sistemas financeiros / pagamento — avaliar separadamente com requisito explícito
- ❌ Serviços com SLA crítico (< 500ms P99) — até ter benchmarks de produção validados

## Plano de rollback

Se surgir problema crítico em produção:

1. Reverter `AddProxos` → `AddMediatR` (1 linha por projeto)
2. Reverter `using Proxos` → `using MediatR` (global find/replace)
3. Remover referência ao pacote `Proxos`, restaurar `MediatR 12.4.x`

Tempo estimado de rollback: 30 minutos por serviço afetado.

## Consequências

- **Positivo:** Custo zero de licença; acesso ao código-fonte; possibilidade de contribuir ou corrigir bugs diretamente
- **Negativo:** Maturidade menor que o MediatR (criado em 2026); suporte depende da manutenção interna
- **Neutro:** API quase idêntica — curva de aprendizado mínima para a equipe
