# Comandos do console

Todos os comandos precisam começar com `/`. Os nomes não diferenciam maiúsculas de minúsculas.

Use `/help` dentro do jogo para mostrar a lista resumida.

## Geral

| Comando | Efeito |
|---|---|
| `/help` | Mostra os comandos disponíveis, um por linha. |

## Movimento e visualização debug

| Comando | Efeito |
|---|---|
| `/debugfly` | Alterna o voo debug. No modo ativo, use W/A/S/D para voar rapidamente e atravessar blocos. |
| `/debugfly on` | Ativa o voo debug. |
| `/debugfly off` | Retorna ao movimento e colisão normais. |
| `/tissuevisual on` | Mostra a camada decorativa completa da `TissueNetwork`. |
| `/tissuevisual off` | Oculta a camada decorativa da `TissueNetwork`. |
| `/tissuefield on` | Mostra somente as células reais do `TissueField` em tiles sólidos. |
| `/tissuefield off` | Oculta o debug do `TissueField`. |

## Ajuste do pulso de Tissue

Os ajustes são aplicados em runtime e não são salvos. Ao iniciar outra sessão, os valores retornam aos defaults. Valores fora da faixa são limitados automaticamente. Ponto e vírgula são aceitos como separador decimal.

| Comando | Default | Faixa | Efeito |
|---|---:|---:|---|
| `/tissuepulse` | — | — | Mostra todos os valores atuais. |
| `/tissuepulse status` | — | — | Mesmo comportamento de `/tissuepulse`. |
| `/tissuepulse speed <valor>` | `320` | `40–1200` | Velocidade da frente em pixels por segundo. |
| `/tissuepulse trail <valor>` | `100` | `8–600` | Comprimento da cauda em pixels. |
| `/tissuepulse fade <valor>` | `1.4` | `0.1–5` | Potência da perda de brilho pela distância percorrida. |
| `/tissuepulse memory <valor>` | `2.8` | `0–15` | Duração, em segundos, do fade da memória laranja. |
| `/tissuepulse curve <valor>` | `1.35` | `0.1–5` | Curva usada no fade da memória. |
| `/tissuepulse intensity <valor>` | `0.82` | `0–1.5` | Intensidade do rastro laranja. |
| `/tissuepulse node <valor>` | `3.2` | `0–15` | Duração, em segundos, do afterglow dos nós. |
| `/tissuepulse reset` | — | — | Restaura todos os defaults. |
| `/tissuepulse help` | — | — | Mostra a sintaxe resumida. |

Aliases de `/tissuepulse node`: `nodeafterglow` e `nodelifetime`.

Exemplo de configuração:

```text
/tissuepulse speed 280
/tissuepulse trail 130
/tissuepulse memory 3.5
/tissuepulse intensity 0.7
```

## Adicionar itens ao inventário

| Comando | Efeito |
|---|---|
| `/get <item> [quantidade]` | Adiciona de `1` a `9999` unidades diretamente ao inventário e, se necessário, à hotbar. A quantidade default é `1`. |
| `/get list` | Lista todos os IDs disponíveis, IDs numéricos e limites de stack. |

O ID textual é o nome do `ItemId` em minúsculas. Espaços, hífens e `_` são ignorados. O ID numérico persistente também é aceito.

Exemplos:

```text
/get ironpickaxe 6
/get iron-pickaxe 6
/get "Iron Pickaxe" 6
/get 2 6
/get dirtblock 999
```

IDs atuais:

| ID textual | ID numérico | Item | Stack máximo |
|---|---:|---|---:|
| `ironpickaxe` | `2` | Iron Pickaxe | `1` |
| `dirtblock` | `3` | Dirt Block | `999` |
| `stoneblock` | `4` | Stone Block | `999` |
| `sandblock` | `5` | Sand Block | `999` |
| `rawwood` | `6` | Raw Wood | `999` |
| `workbench` | `7` | Workbench | `99` |
| `woodpickaxe` | `8` | Wood Pickaxe | `1` |
| `stonepickaxe` | `9` | Stone Pickaxe | `1` |
| `wooddoor` | `10` | Wood Door | `99` |

## Spawn de itens

| Comando | Efeito |
|---|---|
| `/spawn pickaxe` | Solta uma picareta de madeira próxima ao jogador. |
| `/spawn picareta` | Alias de `/spawn pickaxe`. |
| `/spawn wood pickaxe` | Solta uma picareta de madeira. |
| `/spawn stone pickaxe` | Solta uma picareta de pedra. |
| `/spawn iron pickaxe` | Solta uma picareta de ferro. |

## Simulação do mundo

| Comando | Efeito |
|---|---|
| `/tick` | Mostra a velocidade e o estado atual dos world ticks. |
| `/tick status` | Mesmo comportamento de `/tick`. |
| `/tick speed <valor>` | Define a escala de tempo entre `0.1` e `16` e retoma os ticks. |
| `/tick pause` | Pausa os world ticks. |
| `/tick resume` | Retoma os world ticks na velocidade atual. |
| `/tick reset` | Restaura velocidade `1x` e retoma os ticks. |
| `/tick step [ciclos]` | Executa manualmente de `1` a `600` ciclos; o default é `1`. |
| `/grass grow [amostras]` | Força de `1` a `10000` amostras de crescimento; o default é `256`. |
| `/debug ticks` | Mostra contadores fast/medium/slow, amostras, grama, chunks e velocidade. |

## Mundo e persistência

| Comando | Efeito |
|---|---|
| `/world save` | Salva imediatamente o mundo e reinicia o temporizador de autosave. |

O histórico dos comandos digitados é armazenado no save do mundo. Isso preserva a navegação com as setas, mas não torna permanentes configurações temporárias como `/tissuepulse`.

## Atalhos do console

| Atalho | Efeito |
|---|---|
| `Ctrl+A` | Seleciona toda a entrada. |
| `Ctrl+C` | Copia a seleção. |
| `Ctrl+X` | Recorta a seleção. |
| `Ctrl+V` | Cola texto sanitizado. |
| `Shift+←/→` | Expande ou reduz a seleção. |
| `↑/↓` | Navega pelo histórico persistido. |

