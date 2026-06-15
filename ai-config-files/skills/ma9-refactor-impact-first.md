---
name: ma9-refactor-impact-first
description: 'Guia propostas de refatoracao com base no codigo existente. Use quando pedir refatoracao, melhoria estrutural, simplificacao ou limpeza tecnica. Executa fluxo: buscar implementacoes similares, interfaces existentes, padroes do projeto e regras de negocio afetadas; evitar duplicacao; propor mudancas pequenas com impacto, arquivos envolvidos e riscos.'
argument-hint: 'Area/codigo que precisa de proposta de refatoracao'
user-invocable: true
---

# Refactor Impact First

## Objetivo
Produzir propostas de refatoracao aderentes ao projeto, com baixo risco de regressao e sem duplicacao desnecessaria.

## Quando usar
- Pedido de refatoracao em codigo existente.
- Pedido de melhoria de design sem mudanca de regra de negocio.
- Pedido de consolidacao de implementacoes repetidas.

## Nao usar
- Criacao de feature nova sem relacao com codigo existente.
- Mudancas exploratorias sem base em evidencias do repositorio.

## Fluxo obrigatorio antes de propor refatoracao
1. Procurar implementacoes similares no projeto.
2. Procurar interfaces ja existentes reutilizaveis.
3. Procurar padroes adotados no mesmo projeto (naming, camadas, estrutura, contratos).
4. Procurar regras de negocio afetadas direta e indiretamente.
5. Eliminar duplicacao na proposta (reuso > copia).

## Decisoes e ramificacoes
- Se existir implementacao similar confiavel: propor convergencia para o padrao existente.
- Se existir interface compativel: priorizar extensao/adaptacao em vez de criar nova interface.
- Se nao houver padrao claro: nao bloquear a proposta; declarar incerteza e sugerir menor mudanca reversivel com validacao rapida.
- Se regra de negocio puder mudar comportamento: separar refatoracao estrutural de alteracao funcional.
- Se reduzir duplicacao exigir grande impacto: propor plano em etapas pequenas com checkpoints.

## Criterios de qualidade
- A proposta explicita impacto tecnico e impacto funcional esperado.
- Os arquivos envolvidos sao listados com o proposito de cada alteracao.
- Riscos e possiveis regressos sao mapeados.
- O plano vem em passos pequenos e verificaveis.
- Nao recomenda padrao conflitante com o que ja existe no projeto sem justificativa.

## Formato de resposta
- Escopo da refatoracao: objetivo e limites.
- Impacto tecnico: estrutura, acoplamento, reuso, testabilidade e manutencao.
- Impacto funcional: comportamento que deve permanecer igual e pontos sensiveis de regressao.
- Arquivos envolvidos: lista de arquivos e proposito de cada ponto de leitura/alteracao.
- Riscos: regressao, acoplamento, compatibilidade e cobertura de testes.
- Plano incremental: passos pequenos, com validacao a cada passo.

## Checklist rapido
- implementacoes similares mapeadas
- interfaces existentes mapeadas
- padroes do projeto mapeados
- regras de negocio impactadas mapeadas
- duplicacao evitada
- impacto tecnico indicado
- impacto funcional indicado
- arquivos envolvidos com proposito indicados
- riscos indicados
- passos pequenos propostos
