# Lighting V2 - FASE 0: Auditoria e Desenho Arquitetural

**Data:** 2026-07-30  
**Status:** Auditoria Completa - Pronto para FASE 1  
**Build Result:** ✓ Sem warnings  

---

## 1. RESUMO DA AUDITORIA

Build do projeto passou sem warnings ou erros. Diretórios de Lighting V2 já existem em:
- `Nyvorn/Source/Engine/Graphics/LightingV2/`
- `Nyvorn/Source/Engine/Graphics/LightingV2/Local/`

### Nenhuma lógica legada foi reutilizada
Apenas consultas a APIs estáveis e compreensão da integração foram feitas.

---

## 2. SISTEMA LEGADO IDENTIFICADO

### WorldLightingSystem (Gameplay/World/Simulation/)
- **Responsabilidade:** BFS flood-fill com múltiplos canais RGB
- **Entrada:** 
  - `SetPointLights(IEnumerable<Vector2>)` — posições de tochas em espaço mundo
  - `Update(dt, cameraPosition, cameraZoom, screenWidth, screenHeight, Color skyColor)`
- **Saída:**
  - `GetLightAt(tileX, tileY) → Color` — luz combinada (céu + tochas) por tile
  - `CopyLightGridTo(Color[])` — buffer de iluminação para renderização
  - `CopyGlowGridTo(Color[])` — glow de tochas apenas (sem céu), para blend aditivo
  - `WindowOriginTileX/Y, WindowWidth/Height` — bounds da janela computada
- **Detalhes:**
  - SandSystem é consultado opcionalmente para oclusão física
  - Wrapping horizontal suportado (`worldMap.WrapTileX()`)
  - Margem de propagação de 12 tiles além da janela visível
  - Gamma perceptual para a pass combinada (não para glow)

**Lighting V2 NÃO reutilizará:**
- Algoritmo BFS em si (nova implementação)
- Buffers RGB legados
- Decay rates antigas
- Conversão gamma antiga

---

## 3. CICLO DIA/NOITE

### WorldDayNightCycle (Gameplay/World/Simulation/)
- **Duração padrão:** 1800 segundos (30 minutos por ciclo dia-noite completo)
- **Tempo inicial:** 0.25 (6:00 AM)
- **Normalizado:** `[0.0, 1.0)` onde 0 = 00:00, 0.5 = 12:00, 1.0 = 24:00
- **Fases definidas:**
  - 00:00–04:00 — DeepNight
  - 04:00–05:00 — PreDawn
  - 05:00–06:30 — Sunrise (aurora)
  - 06:30–12:00 — Morning
  - 12:00–14:30 — Noon
  - 14:30–17:30 — Afternoon
  - 17:30–19:30 — Sunset (entardecer)
  - 19:30–24:00 — Night

**APIs:**
- `TimeOfDay01` — tempo normalizado `[0, 1)`
- `CycleIndex` — número de ciclos completos desde início
- `CurrentPhase` — enum WorldTimePhase
- `NightStrength` — `[0, 1]` suavizado no amanhecer/entardecer
- `CreateSnapshot()` → WorldTimeSnapshot — captura completa do tempo

**Luz ambiente (sky color):**
- Dia claro: `Color(102, 190, 255)` azul
- Noite escura: `Color(16, 38, 88)` azul escuro
- Interpolado via `Color.Lerp(dayColor, nightColor, nightStrength)`

---

## 4. AMBIENTE E CORES HORARIAS

### WorldEnvironmentSystem (Gameplay/World/Simulation/)
- **Keyframes de cor** — 9 pontos ao longo de 24 horas com interpolação suave:
  - **00:00** (DeepNight)
  - **04:00** (PreDawn)
  - **05:00** (Sunrise — cores quentes)
  - **06:30** (Morning — céu azul claro)
  - **12:00** (Noon — pico de claridade)
  - **14:30** (Afternoon — céu claro)
  - **17:30** (Sunset — cores quentes novamente)
  - **19:30** (Night — céu escuro)
  - **24:00** (wraps para 00:00)

- **Cada keyframe contém:**
  - `TopColor` — topo do gradiente do céu
  - `HorizonColor` — horizonte
  - `SunColor` — cor da luz solar
  - `AmbientLight` — luz ambiente geral

**API:**
- `Update(dt, timeScale, isPaused, WorldTimeSnapshot time)` — atualiza ambiente
- `SkyState` — estado computado do céu com cores interpoladas

---

## 5. RENDERIZAÇÃO DO CÉU E POSIÇÃO DO SOL

### ElyraSkyRenderer (Gameplay/UI/)
- **Draw phases:**
  1. Gradiente de fundo
  2. Estrelas (130 no total)
  3. Bandas de nuvem
  4. Oclusão de eclipse
  5. Névoa
  6. Chuva (camadas traseiras)
  7. **DrawSunGlow** — chamado separadamente
  8. DrawMoons
  9. **DrawParallaxMountains** — ocluem sol/luas
  10. Chuva (camadas dianteiras)

- **Posição solar:**
  - `GetArcPosition(screenWidth, screenHeight, skyState.SunProgress)` → `Vector2`
  - `SunProgress` é um valor `[0, 1]` que descreve a posição ao longo do arco do dia

- **Sun glow shader:** `SunRays.fx`
  - Parâmetro: `SunPosition` — posição em pixels de tela
  - Parâmetro: `SunOpacity` — transparência do sol
  - Parâmetro: `SunColor` — cor da irradiação

**Lighting V2 deve consumir:**
- Posição do sol em espaço mundo (não apenas tela)
- Cor solar real (não hardcoded)
- Direção da luz solar (ângulo em radianos a partir da horizontal)
- Intensidade de luz solar

---

## 6. FONTES DE LUZ LOCAIS

### TorchRuntimeSystem (Gameplay/Crafting/)
- **API central:**
  ```csharp
  public IEnumerable<Vector2> GetLightSourcePositions()
  ```
  Retorna posições mundo de todas as tochas ativas.

- **Dados da tocha:**
  - Posição: pixel-perfect no mundo
  - Tipo de montagem: chão, parede esquerda, parede direita, background
  - Animação de chama: 7 frames, 0.12s cada, determinística por posição (não salva)
  - Flicker: derivado de `visualTimeSeconds + posição`

- **Renderização:**
  - Sprites separados para poste e chama
  - Cor branca (flame texture contém cor)
  - Z-order: integra-se ao terrain normal

**Lighting V2 deve consumir:**
- Lista de `Vector2` de posições de tochas
- Cor: hardcoded como `Color(255, 150, 60)` (laranja quente) por enquanto
- Alcance/intensidade: 0.45 de pico para glow, decay rates conhecidas

---

## 7. CONSULTAS DE TILES E MUNDO

### WorldMap APIs (consultas seguras)
- `IsSolidAt(tileX, tileY) → bool` — tile foreground é sólido
- `GetForegroundTile(tileX, tileY) → TileType` — tipo específico
- `GetBackgroundTile(tileX, tileY) → TileType` — parede de fundo
- `IsBackgroundSolidAt(tileX, tileY) → bool` — parede é sólida
- `HasOpenSkyAbove(tileX, tileY) → bool` — linha clara até y=0
- `WrapTileX(tileX) → int` — wrapping horizontal de mundo
- `TileSize → int` — sempre 16 pixels (fixo no jogo)
- `Width, Height → int` — dimensões do mundo em tiles
- `InBounds(tileX, tileY) → bool` — dentro dos limites válidos

### SandSystem APIs
- `HasSandAt(centerPixelX, centerPixelY) → bool` — areia solta ocupa este pixel

### Wrapping Horizontal
- Mundo é toroidal na horizontal
- `WrapTileX()` converte coordenada bruta para espaço válido `[0, Width)`
- Câmera passa posição desenvolvida (sem wrapping)
- Rendering deseja múltiplas cópias wrappadas

---

## 8. EVENTOS E HOOKS DE MUDANÇA

### Listeners de quebra de tiles (IForegroundTileBreakListener)
Implementado por:
- TorchRuntimeSystem
- DoorRuntimeSystem
- WorkbenchRuntimeSystem, FurnaceRuntimeSystem, TableRuntimeSystem, etc.

**Método:**
```csharp
void OnForegroundTileBroken(ForegroundTileBrokenContext context)
```

**Contexto fornecido:**
- `Tile` — Point do tile (X, Y) que foi quebrado
- `WorldItemRuntimeSystem` — para drops de items
- (e acessos a outros sistemas via contexto conforme necessário)

**Lighting V2 deve:**
- Registrar-se como IForegroundTileBreakListener
- Invalidar cache de luz quando foreground muda
- Invalidar cache de luz quando background muda (evento similar)

---

## 9. CICLO DE RENDERIZAÇÃO E FASE DE COMPOSIÇÃO

### PlayingState (Renderização)
Usa `PlayingSessionViewCoordinator` para orquestrar todas as fases.

**Duas pipelines:**
1. **Legacy (UseNewLightingPipeline=false):**
   - Renderiza direto no backbuffer
   - DrawNightOverlay aplica escurecimento full-screen
   - WorldLighting desenha texture multiplicada

2. **New (UseNewLightingPipeline=true):**
   - Renderiza em RenderTarget2D
   - 7 fases em ordem: Prepare → Atmosphere → World → Lighting → Composite → Overlays → HUD
   - Night overlay é opcional (LegacyNightOverlayMode)

**Resolução:**
- `graphicsDevice.PresentationParameters.BackBufferWidth/Height`
- Zoom da câmera: 2.0x padrão, 3.0x em interiores

---

## 10. RECURSOS GRÁFICOS LEGADOS A NÃO REUTILIZAR

### RenderTargets legados
- `lightTexture` — texture RGB com iluminação combinada
- `glowTexture` — texture RGB com glow de tochas (aditivo)

**Lighting V2 criará seus próprios:**
- AmbientSkyLightMap
- DirectSunlightMap
- LocalEnvironmentLightMap
- SurfaceHitLightMap (opcional FASE 5)
- VisibleSunShaftMap (FASE 7)
- CombinedLightMap (composição final)

### Shaders legados
- Nenhum shader de iluminação é mencionado no sistema legado
- SunRays.fx — é apenas visual (glow de sol no céu), não iluminação física

---

## 11. CICLO DO LIFECYCLE E DESCARTE

### PlayingSession
- Criada em PlayingSessionFactory
- Destruída quando PlayingState.OnExit() é chamado
- Deve informar Lighting V2 de destruição para Dispose correto

**PlayingSessionViewCoordinator recursos:**
Métodos de dispose já existem:
- `DisposeSceneRenderTarget()`
- `DisposeLightingMaskRenderTarget()`
- `DisposeDirectionalSunlightMap()`
- `DisposeForegroundBlockageMap()`
- `DisposePenumbraMap()`

Lighting V2 usará padrão similar.

---

## 12. DEPENDÊNCIAS A EVITAR

**Lighting V2 NÃO deve:**
- Consultar `PlayingState` diretamente
- Chamar métodos de rendering do `PlayingSessionViewCoordinator` (apenas será chamado por ele)
- Depender de `WorldLightingSystem` interno (é legacy)
- Chamar `GetAmbientTintAt` ou similar
- Consultar `player.TintColor` ou tints de entidades
- Usar `DrawNightOverlay` como base
- Depender de flags antigas como `UseLegacyTorchGlow`
- Consultar `SkyColor` do jogo antigo (será recalculado)

**Lighting V2 PODE consultar:**
- WorldMap (queries de tiles)
- WorldDayNightCycle (tempo, fases)
- WorldEnvironmentSystem (cores, ambiente)
- TorchRuntimeSystem (posições de tochas)
- SandSystem (oclusão de areia)
- Camera2D (posição, zoom)
- GraphicsDevice (criar texturas/renderTargets)
- InputService (debug hotkeys)

---

## 13. ARQUITETURA PROPOSTA PARA LIGHTING V2

### Estrutura de Diretórios
```
Nyvorn/Source/Engine/Graphics/LightingV2/
├── LightingV2System.cs              # Orquestrador
├── LightingV2Renderer.cs            # Renderização
├── LightingV2Resources.cs           # Recursos GPU
├── LightingV2Settings.cs            # Config/tuning
├── LightingV2DebugState.cs          # Debug/visualização
│
├── World/
│   ├── LightingCellClassifier.cs    # Classificação de células
│   ├── LightingCellType.cs          # Enum
│   ├── LightingMaterialProperties.cs # Propriedades por material
│   └── LightingActiveRegion.cs      # Região de cálculo
│
├── Natural/
│   ├── AmbientSkyLightSystem.cs     # Luz ambiente do céu
│   ├── DirectSunlightSystem.cs      # Luz solar direta
│   ├── SunlightSurfaceHitSystem.cs  # Superfícies atingidas (FASE 5)
│   └── SunlightShaftSystem.cs       # Feixes visuais (FASE 6-7)
│
├── Local/
│   ├── LocalLightSystem.cs          # Sistema de luzes locais
│   ├── LocalLightSource.cs          # Fonte de luz (genérica)
│   ├── LocalLightRegistry.cs        # Registro de fontes
│   └── TorchLightController.cs      # Controller de tochas
│
└── Composition/
    ├── LightingCompositeRenderer.cs # Composição final
    ├── LightingMaskRenderer.cs      # Máscara de aplicação
    └── EmissiveRenderer.cs          # Renderização de emissivos
```

### Dependências e Responsabilidades

**LightingV2System:**
- Coordena cálculos de luz por frame
- Não renderiza
- Não contém lógica de gameplay
- Não consulta PlayingState

**LightingV2Renderer:**
- Desenha mapas em RenderTargets
- Realiza composição final
- Não calcula propagação
- Chama LightingV2System para obter dados

**LightingV2Resources:**
- Único proprietário de RenderTarget2D e Texture2D
- Aloca/redimensiona/descarta
- Nunca aloca por frame
- Implementa IDisposable

**LightingCellClassifier:**
- Classifica cada célula de tile (OpenSky, BackgroundWall, ForegroundSolid, etc.)
- Fornece propriedades de bloqueio/transmissão
- Não renderiza

**AmbientSkyLightSystem / DirectSunlightSystem:**
- Cada um computa seu próprio mapa
- Usa LightingActiveRegion para limites
- Suporta dirty-flagging para otimização

**LocalLightSystem:**
- Consome lista de posições de tochas
- Calcula iluminação local por fonte
- Suporta oclusão e transmissão

**LightingCompositeRenderer:**
- Combina todos os mapas
- Aplica ao scene RenderTarget
- Respeita masks e emissivos

---

## 14. MODELO DE CÉLULAS

### Classificação Inicial (FASE 1)
```
LightingCellType {
  OpenSky,            // Sem foreground, sem background = céu aberto
  OpenInterior,       // Sem foreground, com background = interior
  BackgroundWall,     // Background sólido = parede
  ForegroundSolid,    // Foreground sólido = bloco opaco
  PartialTransmitter  // Vidro, água, folha, etc.
}
```

### Propriedades por Material (personalizáveis por FASE 10)
- `BlocksAmbientLight: bool` — bloqueia luz ambiente
- `BlocksDirectSunlight: bool` — bloqueia luz solar
- `AmbientTransmission: [0, 1]` — quanto de luz ambiente passa
- `DirectTransmission: [0, 1]` — quanto de luz solar passa
- `LocalTransmission: [0, 1]` — quanto de luz local passa
- `ReceivesLight: bool` — recebe iluminação
- `EmissiveStrength: [0, 1]` — auto-emissão
- `BounceFactor: [0, 1]` — reflexão (FASE 11)
- `BounceTint: Color` (opcional) — cor da reflexão

---

## 15. MAPAS DE ILUMINAÇÃO

### Mapas por Fase

**FASE 1 (Fundação):** Nenhum mapa, apenas estrutura

**FASE 2 (Composição Neutra):**
- SceneRenderTarget — cena com luz branca (Color.White)

**FASE 3 (Ambient):**
- AmbientSkyLightMap — RGB, luz ambiente por célula

**FASE 4 (Direct Sunlight):**
- DirectSunlightMap — RGB, luz solar direta por célula

**FASE 5 (Surface Hit):**
- SurfaceHitLightMap — RGB, iluminação de faces

**FASE 6 (Penumbra):**
- Integrado ao DirectSunlightMap com suavização

**FASE 7 (Visible Shafts):**
- VisibleSunShaftMap — alpha channel, feixes visuais

**FASE 8 (Local Lights):**
- LocalEnvironmentLightMap — RGB, iluminação de tochas e locais

**FASE 9+ (Composição):**
- CombinedLightMap — RGB, composição final de todos os mapas
- Aplicado multiplicativamente ao WorldSceneRenderTarget

---

## 16. MÉTRICAS E PERFORMANCE (BASELINE)

**Build Time:**
- Nenhuma alteração ao sistema legado
- Build: 2.66s (clean)
- Warnings: 0
- Errors: 0

**Runtime (Legacy Pipeline):**
- WorldLightingSystem.Update por frame: O(visibleCells)
- PrepareWorldLighting: O(visibleCells)
- DrawWorldLighting: 1 draw call + stretch/filter GPU

**Lighting V2 (estimado para FASE 2):**
- Estrutura inicial: negligenciável
- Não há computação real ainda

---

## 17. TESTES MANUAIS INICIAIS (PÓS FASE 1)

Antes de implementar iluminação:
- [ ] Build sem warnings
- [ ] PlayingState inicia e encerra sem erros
- [ ] Sessão de jogo abre/fecha
- [ ] WorldMap pode ser consultado normalmente
- [ ] Câmera segue player
- [ ] Resize não causa crash
- [ ] Zoom funciona
- [ ] Wrapping horizontal funciona
- [ ] Tochas são renderizadas visualmente

---

## 18. CRITÉRIOS DE ACEITE PARA FASE 0

✓ **Concluído:**
1. Build passa sem warnings
2. Sistema legado identificado e não modificado
3. APIs estáveis do jogo documentadas
4. Nenhuma lógica legada reutilizada
5. Ciclo do sol e horário documentados
6. Cores ambientes e keyframes documentados
7. Fontes de luz (tochas) identificadas
8. Eventos de mudança de tiles documentados
9. Wrapping horizontal entendido
10. Lifecycle e dispose documentados
11. Dependências a evitar documentadas
12. Arquitetura proposta desenhada
13. Modelo de células definido
14. Mapas de iluminação por fase listados
15. Este documento de auditoria completo

✓ **Pronto para FASE 1**

---

## PRÓXIMOS PASSOS

1. **FASE 1 — Fundação Isolada:**
   - Criar arquivos de estrutura V2
   - Implementar `LightingV2System`, `LightingV2Renderer`, `LightingV2Resources`
   - Criar `LightingPipelineMode` enum (Legacy/V2)
   - Integração com `PlayingSessionViewCoordinator`
   - Lifecycle completo (init, ensure, resize, dispose)
   - Nenhuma alteração visual ainda

2. **Entrega:**
   - Arquivos criados
   - Arquivos modificados (PlayingSession*)
   - Algoritmos de base
   - Resultado do build
   - Warnings (esperado: 0)
   - Testes manuais executados

---

**Assinado:** Fase 0 Audit Complete  
**Data:** 2026-07-30
