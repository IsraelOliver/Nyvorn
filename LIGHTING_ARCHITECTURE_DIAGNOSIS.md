# Diagnóstico da Arquitetura de Renderização — Fase 1
## Ny'vorn Reborn — Sistema de Iluminação Atmosférica

**Data:** 2026-07-29  
**Status:** Diagnóstico Concluído — Pronto para Fase 2  
**Escopo:** Análise completa de renderização, câmera, chunks, RenderTargets, Assets e pontos de integração

---

## 1. COMO A RENDERIZAÇÃO FUNCIONA ATUALMENTE

### 1.1 Loop Principal (PlayingState.cs, lines 255-456)

**Entry Point:** `PlayingState.Draw(GameTime gameTime, SpriteBatch spriteBatch)`

**Características:**
- Uma única passagem de renderização direta para backbuffer
- Nenhum RenderTarget intermediário
- Múltiplas chamadas Begin/End de SpriteBatch (18+ passes)
- Suporte a wrapping horizontal (mundo infinito)
- Cada pass usa diferentes BlendStates e SamplerStates

### 1.2 Frame Preparation

Antes do loop de draw:
- `session.PrepareTerrainRender(graphicsDevice, screenW, screenH, worldOffset)` — Atualiza chunks visíveis
- `session.PrepareWorldLighting(graphicsDevice)` — Compila lightmap (já existe!)
- `session.PrepareTorchGlow(graphicsDevice)` — Compila glow map (já existe!)

**Insight importante:** Iluminação JÁ está sendo preparada e renderizada como texturas multiplicadas.

---

## 2. ORDEM DE DRAW DAS CAMADAS

A renderização segue esta sequência rigorosa:

```
1. Sky (LinearClamp)
   └─ DrawSky(spriteBatch, screenW, screenH)

2. Sun/Moon Effects (SunRays shader)
   ├─ DrawSunGlow(...)
   └─ DrawMoons(...)

3. Parallax Mountains (PointClamp)
   └─ DrawParallaxMountains(...)

4. Background Walls (PointClamp, looped)
   └─ DrawBackgroundWalls(...)

5. Back Trees (PointClamp, looped)
   └─ DrawTreeDecorations(..., TreeRenderLayer.Back)

6. Front Trees (PointClamp, looped)
   └─ DrawTreeDecorations(..., TreeRenderLayer.Front)

7. Water (PointClamp + AlphaBlend, looped)
   └─ DrawWater(...)

8. TERRAIN BASE (PointClamp, looped) ← MAIN GEOMETRY
   ├─ DrawTerrainBase(...) — tiles, sand, chão
   ├─ DrawWetnessOverlay(...) — [MultiplyBlend]
   └─ DrawWorldLighting(...) — [LinearClamp, MultiplyBlend] ← LUZ AMBIENTE ATUAL

9. Terrain Overlay (PointClamp, looped)
   └─ DrawTerrainOverlay(...)

10. World Entities (PointClamp, looped) ← PLAYER, ENEMIES, ITEMS
    └─ DrawLoopedWorldEntities(...)

11. Tissue Rendering (Additive + AlphaBlend, looped)
    ├─ DrawTissueHalo(...) — [Additive]
    └─ DrawTissueCore(...) — [AlphaBlend]

12. Non-Looped Entities (PointClamp)
    └─ DrawEntities(...)

13. Interior Overlay (AlphaBlend, looped)
    └─ DrawInteriorFocusOverlay(...)

14. Tissue Debug (AlphaBlend, looped)
    └─ DrawTissueDebug(...)

15. Rain & Night Overlay (AlphaBlend, screen-space)
    ├─ DrawRainFront(...)
    └─ DrawNightOverlay(...) ← SOMBRA GLOBAL (faz tudo ficar preto à noite)

16. Torch Glow (LinearClamp, Additive, looped) ← LUZ LOCAL PUNTUAL
    └─ DrawTorchGlow(...)

17. HUD & UI (PointClamp, screen-space)
    ├─ DrawHud(...)
    ├─ DrawMinimap(...)
    └─ UI/Console
```

### 1.3 Observações Críticas

- **DrawWorldLighting** está na linha 358, APÓS terrain base, usando MultiplyBlend
  - Significa que a luz ambiente já está multiplicando os tiles
  - Textura de luz já existe e é preparada a cada frame
  
- **DrawTorchGlow** está no FINAL (linha 442), usando Additive
  - Luz local puntual passa POR cima da sobreposição de noite
  - É por isso que tochas brilham mesmo à noite
  
- **DrawNightOverlay** é uma sobreposição global que escurece tudo
  - Aplicada ANTES das tochas
  - Color.Black com alpha parcial

---

## 3. ONDE O SISTEMA DE ILUMINAÇÃO DEVE ENTRAR

### 3.1 Situação Atual

**O que JÁ existe:**
- `WorldLightingSystem.cs` — Sistema completo de propagação de luz BFS
- `lightTexture` e `glowTexture` — Texturas já sendo renderizadas
- `PrepareWorldLighting()` e `PrepareTorchGlow()` — Já calculam luz a cada frame
- `DrawWorldLighting()` — Já multiplica a luz ambiente nos tiles
- `DrawTorchGlow()` — Já renderiza glow aditivo

**O que FALTA:**
- Renderização separada da cena (RenderTarget)
- Composição controlada (cena * lightmap + emissive)
- Luz sendo aplicada também a ENTIDADES (player, inimigos, itens)
- Debug tools
- Quantização/dithering para pixel art
- Bloom discreto

### 3.2 Pontos de Integração Necessários

**Linha 277:** Após `PrepareWorldLighting()`
- Adicionar passagem de preparação de entidades para iluminação

**Linha 358:** Antes de `DrawWorldLighting()`
- Renderizar cena em `SceneRenderTarget` em vez de direto ao backbuffer

**Linha 376-377:** Em `DrawLoopedWorldEntities()`
- Aplicar cor de luz ao desenhar player, inimigos, itens

**Linha 442:** Em `DrawTorchGlow()`
- Manter renderização aditiva de glow (já funciona bem)

**Linha 447-455:** Após HUD
- Renderização final de composição

---

## 4. RESOLUÇÃO INTERNA E ESCALONAMENTO

### 4.1 Valores Detectados

**Backbuffer:** `graphicsDevice.PresentationParameters.BackBufferWidth/Height`
- Variável em tempo real (window resize)

**Camera:** 
- `DefaultCameraZoom = 2f` — Zoom normal do jogo
- `InteriorCameraZoom = 3f` — Zoom em interiores
- Usa `Matrix.CreateTranslation() * Camera.GetViewMatrix()`
- Transform aplicado por pass (looping horizontal)

**Tile Size:** `WorldMap.TileSize` = 8 pixels (lógicos)
- Renderizado em 16x16 pixels na tela (2x escala)

**Pixel Art:**
- `SamplerState.PointClamp` usado para terrain
- `SamplerState.LinearClamp` usado para luz (interpolação suave)
- Implica que LightMap deve estar em resolução reduzida para suavidade

### 4.2 RenderTarget Requirements

**Recomendado:**

```csharp
// SceneRenderTarget — cena sem iluminação
Width: screenWidth
Height: screenHeight
Format: Color (ARGB8)
Usage: Render target
HiDef: true

// LightMapRenderTarget — iluminação
Width: screenWidth / 2  (ou 4, dependendo da suavidade desejada)
Height: screenHeight / 2
Format: Color (ARGB8)
Usage: Render target
HiDef: true

// EmissiveMapRenderTarget — elementos que brilham sozinhos
Width: screenWidth
Height: screenHeight
Format: Color (ARGB8)
Usage: Render target
HiDef: true
```

---

## 5. SISTEMA DE CHUNKS E VIEWPORT SIMULATION

### 5.1 Chunks Visíveis

`PlayingSessionViewCoordinator.UpdateSimulationViewport()`
- Calcula `activeSimulationChunks` baseado na câmera
- Usa `SimulationChunkBorder = 1` — margem além dos chunks visíveis
- Atualizado a cada frame

**Insight:** Iluminação pode aproveitar o mesmo sistema de chunks para limitação de cálculos.

### 5.2 Looping Horizontal

`GetVisibleLoopOffsets()` retorna índices de wraps mundiais
- Quando o player está perto da borda, o mundo se replica
- Cada replicação é drawn em um worldOffset diferente
- Importante para LightMap: precisa ser renderizado POR LOOP também

---

## 6. ASSETS E TEXTURAS

### 6.1 Texturas JÁ Gerenciadas

- `lightTexture` — armazenada em `PlayingSessionViewCoordinator`
- `glowTexture` — armazenada em `PlayingSessionViewCoordinator`
- Ambas com buffers de cor (`Color[]`) atualizados a cada frame

### 6.2 Effects/Shaders

**Existentes:**
- `SunRays.fx` — ray shader para sol/lua
- Default MonoGame sprite effect

**Necessários:**
- `LightingComposite.fx` — compor cena * lightmap + emissive
- `EmissiveExtract.fx` — extrair pixels brilhantes
- `BloomBlur.fx` — blur gaussiano (horizontal + vertical)

---

## 7. ARQUITETURA ATUAL - CLASSES E RESPONSABILIDADES

### 7.1 Hierarquia de Render

```
Main.Game
  ├─ PlayingState (IGameState)
  │  ├─ PlayingSession (contém tudo do jogo)
  │  │  └─ PlayingSessionViewCoordinator
  │  │     ├─ Camera2D
  │  │     ├─ WorldMap
  │  │     ├─ Player
  │  │     ├─ List<Enemy>
  │  │     ├─ List<WorldItem>
  │  │     ├─ WorldLightingSystem ← LUZ JÁ EXISTE
  │  │     ├─ TorchRuntimeSystem
  │  │     └─ [Mais sistemas...]
  │  └─ PlayerHubUI
  └─ Console debug
```

### 7.2 PlayingSessionViewCoordinator

**Responsabilidades atuais:**
- Gerenciar camera2D e viewport simulation
- Preparar renderização de terrain
- Armazenar lightTexture e glowTexture
- Chamar Draw* methods em ordem

**Será expandido para:**
- Gerenciar SceneRenderTarget, LightMapRenderTarget, EmissiveMapRenderTarget
- Coordenar composição final
- Debug visualization de lighting

### 7.3 PlayingSession

**Responsabilidades atuais:**
- Orquestrar todos os sistemas de jogo
- Chamar PrepareWorldLighting e PrepareTorchGlow
- Chamar métodos de draw via ViewCoordinator

**Será expandido para:**
- Passar RenderTargets para draw methods
- Coordenar aplicação de luz a entidades

---

## 8. FLUXO ATUAL DE PREPARAÇÃO DE LUZ

```csharp
// PlayingState.Draw()
session.PrepareWorldLighting(graphicsDevice);  // Linha 282
session.PrepareTorchGlow(graphicsDevice);      // Linha 283

// Interno (PlayingSession.cs)
// PrepareWorldLighting():
worldLightingSystem.Update(dt, cameraPos, zoom, screenW, screenH, skyColor);
lightingViewCoordinator.CopyLightGridTo(lightTextureBuffer);
lightTexture.SetData(lightTextureBuffer);

// PrepareTorchGlow():
// Similar, mas com glow color e additive
```

**Observação:** Já há um sistema de lightmap completo funcionando. Não precisa ser reimplementado.

---

## 9. CLASSES QUE SERÃO MODIFICADAS

### 9.1 Modificações Necessárias

1. **PlayingState.cs** (Draw method)
   - Adicionar RenderTargets
   - Renderizar cena em SceneRenderTarget
   - Chamar composição final

2. **PlayingSessionViewCoordinator.cs**
   - Gerenciar RenderTargets
   - Composição de cena
   - Debug visualization

3. **PlayingSession.cs**
   - Métodos DrawTerrainBase, DrawLoopedWorldEntities, etc.
   - Aceitar RenderTarget como parâmetro
   - Aplicar tint de iluminação a entidades

4. **Camera2D.cs** (possível)
   - Verificar se precisa ajustes para RenderTargets

### 9.2 Classes NÃO Afetadas

- WorldMap.cs — apenas renderiza
- WorldLightingSystem.cs — já funciona
- Player.cs, Enemy.cs, etc. — apenas mudar cor do draw

---

## 10. ARQUIVOS NOVOS NECESSÁRIOS

### 10.1 Sistema de Iluminação Estruturado

```
Engine/Graphics/Lighting/
├─ LightingRenderer.cs         ← Orquestrador principal
├─ LightingCompositor.cs       ← Compõe cena + luz
├─ EmissiveMapExtractor.cs     ← Extrai brilho
├─ BloomApplier.cs             ← Bloom post-process
├─ LightingDebugRenderer.cs    ← Visualizações debug
└─ LightingRenderTarget.cs     ← Gerenciamento de RenderTargets
```

### 10.2 Estruturas de Dados

```
Engine/Graphics/Lighting/
├─ LightingSettings.cs         ← Configuração centralizada
├─ LightRenderData.cs          ← Dados por frame
└─ LightingMaterialSettings.cs ← Por-material behavior
```

### 10.3 Shaders

```
Content/Effects/
├─ LightingComposite.fx        ← Main composition shader
├─ EmissiveExtract.fx          ← Emissive extraction
└─ BloomBlur.fx                ← Blur pass
```

---

## 11. RISCOS ARQUITETURAIS

### 11.1 Performance

**Risco:** Criar RenderTargets a cada frame
- **Mitigação:** Criar uma única vez, reutilizar. Recrear apenas se janela redimensionar.

**Risco:** RenderTargets muito grandes
- **Mitigação:** LightMap em resolução reduzida (50% é comum). Upscale com interpolação linear.

**Risco:** Aplicar iluminação a TODAS as entidades
- **Mitigação:** Limitar a apenas entidades visíveis (já done por chunks).

### 11.2 Pixel Art Quality

**Risco:** LinearClamp interpolation vai suavizar a luz
- **Mitigação:** Intencional — luz suave é desejada. Usar quantização se houver bands visíveis.

**Risco:** RenderTarget sampling artifacts
- **Mitigação:** Usar PointClamp para LightMap quando upscaled, ou pré-calcular em full resolution.

### 11.3 Arquitetura

**Risco:** PlayingSessionViewCoordinator fica muito grande
- **Mitigação:** Extrair LightingRenderer como classe separada, injetar dependências.

**Risco:** Circular dependency com WorldLightingSystem
- **Mitigação:** WorldLightingSystem permanece independente. Apenas lê do sistema, não modifica.

### 11.4 Compatibilidade

**Risco:** Night overlay + nova iluminação criam escuridão dupla
- **Mitigação:** Integrar night overlay DENTRO do lightmap, não como overlay.

**Risco:** Entidades antigas (sem iluminação) parecem "flutuantes"
- **Mitigação:** Aplicar iluminação default a todas as entidades no mínimo.

---

## 12. RESOLUÇÃO INTERNA DO JOGO

### 12.1 Escala de Renderização

- **Logical Tile:** 8x8 pixels
- **Screen Tile:** 16x16 pixels (2x zoom)
- **Default Zoom:** 2.0
- **Screen Resolution:** Variável (não fixed)

**Implicação para Lighting:**
- LightMap calcula por tile lógico (8x8)
- Upscale para screen resolution usando LinearClamp
- Composição acontece no espaço da tela

### 12.2 Pixel Perfect Positioning

```csharp
// Tiles renderizados com PointClamp
spriteBatch.Begin(samplerState: SamplerState.PointClamp, ...);

// Luz renderizada com LinearClamp (interpolação suave)
spriteBatch.Begin(samplerState: SamplerState.LinearClamp, ...);
```

Isso é CORRETO — luz pode ser suave, tiles devem ser sharp.

---

## 13. PLANO DE IMPLEMENTAÇÃO — 7 FASES

### Fase 1: ✅ DIAGNÓSTICO CONCLUÍDO
- [x] Mapear pipeline atual
- [x] Identificar onde iluminação entra
- [x] Documentar pontos de integração
- [x] Identificar RenderTargets necessários

### Fase 2: Composição Mínima
- [ ] Criar `LightingRenderTarget.cs` para gerenciar RenderTargets
- [ ] Renderizar cena em `SceneRenderTarget` (sem iluminação ainda)
- [ ] Criar shader `LightingComposite.fx`: `color = albedo * lightmap`
- [ ] Compor SceneRenderTarget * LightMap direto ao backbuffer
- [ ] Verificar que tudo ainda renderiza identicamente ao antes
- **Critério:** Pixel art permanece sharp, zero visual change

### Fase 3: Uma Luz Local Sem Oclusão
- [ ] Registrar fonte de luz (tocha)
- [ ] Renderizar luz radial simples no LightMap
- [ ] Testar cor, raio, intensidade
- **Critério:** Uma tocha próxima ao player ilumina corretamente

### Fase 4: Oclusão por Tiles
- [ ] Implementar bloqueio de luz por tiles sólidos
- [ ] Limitar propagação à região visível
- [ ] Adicionar dirty flags para otimização
- [ ] Debug: visualizar grid de luz
- **Critério:** Luz não atravessa paredes

### Fase 5: Múltiplas Luzes e Chunks
- [ ] Suportar múltiplas fontes
- [ ] Seleção de luzes visíveis
- [ ] Limite de fontes por frame
- [ ] Atualização per-chunk
- **Critério:** Múltiplas tochas funcionam

### Fase 6: Entidades e Emissive
- [ ] Player recebe cor de iluminação
- [ ] Inimigos iluminados
- [ ] Itens iluminados
- [ ] Emissive map para elementos especiais (chamas, brilho)
- **Critério:** Tudo na cena responde a luz

### Fase 7: Bloom e Refinamento
- [ ] Bloom discreto
- [ ] Quantização/dithering para transições suaves
- [ ] Presets de ambiente
- [ ] Debug tools
- **Critério:** Polished visual, pronto para produção

---

## 14. CRITÉRIOS DE ACEITAÇÃO — FASE 2

A Fase 2 está pronta quando:

- [ ] Projeto compila sem warnings
- [ ] Jogo inicia normalmente em PlayingState
- [ ] Mundo renderiza identicamente ao antes (zero visual regression)
- [ ] Nenhuma mecânica quebrada (movement, mining, items, etc.)
- [ ] RenderTargets NÃO são alocados a cada frame
- [ ] RenderTargets são recriados quando janela redimensiona
- [ ] Recursos são descartados corretamente (sem leaks)
- [ ] HUD não é afetado (renderizado após composição)
- [ ] Minimap não é afetado
- [ ] Pixel art permanece nítido (PointClamp para scene)
- [ ] Camera zoom continua funcionando
- [ ] Debug mode pode visualizar SceneRenderTarget vs final
- [ ] FPS não cai significativamente

---

## 15. DECISÕES DE DESIGN JÁ TOMADAS

### 15.1 Luz Ambiente vs Local

**Decisão:** Manter luz ambiente global calculada por WorldLightingSystem
- Vem do céu, bloqueada por tiles
- Muda ao longo do dia/noite
- Aplicada multiplicativa aos tiles

**Luzes Locais:** Tochas, fogueiras, etc.
- Renderizadas aditividade sobre night overlay
- Raio curto, alta intensidade
- Já funcionam bem

### 15.2 Normal Maps — NÃO AGORA

**Decisão:** Implementar iluminação simples primeiro
- Sem normal maps por enquanto
- Apenas tint-by-light-value
- Normal maps vêm na Fase 7+ se necessário

### 15.3 Sombras — Simples

**Decisão:** Sombras vêm de oclusão de tiles, não ray-casting
- Sombra de contato simples (drop shadow)
- Sem shadow mapping
- Suficiente para pixel art

---

## PRÓXIMOS PASSOS

1. **Aprovação do diagnóstico**
   - Você concorda com a análise?
   - Mudanças na abordagem?

2. **Implementar Fase 2**
   - Criar estrutura de RenderTargets
   - Adaptar PlayingState.Draw() para renderizar em RenderTarget
   - Testar que zero visual regression ocorre

3. **Build & Test**
   - Compilar
   - Rodar jogo
   - Validar contra critérios de aceitação

---

## ANEXO A: Mapa de Classes Relevantes

| Classe | Localização | Responsabilidade | Modificação Necessária |
|--------|-------------|------------------|----------------------|
| PlayingState | Game/States/ | Loop de render principal | Adicionar RenderTargets |
| PlayingSession | Game/States/ | Orquestração geral | Passar RenderTargets aos drawers |
| PlayingSessionViewCoordinator | Game/States/PlayingSession/ | Viewport, camera, draw calls | Gerenciar LightingRenderTarget |
| Camera2D | Engine/Graphics/ | View matrix | Possivelmente nada |
| WorldMap | Gameplay/World/ | Renderização de tiles | Nenhuma |
| WorldLightingSystem | Gameplay/World/Simulation/ | Cálculo de luz | Nenhuma (já completo) |
| Player, Enemy | Gameplay/Entities/ | Sprite e animação | Aplicar tint de iluminação |
| WorldItem | Gameplay/Items/ | Renderização de itens | Aplicar tint de iluminação |

---

**Diagnóstico concluído. Pronto para revisão e Fase 2.**
