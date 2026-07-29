# Correções Finais — Fase 2
## Before Implementation — Critical Fixes

**Data:** 2026-07-29  
**Status:** Erros corrigidos, pronto para aprovação final  
**Próximo passo:** Implementação autorizada após esta aprovação

---

## ERRO 1: DrawWorldLighting Position (CORRIGIDO)

### Problema Identificado
DrawWorldLighting estava sendo executado **ANTES de**:
- TerrainOverlay
- WorldEntities (looped: enemies, items, particles)
- Tissue (halo, core)
- DrawEntities (player não-looped)

Tudo desenhado DEPOIS não recebia multiplicação de iluminação.

### Solução: Ordem Obrigatória Corrigida

**Dentro do SceneRenderTarget, ordem correta:**

```
1. DrawTreeDecorations(Back)
2. DrawBackgroundWalls
3. DrawTreeDecorations(Front)
4. DrawWater
5. DrawTerrainBase
6. DrawWetnessOverlay
   └─ [Todos os componentes do mundo desenados]
7. DrawTerrainOverlay
8. DrawLoopedWorldEntities (enemies, items, particles, furniture UI)
9. DrawTissueHalo (EMISSIVO — será depois da luz, mas documentado)
10. DrawTissueCore (EMISSIVO — será depois da luz, mas documentado)
11. DrawTissueFieldOverlay (EMISSIVO)
12. DrawTissueDebug (EMISSIVO)
    └─ [Todos os sprites do mundo desenhados]
13. *** AGORA APLICAR DrawWorldLighting (MULTIPLY) ***
```

**Motivo:** A multiplicação afeta TUDO o que foi desenhado antes dela no RenderTarget.

---

## ERRO 2: Multiple SetRenderTarget Calls (CORRIGIDO)

### Problema Identificado
Pseudocódigo anterior chamava SetRenderTarget/Clear uma vez **por loop-offset**:

```csharp
for (int i = 0; i < visibleLoopOffsets.Count; i++) {
    graphicsDevice.SetRenderTarget(session.SceneRenderTarget);
    graphicsDevice.Clear(Color.Transparent);
    // desenhar...
    graphicsDevice.SetRenderTarget(null);
}
```

**Consequências:**
- RenderTarget recriado a cada offset (perdendo conteúdo anterior)
- Cópias do mundo renderizadas separadamente
- Composição/iluminação aplicada múltiplas vezes
- Performance terrível (3 SetRenderTarget calls por frame)

### Solução: Fluxo Unificado

**Novo fluxo:**

```
SetRenderTarget(SceneRenderTarget);
Clear(Color.Transparent);

for (int i = 0; i < visibleLoopOffsets.Count; i++) {
    // Desenhar TODAS as cópias do mundo (wrapped)
    DrawTreeDecorations(Back);
    DrawBackgroundWalls;
    DrawTreeDecorations(Front);
    DrawWater;
    DrawTerrainBase;
    DrawWetnessOverlay;
    DrawTerrainOverlay;
    DrawLoopedWorldEntities;
    DrawTissue*;
}

// Aplicar iluminação UMA ÚNICA VEZ sobre toda a cena
DrawWorldLighting (MULTIPLY);

SetRenderTarget(null);

// Blitar ao backbuffer uma única vez
Draw(SceneRenderTarget, Vector2.Zero, Color.White);
```

**Benefícios:**
- SetRenderTarget: 1 call
- Clear: 1 call
- Blitar ao backbuffer: 1 call
- Iluminação aplicada corretamente
- Performance ótima

---

## ERRO 3: DrawEntities() Investigação

### O Que DrawEntities() Contém (Exato)

**Localização:** PlayingSessionViewCoordinator.cs, linha 354-357

```csharp
public void DrawEntities(SpriteBatch spriteBatch, WorldLightingSystem lightingSystem)
{
    Player.Draw(spriteBatch, GetAmbientTintAt(lightingSystem, Player.Position));
}
```

**Conteúdo:** APENAS o Player, com tint de iluminação

**Observations:**
- Sem wrapping (não é looped)
- Não tem bounding box visível area culling
- Recebe iluminação via GetAmbientTintAt()
- Está FORA do RenderTarget no fluxo atual

### Versus DrawLoopedWorldEntities() (Exato)

**Localização:** PlayingSessionViewCoordinator.cs, linha 439-474

```csharp
public void DrawLoopedWorldEntities(
    SpriteBatch spriteBatch, 
    int screenWidth, int screenHeight, 
    float worldOffsetX, 
    WorldLightingSystem lightingSystem, 
    float visualTimeSeconds)
{
    // Culling: viewport + padding
    float viewWidth = screenWidth / Camera.Zoom;
    float viewHeight = screenHeight / Camera.Zoom;
    float localLeft = Camera.Position.X - worldOffsetX - EntityDrawPaddingPixels;
    float localTop = Camera.Position.Y - EntityDrawPaddingPixels;
    float localRight = localLeft + viewWidth + (EntityDrawPaddingPixels * 2f);
    float localBottom = localTop + viewHeight + (EntityDrawPaddingPixels * 2f);

    // Inimigos
    foreach (Enemy enemy in Enemies)
    {
        if (!IntersectsVisibleArea(enemy.Hurtbox, ...))
            continue;
        enemy.Draw(spriteBatch, GetAmbientTintAt(lightingSystem, enemy.Position));
        HealthBarRenderer.Draw(spriteBatch, enemy.Position + Vector2, health, maxHealth, ...);
    }

    // Itens
    foreach (WorldItem worldItem in WorldItems)
    {
        if (!IntersectsVisibleArea(worldItem.WorldBounds, ...))
            continue;
        worldItem.Draw(spriteBatch, GetAmbientTintAt(lightingSystem, worldItem.WorldBounds.Center));
    }

    // Partículas e UI
    BlockParticleSystem.Draw(spriteBatch, ...);
    DamageNumberSystem.Draw(spriteBatch, ...);
    
    // Furniture
    WorkbenchRuntimeSystem?.Draw(spriteBatch);
    FurnaceRuntimeSystem?.Draw(spriteBatch);
    TorchRuntimeSystem?.Draw(spriteBatch, visualTimeSeconds);
    DoorRuntimeSystem?.Draw(spriteBatch);
    ChairRuntimeSystem?.Draw(spriteBatch);
    TableRuntimeSystem?.Draw(spriteBatch);
    PlatformRuntimeSystem?.Draw(spriteBatch);
}
```

**Conteúdo:**
- Enemies (com health bars)
- WorldItems
- BlockParticleSystem (partículas de destruição)
- DamageNumberSystem (números flutuantes)
- WorkbenchRuntimeSystem (UI visual)
- FurnaceRuntimeSystem (UI visual)
- TorchRuntimeSystem (UI visual — apenas cosmético, não altera iluminação real)
- DoorRuntimeSystem (UI visual)
- ChairRuntimeSystem (UI visual)
- TableRuntimeSystem (UI visual)
- PlatformRuntimeSystem (UI visual)

### Decisão Crítica

**DrawEntities (Player não-looped):**
- Está FORA do RenderTarget no fluxo atual (PlayingState.cs, linha 396-398)
- Contem APENAS um sprite (Player)
- Recebe tint de iluminação, mas não está dentro da composição multiplicativa

**Solução Fase 2:**
- DrawEntities permanece FORA do RenderTarget (menos crítico)
- DrawLoopedWorldEntities já DENTRO do RenderTarget
- Player será iluminado de forma imprecisa (tint apenas, não multiplicação)
- Melhoria futura: mover DrawEntities DENTRO do RenderTarget, ANTES de DrawWorldLighting

**Nota:** Player pode parecer "mais brilhante" que inimigos à noite porque não está sob MultiplyBlend. Isso é ACEITÁVEL para Fase 2 (limitação documentada).

---

## ERRO 4: Tecido — Separação de Componentes

### Investigação de DrawTissue*

**DrawTissueHalo() (linha 20-67 de TissueNetworkRenderer.cs):**
- Desenha halós de tecido (cores violetas/magentas)
- Valores alpha baseados em `TissueConfig.WorldVisual.HaloAlphaBase + intensity`
- **Tipo:** Totalmente emissivo (visual puro)

**DrawTissueCore() (linha 69-122):**
- Desenha núcleo de tecido (cores rosa/ouro)
- Valores alpha baseados em `TissueConfig.WorldVisual.CoreAlphaBase + intensity`
- **Tipo:** Totalmente emissivo (visual puro)

**DrawTissueFieldOverlay():**
- Desenha sobreposição de campo
- **Tipo:** Totalmente emissivo

**DrawTissueDebug():**
- Debug visualization
- **Tipo:** Totalmente emissivo

**Conclusão:** Todo o Tecido é EMISSIVO. Não há componente "físico não-emissivo" separado atualmente.

### Status Fase 2

**Limitação aceita:** Tecido será renderizado ANTES de DrawWorldLighting, assim ficará multiplicado pela iluminação ambiente (escurecerá à noite).

**Behavior:**
- Tecido durante o dia: Visível em cores normais
- Tecido durante a noite: Visível mas mais escuro (multiplicado por iluminação noturna)
- Glow torcha: Ainda percorre por cima

**Limitação documentada:** Tecido não é totalmente emissivo (não brilha independentemente à noite).

**Melhoria futura (Fase 6+):** Separar DrawTissue* em pass separado após DrawWorldLighting com Additive/AlphaBlend.

---

## ERRO 5: Dois Flags Independentes (CORRIGIDO)

### Flags de Controle

**Flag 1: UseNewLightingPipeline**
```csharp
public bool UseNewLightingPipeline { get; set; } = false;  // Default: OLD
```

**Flag 2: LegacyNightOverlayMode**
```csharp
public bool LegacyNightOverlayMode { get; set; } = true;   // Default: ON
```

### Presets de Configuração

**Preset Antigo (Compatibilidade Total):**
```
UseNewLightingPipeline = false
LegacyNightOverlayMode = true
├─ Comportamento: Identicamente ao pipeline atual
└─ DrawWorldLighting renderizado direto ao backbuffer
```

**Preset Novo (Novo Pipeline com Night Overlay Herdada):**
```
UseNewLightingPipeline = true
LegacyNightOverlayMode = true
├─ Comportamento: RenderTarget + DrawWorldLighting multiplicativo
└─ DrawNightOverlay ainda aplicado como overlay global
└─ Resultado: Escuro demais à noite (dupla multiplicação)
└─ Útil para comparação visual antes/depois
```

**Preset Novo Limpo (Recomendado para testes):**
```
UseNewLightingPipeline = true
LegacyNightOverlayMode = false
├─ Comportamento: RenderTarget + DrawWorldLighting sem overlay
└─ DrawNightOverlay desativado
└─ Resultado: Iluminação "pura" do sistema BFS
└─ Útil para verificar correção sem ruído visual
```

### Proprietário das Flags

**PlayingSessionViewCoordinator:**
```csharp
public bool UseNewLightingPipeline { get; set; } = false;
public bool LegacyNightOverlayMode { get; set; } = true;
```

**Forward em PlayingSession.cs:**
```csharp
public bool UseNewLightingPipeline 
{
    get => ViewCoordinator.UseNewLightingPipeline;
    set => ViewCoordinator.UseNewLightingPipeline = value;
}
```

**Acesso em PlayingState.Draw():**
```csharp
if (session.UseNewLightingPipeline)
{
    // Nova pipeline com RenderTarget
}
else
{
    // Pipeline antiga (compatibilidade)
}

if (session.LegacyNightOverlayMode)
{
    session.DrawNightOverlay(spriteBatch, screenW, screenH);
}
```

---

## ERRO 6: Proprietário Único do RenderTarget

### Decisão: PlayingSessionViewCoordinator

**PlayingSessionViewCoordinator** é o proprietário único:
- Cria SceneRenderTarget
- Mantém referência
- Redimensiona em window resize
- Descarta (Dispose) corretamente
- Gerencia capacidade como já faz para lightTexture

**Hierarquia:**

```
PlayingSessionViewCoordinator
├─ SceneRenderTarget (new Texture2D)
├─ SceneRenderTargetCapacityWidth/Height
├─ GetSceneRenderTarget() → accessor
├─ CreateSceneRenderTarget(graphicsDevice, w, h)
├─ DisposeSceneRenderTarget()
└─ IsSceneRenderTargetValid → bool property

PlayingSession
└─ Forward properties ao ViewCoordinator

PlayingState.Draw()
└─ Acessa via session.GetSceneRenderTarget()
```

### Anti-pattern Evitado

**NÃO fazer:**
```csharp
// PlayingState.cs
private Texture2D sceneRenderTarget;  // ← DUPLICADO

// PlayingSession.cs
public Texture2D SceneRenderTarget { get; set; }  // ← DUPLICADO

// PlayingSessionViewCoordinator.cs
private Texture2D sceneRenderTarget;  // ← ORIGINAL
```

Mantém uma ÚNICA fonte de verdade em ViewCoordinator.

---

## RESUMO: PSEUDOCÓDIGO CORRIGIDO (Ponto 7)

### PlayingState.Draw() — Nova Ordem Completa

```csharp
void Draw(GameTime gameTime, SpriteBatch spriteBatch)
{
    int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
    int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
    float worldWidthPixels = session.WorldMap.PixelWidth;
    IReadOnlyList<int> visibleLoopOffsets = GetVisibleLoopOffsets(screenW, worldWidthPixels);

    // ========== PREPARAÇÃO (A CADA FRAME) ==========
    for (int i = 0; i < visibleLoopOffsets.Count; i++)
    {
        int loopIndex = visibleLoopOffsets[i];
        float worldOffset = loopIndex * worldWidthPixels;
        session.PrepareTerrainRender(graphicsDevice, screenW, screenH, worldOffset);
    }
    session.PrepareWorldLighting(graphicsDevice);
    session.PrepareTorchGlow(graphicsDevice);

    // ========== PIPELINE SELECIONADO ==========
    if (session.UseNewLightingPipeline)
    {
        DrawWithNewLightingPipeline(spriteBatch, screenW, screenH, visibleLoopOffsets, worldWidthPixels);
    }
    else
    {
        DrawWithLegacyPipeline(spriteBatch, screenW, screenH, visibleLoopOffsets, worldWidthPixels);
    }
}

// ========== NOVO PIPELINE (com RenderTarget) ==========
void DrawWithNewLightingPipeline(SpriteBatch spriteBatch, int screenW, int screenH, 
                                 IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels)
{
    // ===== FASE A: SKY E FUNDO (SEM ILUMINAÇÃO) =====
    spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
    session.DrawSky(spriteBatch, screenW, screenH);
    spriteBatch.End();

    session.DrawSunGlow(spriteBatch, screenW, screenH);
    session.DrawMoons(spriteBatch, screenW, screenH);

    spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    session.DrawParallaxMountains(spriteBatch, screenW, screenH);
    spriteBatch.End();

    // ===== FASE B: MUNDO ILUMINÁVEL (RENDERIZAR EM SCENERENDERTTARGET) =====
    graphicsDevice.SetRenderTarget(session.GetSceneRenderTarget());
    graphicsDevice.Clear(Color.Transparent);

    // Loop-wrap único: todas as cópias dentro do RenderTarget
    for (int i = 0; i < visibleLoopOffsets.Count; i++)
    {
        int loopIndex = visibleLoopOffsets[i];
        float worldOffset = loopIndex * worldWidthPixels;
        Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

        // Camada 1: Background
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Back);
        session.DrawBackgroundWalls(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Camada 2: Front trees
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Front);
        spriteBatch.End();

        // Camada 3: Água
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
        session.DrawWater(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Camada 4: Terrain base
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawTerrainBase(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Camada 5: Wetness
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: MultiplyBlend, transformMatrix: transform);
        session.DrawWetnessOverlay(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Camada 6: Terrain overlay
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawTerrainOverlay(spriteBatch);
        spriteBatch.End();

        // Camada 7: Looped entities (enemies, items, particles, furniture)
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Camada 8: Tecido (EMISSIVO — será multiplicado, limitação Fase 2)
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.Additive, transformMatrix: transform);
        session.DrawTissueHalo(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
        session.DrawTissueCore(spriteBatch, screenW, screenH, worldOffset);
        session.DrawTissueFieldOverlay(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
        session.DrawTissueDebug(spriteBatch);
        spriteBatch.End();
    }

    // ===== ILUMINAÇÃO APLICADA (sobre toda a cena renderizada) =====
    // LinearClamp interpola lightTexture suavemente
    // MultiplyBlend tinta toda a cena
    for (int i = 0; i < visibleLoopOffsets.Count; i++)
    {
        int loopIndex = visibleLoopOffsets[i];
        float worldOffset = loopIndex * worldWidthPixels;
        Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: MultiplyBlend, transformMatrix: transform);
        session.DrawWorldLighting(spriteBatch, worldOffset);
        spriteBatch.End();
    }

    // ===== SAIR DO SCENERENDERTTARGET =====
    graphicsDevice.SetRenderTarget(null);

    // ===== COMPOR AO BACKBUFFER (screen-space, sem transform) =====
    spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    spriteBatch.Draw(session.GetSceneRenderTarget(), Vector2.Zero, Color.White);
    spriteBatch.End();

    // ===== FASE C: INTERIOR + EFEITOS PÓS-ILUMINAÇÃO =====
    for (int i = 0; i < visibleLoopOffsets.Count; i++)
    {
        int loopIndex = visibleLoopOffsets[i];
        float worldOffset = loopIndex * worldWidthPixels;
        Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
        session.DrawInteriorFocusOverlay(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();
    }

    // Player não-looped (fora do RenderTarget — limitação documentada)
    spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: session.Camera.GetViewMatrix());
    session.DrawEntities(spriteBatch);
    spriteBatch.End();

    // ===== FASE D: EFEITOS SCREEN-SPACE =====
    spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
    session.DrawRainFront(spriteBatch, screenW, screenH);
    if (session.LegacyNightOverlayMode)
        session.DrawNightOverlay(spriteBatch, screenW, screenH);
    spriteBatch.End();

    // Torch glow (ADDITIVE, punch-through)
    for (int i = 0; i < visibleLoopOffsets.Count; i++)
    {
        int loopIndex = visibleLoopOffsets[i];
        float worldOffset = loopIndex * worldWidthPixels;
        Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: BlendState.Additive, transformMatrix: transform);
        session.DrawTorchGlow(spriteBatch, worldOffset);
        spriteBatch.End();
    }

    // ===== FASE E: INTERFACE (SEM ILUMINAÇÃO) =====
    spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    session.DrawHud(spriteBatch, screenW, screenH);
    if (minimapVisible)
        session.DrawMinimap(spriteBatch, screenW, screenH, minimapTissueMode);
    playerHubUI.Draw(spriteBatch, ...);
    if (showFps)
        DrawFpsCounter(spriteBatch);
    if (consoleOpen)
        DrawConsole(spriteBatch, screenW);
    spriteBatch.End();
}

// ===== PIPELINE LEGADA (compatibilidade) =====
void DrawWithLegacyPipeline(SpriteBatch spriteBatch, int screenW, int screenH,
                            IReadOnlyList<int> visibleLoopOffsets, float worldWidthPixels)
{
    // [Mesmo código que PlayingState.Draw() atualmente — sem RenderTarget]
    // [Mantém comportamento idêntico]
}
```

---

## ORDEM EXATA DAS CAMADAS

### Antes da Iluminação (Dentro do RenderTarget)

```
Sky (não-iluminado)
Sun/Moons (não-iluminado)
Parallax Mountains (não-iluminado)
└─ [FIM DO FUNDO]

Background Walls (iluminável)
Back Trees (iluminável)
Front Trees (iluminável)
Water (iluminável)
Terrain Base (iluminável)
Wetness Overlay (iluminável)
Terrain Overlay (iluminável)
Looped Entities (iluminável)
  ├─ Enemies + health bars
  ├─ WorldItems
  ├─ BlockParticles
  ├─ DamageNumbers
  └─ Furniture UI
Tissue Halo (emissivo — será multiplicado, limitação)
Tissue Core (emissivo — será multiplicado, limitação)
Tissue Field Overlay (emissivo — será multiplicado, limitação)
Tissue Debug (emissivo — será multiplicado, limitação)
└─ [TUDO RENDERIZADO NO SCENERENDERTTARGET]
```

### Iluminação Aplicada (Multiplicativa)

```
DrawWorldLighting (MULTIPLY) ← Afeta TUDO acima
```

### Depois da Iluminação (Screen-space + Additive)

```
Interior Focus Overlay (AlphaBlend)
Player não-looped (DefaultBlend) ← Limitação documentada
Rain Front (AlphaBlend)
Night Overlay (AlphaBlend — debug mode)
Torch Glow (Additive) ← Punch-through
HUD/UI/Console (screen-space) ← Sem iluminação
```

---

## CONTEÚDO EXATO: DrawEntities()

**Localização:** PlayingSessionViewCoordinator.cs:354-357

**Código:**
```csharp
public void DrawEntities(SpriteBatch spriteBatch, WorldLightingSystem lightingSystem)
{
    Player.Draw(spriteBatch, GetAmbientTintAt(lightingSystem, Player.Position));
}
```

**Contém:** Apenas Player com tint

**Não contém:** Nada mais

**Status Fase 2:** Permanece FORA do RenderTarget (limitação documentada)

---

## PROPRIETÁRIO FINAL DO RENDERTARGET

### Hierarquia Única

```
PlayingSessionViewCoordinator (PROPRIETÁRIO)
├─ private Texture2D sceneRenderTarget
├─ private int sceneRenderTargetCapacityWidth
├─ private int sceneRenderTargetCapacityHeight
├─ public Texture2D GetSceneRenderTarget() → accessor
├─ public void CreateSceneRenderTarget(GraphicsDevice, width, height)
├─ public void DisposeSceneRenderTarget()
└─ public bool IsSceneRenderTargetValid { get; }

PlayingSession (FORWARD APENAS)
└─ public Texture2D GetSceneRenderTarget() { return ViewCoordinator.GetSceneRenderTarget(); }

PlayingState (ACESSO APENAS)
└─ session.GetSceneRenderTarget()
```

**Sem duplicação.** Fonte única de verdade.

---

## LISTA FINAL DE ARQUIVOS ALTERADOS

### PlayingState.cs
- **Linhas ~255-456 (método Draw):**
  - Reorganizar com novo pipeline condicional
  - Adicionar `if (session.UseNewLightingPipeline) { ... } else { ... }`
  - Mover sky/mountains FORA do RenderTarget
  - Mover terrain loop DENTRO do RenderTarget
  - Aplicar DrawWorldLighting DENTRO do RenderTarget
  - Blitar RenderTarget ao backbuffer
  - Mover torch glow DEPOIS
  - Mover HUD DEPOIS

### PlayingSession.cs
- **Linhas ~100:** Forward GetSceneRenderTarget()
- **Linhas ~100:** Forward UseNewLightingPipeline property
- **Linhas ~100:** Forward LegacyNightOverlayMode property

### PlayingSessionViewCoordinator.cs
- **Linhas ~25:** Adicionar campos de RenderTarget
  ```csharp
  private Texture2D sceneRenderTarget;
  private int sceneRenderTargetCapacityWidth;
  private int sceneRenderTargetCapacityHeight;
  ```
  
- **Linhas ~25:** Adicionar flags
  ```csharp
  public bool UseNewLightingPipeline { get; set; } = false;
  public bool LegacyNightOverlayMode { get; set; } = true;
  ```

- **Linhas ~100:** Adicionar métodos
  ```csharp
  public Texture2D GetSceneRenderTarget() { return sceneRenderTarget; }
  
  public void CreateSceneRenderTarget(GraphicsDevice graphicsDevice, int width, int height)
  {
      // Mesmo padrão que lightTexture (grow-only, defensive)
  }
  
  public void DisposeSceneRenderTarget()
  {
      sceneRenderTarget?.Dispose();
      sceneRenderTarget = null;
  }
  
  public bool IsSceneRenderTargetValid { get; }
  ```

- **Linhas ~100:** Adicionar para recreação em window resize
  ```csharp
  public void RecreateRenderTargetsIfNeeded(GraphicsDevice graphicsDevice, int screenW, int screenH)
  {
      // Verificar se capacidade é insuficiente, recrear se necessário
  }
  ```

### Nenhum Arquivo Novo Obrigatório
- LightingRenderTargetManager.cs pode ser Fase 2.5 (optional)
- Nenhum shader novo necessário

---

## LIMITAÇÕES DOCUMENTADAS — FASE 2

| Limitação | Motivo | Fase de Fix |
|-----------|--------|------------|
| Player não-looped fora do RenderTarget | Simplificação v1 | Fase 2.5+ |
| Tecido multiplicado por iluminação (não totalmente emissivo) | Não há separação física/emissivo | Fase 6+ |
| Sem bloom | Fora de escopo | Fase 7+ |
| Sem normal maps | Fora de escopo | Fase 7+ |
| Sem dithering | Fora de escopo | Fase 7+ |
| Sem per-entity light sampling | Fora de escopo v1 | Fase 4+ |

---

**Documento de Correções Finais Completo. Pronto para Aprovação e Implementação.**
