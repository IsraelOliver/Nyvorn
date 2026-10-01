---
verificado_em: 2026-09-14
commit: ec39358c71c67fd05a05063c5f52aa148fb35583
---

# Mapa de sistemas — Nyvorn

Auditoria: [[01-estado]] · [[02-iluminacao-historico]] · [[03-docs-divergentes]] · [[04-perguntas]]
Caminhos relativos a `Nyvorn/Source/` salvo indicação. Branch `physics`, com working tree sujo (V8 não commitada).

| Sistema | Arquivos principais | O que faz |
|---|---|---|
| Entrada e estados | `../Program.cs`, `../Game1.cs`, `Game/StateMachine.cs`, `Game/States/*State.cs` | Pilha de telas: mundos, jogador, loading, jogo, pausa, morte. |
| Sessão de jogo | `Game/States/PlayingState.cs`, `PlayingSession.cs`, `PlayingSessionFactory.cs`, `PlayingSession/*` | Monta a sessão e orquestra o frame: input, entidades, combate, ticks, render. |
| Console debug | `Game/States/PlayingState.cs` | Comandos `/…` de debug (tempo, ticks, água, tecido, itens, spawn). |
| Mundo (tiles) | `Gameplay/World/WorldMap.cs`, `Tile.cs`, `WorldChunkCoord.cs` | Grade de tiles com wrap horizontal, cache de chunks e autotile. |
| Geração procedural | `Gameplay/World/Generation/WorldGenerator.cs`, `Passes/*`, `Biomes/*` | 17 passes por seed: terreno, cavernas, deserto, água, minério, árvores. |
| Persistência | `Gameplay/World/Persistence/PlanetSaveService.cs`, `PlayerSaveService.cs` | Save de mundo e de jogador, em arquivos separados. |
| Ticks do mundo | `Gameplay/World/Simulation/WorldTickSystem.cs`, `PlayingSession/PlayingSessionWorldTickCoordinator.cs` | Ticks rápido (areia/água), médio (grama) e lento (cogumelos). |
| Ambiente e clima | `Simulation/WorldEnvironmentSystem.cs`, `WorldDayNightCycle.cs`, `WorldEvent*.cs`, `TileWetnessField.cs` | Dia/noite, duas luas, chuva, eclipse e umidade. |
| Areia | `Engine/Physics/Sand/SandSystem.cs` | Areia por pixel com queda. |
| Líquidos | `Engine/Physics/Liquids/*`, `Passes/HydrologyPass.cs` | Água em células, com hidrologia na geração. |
| Colisão/física | `Engine/Physics/Kinematic*.cs`, `Gameplay/World/Collision/*` | Corpo cinemático AABB contra tiles e plataformas. |
| Player | `Gameplay/Entities/Player/*` | Movimento, animação em partes, ataque, natação. |
| Inimigos | `Gameplay/Entities/Enemies/*`, `AI/*` | IA perseguidora, spawn noturno na superfície, respawn. |
| Combate | `Gameplay/Combat/*` | Hitboxes, armas e ferramentas, dano, números de dano. |
| Itens | `Gameplay/Items/*`, `Data/Serialization/JsonLoader.cs`, `../Content/Data/items.json` | Definições em JSON, inventário, hotbar, itens no chão. |
| Crafting | `Gameplay/Crafting/Recipe*.cs`, `CraftTier.cs`, `../Content/Data/recipes.json` | Receitas por tier (bancada, fornalha). |
| Objetos e móveis | `Gameplay/World/Objects/*`, `Gameplay/Crafting/*Instance.cs` + `*RuntimeSystem.cs` | Porta, tocha, cadeira, mesa, plataforma, bancada e fornalha: colocáveis, mineráveis e salvos. |
| Árvores e decorações | `Gameplay/World/Decorations/*` | Árvores montadas por partes e cogumelos de superfície. |
| Tecido (Tissue) | `Gameplay/World/Tissue/*`, `Passes/TissuePass.cs`, `PlayingSession/PlayingSessionTissueSystem.cs` | Rede/campo no subsolo com revelação, pulsos, mutação e propagação. |
| Poderes | `Gameplay/Powers/*`, `Gameplay/UI/PowerHUD.cs` | Poder de revelar o tecido, com carga. |
| Interação com blocos | `Gameplay/Interaction/*`, `PlayingSession/PlayingSessionBlockInteractionSystem.cs` | Alvo de interação; quebrar e colocar blocos e areia. |
| Interiores | `Gameplay/World/Interiors/InteriorFocusSystem.cs` | Foco de câmera e overlay em espaço fechado. |
| UI/HUD | `Gameplay/UI/*` | HUD, minimapa, hub do jogador, barras de vida, preview de tile. |
| Céu e parallax | `Gameplay/UI/ElyraSkyRenderer.cs`, `Engine/Graphics/SubterraneanParallaxRenderer.cs`, `../Content/effects/SunRays.fx`, `MoonPhase.fx` | Céu, sol, luas; parallax por camada com crossfade. |
| Partículas | `Gameplay/World/Particles/BlockParticleSystem.cs` | Partículas de quebra de bloco. |
| Câmera e input | `Engine/Graphics/Camera2D.cs`, `Engine/Input/*` | Câmera com zoom; teclado, mouse, clipboard. |
| Iluminação V7 (padrão) | `Engine/Graphics/LightingV7/*`, `../Content/effects/PixelComposite.fx` | Lightmap por tile (céu + bloco), luz direta, sol direcional, halos. |
| Iluminação V6 ("Legacy") | `Engine/Graphics/LightingPipeline/V6*.cs` | Lightmap anterior; alternável com F9. |
| Iluminação V8 (não commitada) | `Engine/Graphics/LightingV8/*`, `Game/States/PlayingState.V8*.cs`, `../Content/effects/V8*.fx` | Luz direta com stencil + ambiente; ativada por `--lighting-v8`. |
| Diagnóstico de iluminação (era V3) | `LightingPipeline/RenderedFrameProfiler.cs`, `FrameProfiler.cs`, `SunVisibility*.cs`, `LightingTestRunner.cs` | Profilers, ablações e testes da V3. |
| Projetos auxiliares (raiz) | `LightingV4CapabilityProbe/`, `V6Validator/`, `LightingV7Validator/`, `TestConsole/` | Harnesses de teste fora do jogo. |
