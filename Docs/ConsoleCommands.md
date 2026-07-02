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

## Mutação debug do Tissue

Estes comandos alteram o `TissueField` persistente no tile sob o mouse. O raio é
opcional, usa uma área circular e aceita valores de `0` a `16` tiles. O raio
default é `0`, afetando somente o tile apontado.

| Comando | Efeito |
|---|---|
| `/tissuedamage <valor> [raio]` | Reduz `Vitality` pelo valor indicado. |
| `/tissueheal <valor> [raio]` | Aumenta `Vitality` sem recriar Tissue removido. |
| `/tissuecorrupt <valor> [raio]` | Aumenta `Corruption`. |
| `/tissuememory <valor> [raio]` | Aumenta `MemoryDensity`. |
| `/tissueflow <valor> [raio]` | Define `Flow` para o valor indicado. |
| `/tissueremove [raio]` | Remove o Tissue físico e cria o tombstone persistente. |
| `/tissuereset [raio]` | Remove overrides e restaura o estado original gerado. Exige tile sólido. |

Valores biológicos ficam entre `0` e `1`. Os comandos informam quantos tiles
foram realmente alterados. Alterações são salvas normalmente no mundo.

Exemplos:

```text
/tissuedamage 0.5
/tissuecorrupt 0.35 3
/tissueflow 0 2
/tissueremove
/tissuereset 3
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

## Spawn de entidades

| Comando | Efeito |
|---|---|
| `/spawn <entidade>` | Reservado exclusivamente para entidades. Ainda não existem entidades debug registradas para esse comando. |

Itens nunca usam `/spawn`. Para adicionar itens ao jogador, use `/get`.

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
| `/time` | Mostra horario, fase, ciclo, noite e estado ambiental. |
| `/time status` | Mesmo comportamento de `/time`. |
| `/time day` | Ajusta o horario para 06:00. |
| `/time night` | Ajusta o horario para 19:30. |
| `/time dawn` | Ajusta o horario para 04:00. |
| `/time sunrise` | Ajusta o horario para 05:00. |
| `/time noon` | Ajusta o horario para 12:00. |
| `/time sunset` | Ajusta o horario para 17:30. |
| `/time midnight` | Ajusta o horario para 00:00. |
| `/event status` | Mostra chuva, eclipse, cooldowns, clima e pulso corretivo do Tissue. |
| `/event rain start` | Forca o pressagio de chuva procedural. |
| `/event rain stop` | Dissipa a chuva ativa. |
| `/event eclipse start` | Forca a transicao de eclipse solar. |
| `/event eclipse stop` | Dissipa o eclipse ativo. |
| `/event clear` | Limpa eventos ambientais e cooldowns debug. |
| `/water status` | Mostra tiles com agua, tiles ativos e volume equivalente. |
| `/water place [raio]` | Cria agua ao redor do mouse. O raio default e `1` tile. |
| `/water drain [raio]` | Remove agua ao redor do mouse. O raio default e `2` tiles. |
| `/water clear` | Remove toda a agua do mundo. |
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

