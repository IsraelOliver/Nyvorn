# Project Cleanup v0.2.0

Este documento acompanha a limpeza tecnica antes de expandir o mundo sandbox.
O objetivo e reduzir restos de prototipo sem trocar o foco para features grandes.

## Escopo Atual

- Consolidar assets e referencias do `Content`.
- Manter somente sistemas usados no loop jogavel atual.
- Separar melhor base tecnica de expansoes futuras.
- Preservar saves, worldgen e gameplay existentes enquanto a limpeza avanca.

## Etapas

### 1. Content e Assets

Status: em andamento.

Feito:
- Removidas copias soltas de `playerDown_sheet.png` e `playerUp_sheet.png` da raiz de `Content`.
- Mantidas as versoes corretas em `Content/entities/player`.
- Removidos assets antigos nao usados:
  - `Content/blocks/grass_spritesheet_upscaled.png`
  - `Content/blocks/sand_block.png`
- Conferido que `Content.mgcb` aponta para os assets atuais.

Proximo:
- Revisar se `spear.png` e `Spear.cs` continuam como sistema ativo ou se ficam fora da base.
- Confirmar quais assets de teste ainda devem permanecer.

### 2. Player e Animacao

Status: concluido na base atual.

Objetivo:
- Manter apenas o sistema modular atual de lower body, upper body e arma/ferramenta integrada.
- Remover qualquer referencia conceitual ao sistema antigo.
- Documentar root, pivo, offsets e regras de bounce.

Feito:
- Removido o caminho antigo `PlayerAttackUpTexture`, que ja era apenas alias de `PlayerUpTexture`.
- `Player` agora recebe somente `playerDown` e `playerUp`.
- `PlayerAnimator` teve a API reduzida para os dados realmente usados:
  - `MovementFrame`
  - `UpperFrame`
  - `MovementFrameIndex`
  - `UpperFrameIndex`
  - `DrawLowerBody`, `DrawUpperBody` e `DrawLayer`
- Comentarios do root/pivo e do offset de locomocao foram normalizados.
- Confirmado que nao ha referencias restantes ao sistema antigo:
  - `playerUp_shortAttack`
  - `bodyTexture_base`
  - `handBackTexture_base`
  - `handFrontTexture_base`
  - `legsTexture_base`
  - `player_dodge`

Regra atual:
- A posicao logica do player continua sendo o root no centro dos pes.
- Lower body usa a animacao de movimento.
- Upper body usa a animacao superior, mas recebe o mesmo `OffsetY` da locomocao.
- Armas/ferramentas podem substituir o upper body via `Weapon.ReplacesPlayerUpperBody`.
- A picareta usa a propria folha como upper body integrado.

### 3. Itens, Hotbar e Inventario

Status: em andamento.

Objetivo:
- Centralizar regras de coleta, drop, stack e prioridade de slots.
- Garantir que item no mundo, inventario e hotbar usem frames corretos.
- Separar constantes de layout da UI em um lugar mais claro.

Feito:
- Coleta do mundo usa regra dedicada em `TryStoreCollectedDefinition`.
- Itens coletados juntam em pilhas existentes e so depois escolhem slot vazio.
- Se o inventario nao tiver aquele item, slot vazio da hotbar tem prioridade.
- Criacao de item fisico no mundo passa por `SpawnWorldItem`.
- `HudRenderer` agora reutiliza helpers para desenhar icone e numero de stack.
- Layout da hotbar e do inventario foi separado em helpers internos:
  - `HotbarLayout`
  - `InventoryLayout`
  - `GetHotbarLayout`
  - `GetInventoryLayout`

Proximo:
- Avaliar extrair layout da UI para uma classe propria quando inventario ganhar skin final.

### 4. Combate e Ferramentas

Status: em andamento.

Objetivo:
- Definir o minimo da base: mao, picareta e arma inicial.
- Separar melhor ferramenta de arma se a diferenca crescer.
- Confirmar hitboxes ativas por frame.

Feito:
- Removidos da base ativa:
  - `ShortStick`
  - `Spear`
  - `NullWeapon`
- `Content/weapons` agora mantem apenas `pickaxe_sheet.png`.
- `Content.mgcb` nao registra mais `shortStick.png` nem `spear.png`.
- `ItemDefinitions` registra apenas a picareta como item equipado ativo.
- O spawn inicial de itens cria apenas a picareta.
- `Weapon` recebeu base de poder:
  - `PowerTier`
  - `HitDamage`
  - `HitKnockbackX`
  - `HitKnockbackY`
- A picareta ficou como ferramenta/arma hibrida tier 1:
  - quebra blocos
  - causa dano baixo
  - aplica knockback maior para afastar inimigos
- `CombatConfig` foi removido; dano e knockback agora vem do proprio `IHitSource`.
- `PlayerCombat` continua como orquestrador, mas foi organizado em blocos menores:
  - cooldowns
  - ataque
  - hitbox ativa
  - dodge
  - dano recebido
- Duracao de ataque saiu de `PlayerConfig` e agora pertence a `Weapon.AttackDuration`.

Proximo:
- Avaliar se dodge deve sair de `PlayerCombat` no futuro, caso cresca.
- Ajustar valores finos de dano/knockback da picareta durante teste jogavel.

### 5. Mundo

Status: pendente.

Objetivo:
- Manter `WorldMap` e worldgen confiaveis antes de adicionar biomas.
- Validar spawn, cavernas e entradas naturais.
- Preparar `SurfacePolishPass` como proximo passo de mundo.

### 6. Save e Debug

Status: pendente.

Objetivo:
- Revisar compatibilidade de saves.
- Remover debug visual que nao ajuda mais.
- Manter diagnosticos de worldgen que ainda servem para iterar.
