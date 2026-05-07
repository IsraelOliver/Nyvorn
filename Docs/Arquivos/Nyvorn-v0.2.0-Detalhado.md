# Nyvorn v0.2.0 - Consolidacao da Base

## Objetivo da Versao

A `v0.2.0` existe para consolidar a base tecnica do projeto antes de abrir o mundo sandbox real.

Ela nao e a fase de narrativa profunda, nem de biomas completos, nem de mundo procedural final.
Ela e a fase de estabilizacao da fundacao.

O foco aqui e garantir que os sistemas centrais do jogo estejam organizados, previsiveis e prontos para crescer.

## Resultado Esperado ao Final da v0.2.0

Ao final desta versao, o projeto deve estar assim:

- Base jogavel estavel
- Menos codigo hardcoded espalhado
- Menos restos de prototipo visual e estrutural
- Sistemas centrais mais configuraveis
- Sessao de jogo mais limpa
- Mundo de teste ainda existente, mas claramente separado do mundo real futuro

Em resumo:

- a `v0.1.0` prova que o jogo roda
- a `v0.2.0` prova que a base aguenta crescer

## Escopo da v0.2.0

### 1. Consolidar configuracoes iniciais

Objetivo:

- Tirar numeros importantes de dentro de varios arquivos e centralizar valores-base

Inclui:

- configuracao do player
- configuracao do inimigo
- configuracoes de combate que ainda estiverem espalhadas
- preparacao para configs futuras de itens e mundo

Resultado esperado:

- velocidade, vida, hurtbox, cooldowns e outros valores-base deixam de ficar dispersos

### 2. Limpar restos de prototipo

Objetivo:

- Reduzir ruído tecnico antes de expandir o projeto

Inclui:

- remover debug visual que nao faz parte do jogo
- revisar spawns e valores de teste excessivamente fixos
- revisar dependencias provisórias
- manter apenas o que ainda ajuda no desenvolvimento

Resultado esperado:

- menos comportamento de teste embutido no loop principal

### 3. Organizar melhor a sessao jogavel

Objetivo:

- Deixar claro o que pertence ao jogo e o que pertence ao bootstrap da cena

Inclui:

- revisar `PlayingSession`
- revisar `PlayingSessionFactory`
- separar melhor:
  - criacao da sessao
  - dados de teste
  - inicializacao de entidades
  - dependencias de combate e UI

Resultado esperado:

- a sessao fica mais pronta para receber mundo real depois

### 4. Consolidar o loop basico de combate, morte e respawn

Objetivo:

- Garantir que o jogo base continue confiavel antes da expansao sandbox

Inclui:

- player leva dano
- inimigo leva dano
- knockback
- morte do player
- tela de morte
- respawn ou reinicio
- respawn do inimigo de teste

Resultado esperado:

- loop basico funcional e sem comportamento quebrado

### 5. Revisar inventario e hotbar como base, nao como feature final

Objetivo:

- Manter inventario usavel, mas entender que ele ainda e uma fundacao

Inclui:

- hotbar funcional
- inventario basico funcional
- coleta de item no mundo
- drop funcional
- interacao minimamente confiavel

Resultado esperado:

- sistema bom o suficiente para sustentar exploracao futura

### 6. Preservar o mundo de teste, mas marcar a transicao para o mundo real

Objetivo:

- Nao confundir mapa de teste com estrutura definitiva do jogo

Inclui:

- manter o `GenerateTest()` apenas como apoio temporario
- evitar expandir gameplay grande em cima do mapa de teste
- preparar a proxima fase para sair do layout artificial atual

Resultado esperado:

- a equipe sabe que o mapa atual e provisório

## O Que Entra na v0.2.0

- consolidacao tecnica
- configuracoes base
- limpeza de prototipo
- estabilidade do loop principal
- organizacao melhor da sessao jogavel
- inventario/hotbar estaveis o bastante para continuar

## O Que Nao Entra na v0.2.0

Para nao misturar fases, estas coisas nao devem ser objetivo principal agora:

- mundo sandbox completo
- biomas reais
- corrupcao sistemica completa
- memorias e ecos narrativos
- culto e faccoes
- bosses relevantes para a lore
- progressao longa do mundo
- implementacao profunda da narrativa de Ekko

Esses pontos pertencem as fases seguintes.

## Checklist Tecnico da v0.2.0

### Player

- centralizar config do player
- revisar valores de movimento
- revisar valores de dodge
- revisar hurtbox
- remover debug restante

### Enemy

- centralizar config do inimigo
- revisar vida e hurtbox
- revisar gravidade e knockback
- revisar respawn controller

### Combat

- manter `CombatSystem` modular
- evitar retorno de acoplamento com classes concretas
- manter danos e knockbacks centralizados

### Session

- revisar `PlayingSessionFactory`
- reduzir inicializacao hardcoded demais
- manter bootstrap claro

### Inventory

- manter hotbar funcionando
- manter inventario funcionando
- manter drop e coleta confiaveis

### Mapa

- manter mapa de teste apenas como suporte
- evitar tratar o layout atual como base final do mundo

## Entregas Minimas da v0.2.0

Para considerar a versao concluida, o projeto deve ter:

1. Configs centrais iniciais para player e inimigo
2. Sessao de jogo organizada o bastante para crescer
3. Loop basico de combate e morte confiavel
4. Inventario e hotbar sem bugs centrais conhecidos
5. Menos resquicios de prototipo visual e estrutural
6. Base limpa para iniciar a construcao do mundo sandbox real na proxima fase

## Relacao com a Proxima Versao

A `v0.2.0` prepara a `v0.3.0`.

A proxima versao deve focar em:

- estrutura real do mundo sandbox
- sair do mapa de teste
- estabelecer o primeiro recorte exploravel do planeta

Ou seja:

- `v0.2.0` arruma a fundacao
- `v0.3.0` comeca a construir o mundo

## Resumo Final

A `v0.2.0` nao precisa impressionar pelo conteudo.
Ela precisa gerar confianca tecnica.

Se ela for bem feita, o projeto para de ser apenas um prototipo funcional e passa a ser uma base real de desenvolvimento.
