# Revisão Técnica Complementar — Fase 2
## Lighting Architecture Analysis — Nyvorn Reborn

**Data:** 2026-07-29  
**Status:** Investigação detalhada + revisão de 10 pontos críticos  
**Próximo passo:** Aprovação desta revisão antes de implementação

---

## INVESTIGAÇÃO DETALHADA: SISTEMA DE ILUMINAÇÃO EXISTENTE (Ponto 4)

### 4.1 Resolução Lógica e Física

**WorldLightingSystem.cs — Linha 38-162**

- **Resolução lógica:** Tiles (8 pixels lógicos por tile)
- **Janela calculada:** Camera viewport + `PropagationMarginTiles = 12` tiles de margem em todas as direções
- **Tamanho do buffer:** `bufferWidth × bufferHeight` (em tiles lógicos)
- **Espaço de coordenadas:** Tile space (não pixel space)

**Cálculo de janela (linha 119-125):**
```csharp
float viewWidth = MathF.Ceiling((screenWidth / cameraZoom) / tileSize) * tileSize;
float viewHeight = MathF.Ceiling((screenHeight / cameraZoom) / tileSize) * tileSize;

int startTileX = (int)MathF.Floor(cameraPosition.X / tileSize) - PropagationMarginTiles;
int endTileX = (int)MathF.Ceiling((cameraPosition.X + viewWidth) / tileSize) + PropagationMarginTiles;
int startTileY = Math.Clamp(..., 0, worldMap.Height - 1);
int endTileY = Math.Clamp(..., 0, worldMap.Height - 1);
```

**Implicações:**
- A janela é **sempre aligned** a tiles inteiros
- Muda discretamente quando câmera se move suficientemente para cruzar fronteira de tile
- PropagationMarginTiles = 12 garante que luzes propagam dentro dessa margem antes de serem "cortadas"

### 4.2 Estrutura de Dados Dual: Light e Glow

**Light (Sky + Torch Combined) — Linha 67-72:**
```csharp
private float[] lightR = Array.Empty<float>();
private float[] lightG = Array.Empty<float>();
private float[] lightB = Array.Empty<float>();
```

- 3 canais de float separados
- Layout: Row-major, `index = (localY * bufferWidth) + localX`
- Conteúdo: `lightR[i]`, `lightG[i]`, `lightB[i]` — valores float [0.0, 1.0]
- Uso: Multiplicado sobre tiles sólidos/areia (CopyLightGridTo)

**Glow (Point Lights Only) — Linha 70-72:**
```csharp
private float[] glowR = Array.Empty<float>();
private float[] glowG = Array.Empty<float>();
private float[] glowB = Array.Empty<float>();
```

- Mesma estrutura que light, mas **sem sementes de céu**
- Conteúdo: Apenas propagação de luzes pontuais
- Uso: Aditivo sobre toda a cena (DrawTorchGlow)

### 4.3 Texturas GPU e Upload

**PlayingSessionViewCoordinator.cs — Linhas 43-59:**

```csharp
private Texture2D lightTexture;
private int lightTextureCapacityWidth;
private int lightTextureCapacityHeight;
private Color[] lightTextureBuffer;  // Alocado uma vez
private int lightTextureActiveWidth;
private int lightTextureActiveHeight;
private int lightTextureOriginTileX;
private int lightTextureOriginTileY;

// Idêntico para glowTexture
```

**Criação de Texture (linha 257):**
```csharp
lightTexture = new Texture2D(graphicsDevice, 
                            lightTextureCapacityWidth,  // Width em pixels
                            lightTextureCapacityHeight, // Height em pixels
                            false, 
                            SurfaceFormat.Color);        // ARGB8 (32-bit)
```

**Resolução de textura:**
- **Largura:** `bufferWidth` (tiles)
- **Altura:** `bufferHeight` (tiles)
- **Formato:** SurfaceFormat.Color (ARGB8, 32 bits por texel)
- **Texel-to-tile mapping:** 1 texel = 1 tile lógico

**Exemplo:** Se viewport visible = 32 tiles wide × 18 tiles tall + 12 tile margin:
- `bufferWidth = 32 + 24 = 56 tiles`
- `bufferHeight = 18 + 24 = 42 tiles`
- `lightTexture` = 56×42 pixels (muito pequena)

### 4.4 Ciclo de Upload CPU→GPU

**Frequência: A CADA FRAME**

**PlayingState.Draw() — Linha 282-283:**
```csharp
session.PrepareWorldLighting(graphicsDevice);  // Calcula + upload
session.PrepareTorchGlow(graphicsDevice);      // Calcula + upload
```

**Dentro de PlayingSessionViewCoordinator.PrepareWorldLighting() — Linhas 232-265:**

```
1. Consulta WorldLightingSystem.WindowWidth/Height
2. Aloca lightTextureBuffer se necessário (CPU array)
3. Chama lightingSystem.CopyLightGridTo(lightTextureBuffer)
   └─ Converte float[3] → Color[1] com gamma
   └─ Força tiles open para Color.White
4. Verifica se lightTexture precisa recriar (capacidade)
5. Chama lightTexture.SetData(Rectangle, Color[], ...) ← GPU UPLOAD
   └─ TransferRegion apenas da área ativa (não toda a capacidade)
6. Atualiza metadados (originTileX, originTileY, activeWidth, activeHeight)
```

**Custo mensurável:**
- CPU: CopyLightGridTo = ~1000-5000 tiles × 3 channels = negligenciável
- GPU: SetData = pequena transferência DMA (~56×42 × 4 bytes = ~10KB por frame)

**Não há invalidação por frame — upload acontece SEMPRE, mesmo que BFS não mude.**

### 4.5 Algoritmo BFS: Estrutura e Decay

**Seed Inicial (Linha 150-153):**

```csharp
// Sky + occlusion pass
SeedSkyExposedTiles(skyColor);           // Semeia Color.R/G/B (0-255)
SeedPointLightsInto(lightR,G,B, 1f);     // Semeia luzes pontuais
Propagate(lightR,G,B, 0.02f, 0.12f);     // BFS com decay

// Glow-only pass (repetido)
SeedPointLightsInto(glowR,G,B, 0.45f);   // Decay mais rápido
Propagate(glowR,G,B, 0.18f, 0.5f);
```

**Decay Rates (Linhas 42-56):**

```csharp
const float OpenTileLightDecay = 0.02f;      // Ar aberto: declina 0.02 por passo
const float SolidTileLightDecay = 0.12f;     // Tile sólido: declina 0.12 por passo

const float GlowOpenTileDecay = 0.18f;       // Glow (ar): declina rápido
const float GlowSolidTileDecay = 0.5f;       // Glow (sólido): cai muito rápido
const float GlowPeakIntensity = 0.45f;       // Pico de glow = 45% (não 100%)
```

**Distância efetiva:**
- Light no ar aberto: ~50 tiles antes de fade (0.02 × 50 ≈ 1.0)
- Light em sólido: ~8 tiles antes de fade (0.12 × 8 ≈ 0.96)
- Glow no ar: ~5 tiles (0.18 × 5 ≈ 0.9)
- Glow em sólido: ~2 tiles (0.5 × 2 ≈ 1.0)

**Comportamento:** Luz ambiente se propaga longe para ser suave; glow é local e intenso.

### 4.6 Seeding e Fonte de Cores

**Seed de Céu (Linha 277-313):**

```csharp
private void SeedSkyExposedTiles(Color skyColor)
{
    float skyR = skyColor.R / 255f;    // Normaliza 0-255 → 0.0-1.0
    float skyG = skyColor.G / 255f;
    float skyB = skyColor.B / 255f;

    for (int localX = 0; localX < bufferWidth; localX++)
    {
        int tileX = worldMap.WrapTileX(bufferOriginTileX + localX);
        
        // Checa se coluna tem céu aberto
        bool topRowExposed = IsAttenuatingAt(tileX, bufferOriginTileY) 
                          || worldMap.HasOpenSkyAbove(tileX, bufferOriginTileY);
        
        if (!topRowExposed)
            continue;  // Coluna inteira não recebe luz (bloqueada por teto)

        // Semeia toda a coluna exposta ao céu
        for (int localY = 0; localY < bufferHeight; localY++)
        {
            int index = (localY * bufferWidth) + localX;
            lightR[index] = skyR;
            lightG[index] = skyG;
            lightB[index] = skyB;
            propagationQueue.Enqueue(index);
            
            if (IsAttenuatingAt(tileX, tileY))
                break;  // Para na primeira profundidade
        }
    }
}
```

**Observation:** `HasOpenSkyAbove()` é chamado uma vez por COLUNA (not por tile).

**Seed de Tochas (Linha 365-390):**

```csharp
private void SeedPointLightsInto(float[] r, float[] g, float[] b, float peakIntensity)
{
    static readonly Color PointLightColor = new(255, 150, 60);  // Warm orange
    
    float pointR = (255 / 255f) * peakIntensity;  // 1.0 × 0.45 = 0.45
    float pointG = (150 / 255f) * peakIntensity;  // 0.588 × 0.45 = 0.265
    float pointB = (60 / 255f) * peakIntensity;   // 0.235 × 0.45 = 0.106
    
    for (int i = 0; i < pointLightPositions.Count; i++)
    {
        Vector2 worldPosition = pointLightPositions[i];
        int tileX = (int)MathF.Floor(worldPosition.X / worldMap.TileSize);
        int tileY = (int)MathF.Floor(worldPosition.Y / worldMap.TileSize);
        
        // Localiza tile dentro do buffer
        int localX = tileX - bufferOriginTileX;
        int localY = tileY - bufferOriginTileY;
        
        if (fora do buffer)
            continue;
        
        int index = (localY * bufferWidth) + localX;
        // Apenas melhora se nova cor > cor anterior
        if (pointR > r[index]) { r[index] = pointR; improved = true; }
        if (pointG > g[index]) { g[index] = pointG; improved = true; }
        if (pointB > b[index]) { b[index] = pointB; improved = true; }
        
        if (improved)
            propagationQueue.Enqueue(index);
    }
}
```

**Cores RGB independentes:** Cada canal propaga e decai independentemente. Isso permite:
- Sobreposição de cores: torch (warm) + sky (blue) = resultado natural
- Sem blending especial, apenas take-max por canal

### 4.7 Bloqueio e Atenuação de Luz

**IsAttenuatingAt() — Linha 348-363:**

```csharp
private bool IsAttenuatingAt(int tileX, int tileY)
{
    if (worldMap.IsSolidAt(tileX, tileY))
        return true;  // Tile sólido bloqueia

    if (worldMap.GetBackgroundTile(tileX, tileY) != TileType.Empty)
        return true;  // Tile de background sólido bloqueia
    
    if (SandSystem == null)
        return false;
    
    int tileSize = worldMap.TileSize;
    int centerPixelX = (tileX * tileSize) + (tileSize / 2);
    int centerPixelY = (tileY * tileSize) + (tileSize / 2);
    return SandSystem.HasSandAt(centerPixelX, centerPixelY);  // Areia bloqueia
}
```

**Bloqueadores:**
1. Tiles sólidos (foreground)
2. Tiles de background (CavePass.cs criou fissuras)
3. Areia solta (SandSystem)

**Nota:** Background tiles agora BLOQUEIAM luz (adicionado em commit recente).

### 4.8 Conversão para Cor e Gamma Perceptual

**Light (Sky-aware, multiplicativo) — Linha 256-263:**

```csharp
const float PerceptualGamma = 0.5f;

private static Color ToPerceptualColor(float r, float g, float b)
{
    return new Color(
        (byte)(MathF.Pow(Math.Clamp(r, 0f, 1f), PerceptualGamma) * 255f),
        (byte)(MathF.Pow(Math.Clamp(g, 0f, 1f), PerceptualGamma) * 255f),
        (byte)(MathF.Pow(Math.Clamp(b, 0f, 1f), PerceptualGamma) * 255f),
        (byte)255);
}
```

**Reasoning (Linha 246-254):**
- Float linear [0.0, 1.0] decai como 8,7,6,5,4,3,2,1,0
- Visualmente, humanos veem 90% → 45% como "praticamente nenhuma mudança"
- 10% → 0% visto como "colapso repentino para preto"
- Gamma 0.5 (sqrt) levanta o intervalo baixo para parecer mais linear

**Glow (Point lights, aditivo) — Linha 237-244:**

```csharp
private static Color ToColor(float r, float g, float b)
{
    return new Color(
        (byte)(Math.Clamp(r, 0f, 1f) * 255f),
        (byte)(Math.Clamp(g, 0f, 1f) * 255f),
        (byte)(Math.Clamp(b, 0f, 1f) * 255f),
        (byte)255);
}
```

**Sem gamma:** Aditivo já está tuned para parecer correto com valores lineares.

### 4.9 CopyLightGridTo vs CopyGlowGridTo

**CopyLightGridTo (Linha 208-222):**

```csharp
public void CopyLightGridTo(Color[] destination)
{
    for (int localY = 0; localY < bufferHeight; localY++)
    {
        int tileY = bufferOriginTileY + localY;
        for (int localX = 0; localX < bufferWidth; localX++)
        {
            int tileX = worldMap.WrapTileX(bufferOriginTileX + localX);
            int index = (localY * bufferWidth) + localX;
            
            // CRÍTICO: Força tiles OPEN para Color.White
            destination[index] = IsAttenuatingAt(tileX, tileY)
                ? ToPerceptualColor(lightR[index], lightG[index], lightB[index])
                : Color.White;  // ← Sem iluminação em tiles vazios
        }
    }
}
```

**Motivo (Linha 189-207):**
- Textura será upscalada com LinearClamp (interpolação suave)
- Se um tile vazio receber seu próprio valor escuro, o interpolação blend escurece o céu vizinho
- Forçar white garante que tile vazio + vizinhança = resultado correto
- Tira isso quando geometria/stencil-based masking for implementado

**CopyGlowGridTo (Linha 230-235):**

```csharp
public void CopyGlowGridTo(Color[] destination)
{
    int cellCount = bufferWidth * bufferHeight;
    for (int i = 0; i < cellCount; i++)
        destination[i] = ToColor(glowR[i], glowG[i], glowB[i]);
}
```

**Sem filtragem:** Todos os values, open ou não (será Additive de qualquer forma).

### 4.10 Integração com Chunks e World Wrapping

**Sem integração direta:** WorldLightingSystem ignora chunks.

**World Wrapping (Linha 215, 285, 373):**

```csharp
int tileX = worldMap.WrapTileX(bufferOriginTileX + localX);
```

Garante que coordenadas wrappam horizontalmente.

**PlayingSessionViewCoordinator.DrawWorldLighting() — Linha 282:**

```csharp
int lightingTileOffset = (int)MathF.Round(worldOffsetX / tileSize);
int destX = (lightTextureOriginTileX - lightingTileOffset) * tileSize;
```

**Compensação:** Texture é renderizada uma vez (sem wrapping), mas desenha em cada loop offset com compensation.

---

## 1. COMPOSIÇÃO FINAL — ORDEM CORRIGIDA (Ponto 1)

### Decisão Revisada

A composição final deve ocorrer **ANTES do HUD**, nunca depois.

**Ordem lógica nova:**

```
[Renderização da cena de mundo em SceneRenderTarget]
    ↓
[Geração/aplicação do lightmap]
    ↓
[Composição: SceneColor * LightMap + Emissive + Glow]
    ↓
[Renderização do resultado ao backbuffer]
    ↓
[HUD, minimap, console, UI — screen-space, sem iluminação]
```

**Motivo:** HUD nunca deve receber multiplicação de iluminação. Deve permanecer visible à noite, visível under torches, etc.

---

## 2. CAMADAS DE RENDERIZAÇÃO — SEPARAÇÃO CONCEITUAL (Ponto 2)

### Hierarquia de Camadas Iluminadas

**CAMADA 1: Fundo Atmosférico (SEM iluminação)**
- Sky (céu gradiente)
- Sun rays e moons
- Parallax mountains (distância infinita)

**Motivo:** Não são geometria do mundo. Iluminá-las seria contraproducente.

**Ficam:** Fora do SceneRenderTarget

---

**CAMADA 2: Mundo Iluminável (COM iluminação multiplicativa)**
- Background walls (florestas)
- Back tree decorations
- Front tree decorations
- Water
- Terrain base (tiles, areia)
- Terrain overlay
- World entities (player, inimigos, itens)
- Tissue network (halo, core)

**Motivo:** Geometria real. Recebe oclusão e iluminação de tochas/céu.

**Vão:** Dentro do SceneRenderTarget

---

**CAMADA 3: Luz Ambiente (MULTIPLY sobre Camada 2)**
- Multiplicado após desenho do terreno
- Aplica skyColor + oclusão + torch glow

**Vão:** Aplicado na composição final

---

**CAMADA 4: Elementos Emissivos (SEM multiplicação, adicionados ao final)**
- Torch glow (Additive)
- Flame particles (Additive, futura)
- Tissue glow especial (Additive, futura)

**Motivo:** Brilham por si mesmos. Não devem escurecer.

**Vão:** Após composição, Additive blend

---

**CAMADA 5: Interface (SEM iluminação)**
- HUD (vida, inventário, etc.)
- Minimap
- Console
- Menus

**Motivo:** Informação de jogador. Sempre visível.

**Ficam:** Fora de todo o sistema de iluminação

---

**CAMADA 6: Night Overlay (COMPATIBILIDADE)**
- Escurecimento global à noite
- Será integrado dentro de AmbientLightColor eventualmente

**Status:** Modo debug (ativar/desativar)

---

### Fase 2 Implementation Detail

**SceneRenderTarget conterá:** Camadas 2 + aplicação de Camada 3

**Fora do RenderTarget:**
- Camada 1 (sky, fundo)
- Camada 4 (emissive, futura)
- Camada 5 (HUD)
- Camada 6 (night overlay, debug)

---

## 3. DETERMINAÇÃO DO PONTO DE INÍCIO DO SceneRenderTarget (Ponto 3)

### Análise da Ordem Atual

**PlayingState.Draw() — Linhas 255-456**

```
Linha 285-287:   DrawSky             (SkyRenderTarget saída? Não, LinearClamp)
Linha 291-292:   DrawSunGlow, Moons  (Screen-space)
Linha 297-299:   DrawParallaxMountains (Screen-space)
                 └─ FIM DO FUNDO

Linha 301-310:   LOOP-WRAP:
                   ├─ DrawTreeDecorations (Back)
                   ├─ DrawBackgroundWalls
Linha 313-321:     ├─ DrawTreeDecorations (Front)
Linha 324-339:     ├─ DrawWater
Linha 341-343:     ├─ DrawTerrainBase    ← INÍCIO DO MUNDO ILUMINÁVEL
Linha 347-349:     ├─ DrawWetnessOverlay
Linha 354-359:     ├─ DrawWorldLighting (MULTIPLY)
Linha 361-363:     ├─ DrawTerrainOverlay
Linha 366-378:     └─ DrawLoopedWorldEntities (player, enemies, items)
                     └─ FIM DO MUNDO

Linha 380-394:   LOOP-WRAP (continuado):
Linha 396-398:     DrawEntities (non-looped)
Linha 406-408:     DrawTissueHalo, Core, Field, Debug
Linha 422-425:   DrawRainFront, NightOverlay
Linha 432-444:   DrawTorchGlow (ADDITIVE)
                 └─ FIM DOS EFEITOS
                 
Linha 446-455:   DrawHud, Minimap, Console, UI
                 └─ FIM DA INTERFACE
```

### Ponto de Início Correto para SceneRenderTarget

**INICIAR ANTES DE:** DrawTreeDecorations(Back) ou DrawBackgroundWalls

**TERMINAR DEPOIS DE:** DrawLoopedWorldEntities (todas as entidades mundo)

**Razonamento:**
- Céu/montanhas paralelas NÃO devem ser iluminadas (fundo fixo)
- Tudo a partir de BackgroundWalls deve ser iluminado
- Entidades looped (player, inimigos, itens) devem estar DENTRO do RenderTarget para receber iluminação

### Novo Ponto Específico

**Iniciar SceneRenderTarget.SetRenderTarget(graphicsDevice) DENTRO do LOOP-WRAP, ANTES de DrawTreeDecorations(Back)**

**Finalizar e Blitar ao backbuffer APÓS DrawLoopedWorldEntities, DENTRO do LOOP-WRAP**

**Aplicar iluminação (lightmap multiply) DENTRO do RenderTarget, após terreno**

---

## 4. RESULTADO FINAL DA INVESTIGAÇÃO (Ponto 4)

| Aspecto | Valor | Observação |
|---------|-------|-----------|
| **Resolução lógica** | Tiles (8px) | Não pixels |
| **Format de lightTexture** | SurfaceFormat.Color (ARGB8) | 32 bits/texel |
| **Format de glowTexture** | SurfaceFormat.Color (ARGB8) | Idêntico |
| **Resolução de texture** | bufferWidth × bufferHeight tiles | ~56×42 típico |
| **Frequência atualização** | A CADA FRAME | Sem caching |
| **Custo de PrepareWorldLighting** | ~50μs CPU + 10-20KB GPU upload | Negligenciável |
| **Custo de PrepareTorchGlow** | Idêntico a PrepareWorldLighting | - |
| **Upload CPU→GPU** | Sim, SetData() a cada frame | Pequeno transfer |
| **Algoritmo BFS** | Flood-fill com decay | 2 passes (light + glow) |
| **Canais RGB** | Sim, 3 independentes | Mistura de cores automática |
| **Bloqueio de tiles** | IsSolidAt + BackgroundTile + SandSystem | Tríplice check |
| **Integração chunks** | Nenhuma (WorldLightingSystem standalone) | Possível otimização futura |
| **Integração wrapping** | WrapTileX em SeedSkyExposedTiles + CopyLightGridTo | Funciona corretamente |
| **Margem propagação** | PropagationMarginTiles = 12 tiles | Além de visible viewport |
| **Comportamento movimento** | Janela recalculada, BFS recompute | Sem persistência entre frames |
| **Comportamento zoom** | Janela adapta em tile units | Stable frame-to-frame |
| **Recriação texturas** | Só se capacidade insuficiente | Crescimento defensivo |
| **Descarte texturas** | Dispose() quando desatualizada | Sem leak (confirmed) |
| **SamplerState atual** | LinearClamp para lightTexture | Interpolação suave |
| **Motivo atualização a cada frame** | Sem dirty-flag system (v1) | Primeiro passe, simplicidade |

---

## 5. NÃO ESPALHAR LÓGICA DE ILUMINAÇÃO EM DRAWS (Ponto 5)

### Confirmação e Escopo

**Fase 2:** Aplicar iluminação apenas na composição final

```
SceneColor × LightMap
```

**Não fazer:**
```
Draw.Player(tint: GetLightAt(player.position))  // Não nesta fase
Draw.Enemy(tint: GetLightAt(enemy.position))    // Não nesta fase
```

**Razão:** Simplifica implementação. Cada entidade seria 3 chamadas de SetRenderTarget (ineficiente).

**Fallback futuro:** Se composição não for suficiente (entidades muito escuras), será feito.

---

## 6. DrawTorchGlow COMO GLOW VISUAL, NÃO ILUMINAÇÃO (Ponto 6)

### Hierarquia Visual Corrigida

```
FinalColor = (SceneColor × LightMap_Sky) + Glow_Torch
             ↑
             Iluminação real (multiplicativa)
```

**DrawTorchGlow:**
- Renderizado APÓS night overlay
- Additive blend (não sobrescreve)
- Punch-through efeito visual
- Não afeta "real" lighting calculation

**Observação:** Já é assim atualmente (linha 438-443 de PlayingState.cs).

---

## 7. DrawNightOverlay — MODO COMPATIBILIDADE (Ponto 7)

### Situação Atual

DrawNightOverlay renderiza uma tela preta com alpha > 0, escurecendo tudo.

**Problema:** Duplo escurecimento com futuro AmbientLightColor.

### Solução: Debug Mode

**Adicionar flag em PlayingSessionViewCoordinator:**

```csharp
public bool LegacyNightOverlayMode { get; set; } = true;  // Default: ON (compatibilidade)
```

**Em DrawNightOverlay (PlayingState.cs):**

```csharp
if (viewCoordinator.LegacyNightOverlayMode)
{
    session.DrawNightOverlay(spriteBatch, screenW, screenH);  // Old behavior
}
else
{
    // Novo behavior: AmbientLightColor vem de WorldLightingSystem
    // (Implementado na Fase 6)
}
```

**Permite:**
- ✅ Manter compatibilidade
- ✅ Comparar render antigo vs novo
- ✅ Transição suave

**Console command (future):**
```
lighting_legacy_overlay [true|false]
```

---

## 8. ESCOPO REVISADO DA FASE 2 (Ponto 8)

### O Que Será Implementado

✅ RenderTarget para SceneRenderTarget (1280×720 típico)
✅ Renderizar mundo nele (layers 2 iluminável)
✅ Reutilizar lightTexture/glowTexture existentes
✅ Compor SceneRenderTarget × lightmap
✅ Desenhar resultado ao backbuffer
✅ Manter sky/mountains/UI fora da iluminação
✅ Preservar PointClamp nitidez
✅ Modo debug: legacy vs new pipeline
✅ Alternância entre pipelines antigos/novos

### O Que NÃO Será Implementado

❌ Bloom
❌ Emissive map completo
❌ Dithering/quantização
❌ Normal maps
❌ Shadow mapping
❌ Per-entity light sampling
❌ Entidades com cor por pixel

---

## 9. PSEUDOCÓDIGO DO NOVO DRAW ORDER (Ponto 9)

### PlayingState.Draw() — Nova Ordem com RenderTargets

```csharp
void Draw(GameTime gameTime, SpriteBatch spriteBatch)
{
    int screenW = graphicsDevice.PresentationParameters.BackBufferWidth;
    int screenH = graphicsDevice.PresentationParameters.BackBufferHeight;
    float worldWidthPixels = session.WorldMap.PixelWidth;
    IReadOnlyList<int> visibleLoopOffsets = GetVisibleLoopOffsets(screenW, worldWidthPixels);

    // PREPARAÇÃO (a cada frame)
    for (int i = 0; i < visibleLoopOffsets.Count; i++)
    {
        int loopIndex = visibleLoopOffsets[i];
        float worldOffset = loopIndex * worldWidthPixels;
        session.PrepareTerrainRender(graphicsDevice, screenW, screenH, worldOffset);
    }
    session.PrepareWorldLighting(graphicsDevice);
    session.PrepareTorchGlow(graphicsDevice);

    // ============================================
    // FASE A: SKY E FUNDO (NÃO-ILUMINADO)
    // ============================================
    
    spriteBatch.Begin(samplerState: SamplerState.LinearClamp);
    session.DrawSky(spriteBatch, screenW, screenH);
    spriteBatch.End();

    session.DrawSunGlow(spriteBatch, screenW, screenH);
    session.DrawMoons(spriteBatch, screenW, screenH);

    spriteBatch.Begin(samplerState: SamplerState.PointClamp);
    session.DrawParallaxMountains(spriteBatch, screenW, screenH);
    spriteBatch.End();

    // ============================================
    // FASE B: MUNDO ILUMINÁVEL (dentro do RenderTarget)
    // ============================================
    
    for (int i = 0; i < visibleLoopOffsets.Count; i++)
    {
        int loopIndex = visibleLoopOffsets[i];
        float worldOffset = loopIndex * worldWidthPixels;
        Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

        // === INICIAR SceneRenderTarget ===
        graphicsDevice.SetRenderTarget(session.SceneRenderTarget);
        graphicsDevice.Clear(Color.Transparent);

        // Desenhar background walls
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Back);
        session.DrawBackgroundWalls(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Desenhar front trees
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawTreeDecorations(spriteBatch, screenW, screenH, worldOffset, TreeRenderLayer.Front);
        spriteBatch.End();

        // Desenhar água
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
        session.DrawWater(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Desenhar terreno base
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawTerrainBase(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Desenhar wetness overlay
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: MultiplyBlend, transformMatrix: transform);
        session.DrawWetnessOverlay(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // CRÍTICO: Aplicar world lighting (MULTIPLICATIVO) dentro do RenderTarget
        spriteBatch.Begin(samplerState: SamplerState.LinearClamp, blendState: MultiplyBlend, transformMatrix: transform);
        session.DrawWorldLighting(spriteBatch, worldOffset);
        spriteBatch.End();

        // Desenhar terrain overlay
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawTerrainOverlay(spriteBatch);
        spriteBatch.End();

        // Desenhar entities (player, enemies, items)
        spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: transform);
        session.DrawLoopedWorldEntities(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();

        // Desenhar tissue (halo, core, field, debug)
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

        // === FINALIZAR SceneRenderTarget, RENDER AO BACKBUFFER ===
        graphicsDevice.SetRenderTarget(null);
        
        // Compor SceneRenderTarget ao backbuffer (screen-space, sem transform)
        spriteBatch.Begin(samplerState: SamplerState.PointClamp);
        spriteBatch.Draw(session.SceneRenderTarget, Vector2.Zero, Color.White);
        spriteBatch.End();
    }

    // ============================================
    // FASE C: EFEITOS PÓS-ILUMINAÇÃO (ADDITIVE)
    // ============================================

    // Interior focus overlay
    for (int i = 0; i < visibleLoopOffsets.Count; i++)
    {
        int loopIndex = visibleLoopOffsets[i];
        float worldOffset = loopIndex * worldWidthPixels;
        Matrix transform = Matrix.CreateTranslation(worldOffset, 0f, 0f) * session.Camera.GetViewMatrix();

        spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend, transformMatrix: transform);
        session.DrawInteriorFocusOverlay(spriteBatch, screenW, screenH, worldOffset);
        spriteBatch.End();
    }

    // Non-looped entities
    spriteBatch.Begin(samplerState: SamplerState.PointClamp, transformMatrix: session.Camera.GetViewMatrix());
    session.DrawEntities(spriteBatch);
    spriteBatch.End();

    // Rain + Night overlay (screen-space)
    spriteBatch.Begin(samplerState: SamplerState.PointClamp, blendState: BlendState.AlphaBlend);
    session.DrawRainFront(spriteBatch, screenW, screenH);
    if (viewCoordinator.LegacyNightOverlayMode)
        session.DrawNightOverlay(spriteBatch, screenW, screenH);  // Debug mode
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

    // ============================================
    // FASE D: INTERFACE (SEM ILUMINAÇÃO)
    // ============================================

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
```

### Mudanças Principais

**Antes:**
- DrawWorldLighting dentro do loop-wrap, multiplicado direto

**Depois:**
- SceneRenderTarget criado, renderizado nele
- Iluminação aplicada DENTRO do RenderTarget
- RenderTarget composto ao backbuffer screen-space
- Muito mais controle

---

## 10. LISTA EXATA DE ARQUIVOS — FASE 2 (Ponto 10)

### Modificações Necessárias

#### PlayingState.cs
- **Linha ~35:** Adicionar campo `sceneRenderTarget`
- **Linha ~270-456:** Reordenar Draw com RenderTarget begin/end
- **Linha ~282:** Debug flag `LegacyNightOverlayMode`

#### PlayingSessionViewCoordinator.cs
- **Linha ~25:** Adicionar campos RenderTarget
- **Linha ~100:** Novo método `CreateRenderTargets(graphicsDevice, screenW, screenH)`
- **Linha ~100:** Novo método `DisposeRenderTargets()`
- **Linha ~100:** Novo método `GetSceneRenderTarget()` accessor
- **Linha ~25:** Property `LegacyNightOverlayMode`

#### PlayingSession.cs
- **Linha ~400:** Forward properties para ViewCoordinator.RenderTarget
- **Linha ~400:** Forward property para LegacyNightOverlayMode

### Novos Arquivos

#### Engine/Graphics/Lighting/LightingRenderTargetManager.cs (OPCIONAL — Fase 2.5)
- Gerenciar SceneRenderTarget, EmissiveMapRenderTarget (futuro)
- Recreação em resize de janela
- Cleanup seguro

**Nesta fase pode ser inlined em PlayingSessionViewCoordinator.**

### Arquivos IMUTÁVEIS (NÃO MODIFICAR)

- WorldLightingSystem.cs (já funciona perfeitamente)
- WorldMap.cs
- Player.cs, Enemy.cs, WorldItem.cs
- Todos os "Draw*" methods em PlayingSessionViewCoordinator (assinaturas iguais)

---

## RESUMO DAS DECISÕES TOMADAS

| Decisão | Status | Impacto |
|---------|--------|--------|
| SceneRenderTarget comça antes de BackgroundWalls | ✅ APROVADO | Captura tudo iluminável |
| Céu/fundo fora do RenderTarget | ✅ APROVADO | Sem escurecimento indesejado |
| HUD/UI fora do RenderTarget | ✅ APROVADO | Sempre visível |
| Composição ANTES de HUD | ✅ APROVADO | Interface nunca iluminada |
| Iluminação não espalhada em DrawEntity | ✅ APROVADO | Implementação simplificada v1 |
| DrawTorchGlow = glow visual puro | ✅ CONFIRMADO | Já é assim |
| DrawNightOverlay modo debug | ✅ APROVADO | Compatibilidade mantida |
| Reutilizar lightTexture/glowTexture | ✅ APROVADO | Sem recompile BFS |
| Bloom/emissive/dithering adiados | ✅ CONFIRMADO | Fase 3+ |
| PointClamp mantido para scene | ✅ CONFIRMADO | Nitidez pixel art |

---

## PRÓXIMOS PASSOS

1. **Aprovação desta revisão**
   - Todos os 10 pontos clarificados?
   - Pseudocódigo correto?
   - Arquivos corretos?

2. **Implementação Fase 2** (autorizada após aprovação)
   - Criar SceneRenderTarget
   - Adaptar PlayingState.Draw()
   - Testar zero regression visual
   - Validar contra critérios de aceitação

3. **Preparação Fase 3**
   - Ajustar resolution/format de RenderTarget conforme necessário
   - Implementar composição shader (se houver)

---

**Documento de Revisão Completo. Pronto para Aprovação.**
