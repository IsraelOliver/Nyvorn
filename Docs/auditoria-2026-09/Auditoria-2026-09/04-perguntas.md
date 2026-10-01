---
verificado_em: 2026-09-14
commit: ec39358c71c67fd05a05063c5f52aa148fb35583
---

# Perguntas para o desenvolvedor

Relacionado: [[00-system-map]] · [[01-estado]] · [[02-iluminacao-historico]] · [[03-docs-divergentes]]

Só perguntas que o código e o git não respondem.

## Documentação e repositório

1. `Docs/Auditoria` está vazia. Era pra comparar com outra pasta? Os documentos que existiam lá ficaram fora do repo? (em [[03-docs-divergentes]] usei `Docs/Arquitetura` + `ConsoleCommands.md` + `LIGHTING_BASELINE_2026-08-11.md`)
   
   R. Na verdade era para criar a auditoria ali dentro, mas tudo bem!
1. O `*.md` no `.gitignore` (reforçado em `b2f6c83`, "remove .md files") é intencional? Hoje nenhuma doc do vault é versionada, incluindo as de V7/V8.

	 R. nA VERDADE VOU COMEÇAR A AUDITAR TUDO HOJE. TUDO O QUE TINHA ANTES de .md so vai entrar com minha autorização depois de uma revisão.
2. `hotmart.html`, `html.html`, `nyvorn_v6_diagnostic.txt` e `v6_perf_log.txt` na raiz fazem parte do projeto?

	R. hotmart.html e html.html, não fazem parte
2. `main` está parada em `28df7ae` (V6, 2026-08-11), e `physics` está 27 commits à frente, com trabalho que não é de física (parallax, V7). Existe plano de integração? Por que o nome `physics`?

	 R. por enquanto ainda não tenho um plano, mas tive que fazer essa implementação de urgencia porque ela esta ligada diretamente a iluminação das cavernas.

## Iluminação

5. `WorldLightingSystem` (julho) conta como "V1"? Os commits nunca usam esse nome; a numeração em [[02-iluminacao-historico]] é inferida.

	R. Sim. A V1 entregou uma iluminação no estilo Terraria — funcionava, mas não era o resultado visual que eu queria. Motivo do abandono: conceito visual.
6. Por que a V2 foi abandonada um dia depois de criada (`539f2d2` → `7194ede`)?

	R. A implementação mexeu em coisas desnecessárias do projeto e quase destruiu a base. Abandonada por dano colateral, não por resultado ruim de iluminação. Motivo: risco de engenharia.
7. O que foram V4.0 a V4.2A? O README do probe cita `LIGHTING_V4_2B_B_TEST_MANIFEST.md`, que não existe no disco. A V4 chegou a ter código de jogo?

	R. Sim
8. O abandono da V3 foi por performance (build de SunVisibility em 17 ms × meta de 3 ms, travamentos a ~2 FPS)? O commit que a removeu (`cc0f24d`) não diz.

	R. Sim. V3 e V4 travaram por FPS — a técnica escolhida não escalava. Motivo: performance.
9. Por que a V5 foi descartada em favor da V6?

	R. A V5 foi a primeira a dar um resultado visual, mas a ideia por trás era falha. Não foi descartada por completo: as ideias que funcionaram foram reaproveitadas para construir a V6. Motivo: fundação conceitual errada, com reaproveitamento parcial.
10. O que motivou a V7 no lugar da V6? A V6 ("Legacy", F9) deve continuar no código?

	R. A V6 foi a primeira a chegar perto do visual que eu queria, mas o código foi ficando torto e acumulando bug em cima de bug. Reiniciei na V7 com uma ideia melhor. Motivo: apodrecimento de código, não resultado.

		Sobre manter a V6 como Legacy: [POR ENQUANTO SIM]
11. `LightingPipelineMode` diz "V6 kept until V7 is approved". A V7 está aprovada? A V8 vai substituir a V7 ou é outro experimento?

	R. A V7 NÃO está aprovada. Ela foi abandonada por conceito — estava indo para um caminho que não fazia mais sentido para o jogo.
	A V8 é a substituta pretendida, não um experimento paralelo. Em 2026-09-14, com a sombra projetada rodando in-game em mundo procedural, o resultado visual foi aprovado: a sombra projetada nítida é a estética escolhida para o jogo. Pendências conhecidas: custo (~20 FPS em jogo) e iluminação de superfície de dia ainda não pronta — esta última não pode ser julgada ainda.

12. O que deve acontecer com os restos da V3 em `LightingPipeline/` (profilers, ablações por Ctrl+Shift, `SunVisibility*`, `TestConsole/`)? São ferramenta ativa ou histórico?

	R. Vão para o lixo
13. O pipeline de 2026-07-29 (`ComposeLighting.fx`, `Build*Map` em `PlayingState`, RTs de sol/penumbra no `ViewCoordinator`) ainda tem algum propósito?

	R. Por enquanto não!
14. `LightingV7Emissive` tem a tabela vazia. Existem planos de tiles emissivos (lava, minério)?

	R. Existem, mas estão pausados por enquanto.
15. `WorldMap.HasOpenSkyAbove` usa `y >= 960` fixo, e isso não bate com os presets de mundo. É intencional?
	R. Vou averiguar *PENDENTE*

## Mundo e gameplay

16. Tecido (Tissue): qual é o papel no jogo? Hubs, fast travel, corrupção, "memória" e vitalidade existem no código, mas o objetivo de design não aparece nele.

	R. Rede planetária emergente: infraestrutura, memória, equilíbrio e resposta. No gameplay é o sistema que une exploração, narrativa e ameaça: - Hubs — navegação, leitura, risco. Vivos permitem fast travel; mortos exigem restauração; corrompidos MENTEM; de memória revelam ecos - Bestiário por camadas — matar dá anatomia e drops; observar, usar reveal e acessar hubs dá narrativa - Progressão não é dano: é percepção, sintonia, leitura de memória, resistência à corrupção, alcance de reveal - Ciclo noturno — o Tissue corrige anomalias; o jogador é a anomalia Regra: mexer na rede deve ser útil, mas nunca completamente seguro.
17. `SurfaceBackgroundPass` existe e não está no `WorldGenerator`, e o preenchimento de fundo foi para `BaseTerrainFillPass` (`ddb56db`). Esse pass fica ou sai?

	R. Pode excluir também não será mais usado.
18. Interiores: o escopo é só o foco de câmera/overlay atual (`InteriorFocusSystem`) ou o "Interior Refuge System" completo descrito em `Docs/Design`?

	R. Está incompleto, mas usarei o "Interior Refuge System" completo.
19. O inimigo "signature" deveria ter comportamento próprio? Hoje é o mesmo `GroundChaserBrain`, só com outros parâmetros.

	R. Sim, deveria. A bíblia define cinco categorias de criatura, com origem e função distintas: - Respostas corretivas — Tissue em ciclo noturno; caçam Ekko por assinatura anômala - Fauna normal — ecossistema local; atacam por fome, defesa, território - Criaturas corrompidas — Tissue reescrito por Smiley - Ecos de memória — regiões saturadas de morte/trauma - Entidades externas — brechas abertas pela corrupção Diretriz explícita: monstros noturnos não devem ser convenção de sandbox, e nem todo inimigo vem do Tissue. Hoje todos usam GroundChaserBrain com parâmetros diferentes — isso contradiz o design. O "signature" provavelmente é uma resposta corretiva, que caça assinatura e não carne.
20. A colisão de móveis por raycast (`FurnitureCollisionSystem`, `IRaycastCollider`, `PlayerMotor.SetFurnitureRaycastCheck`) foi descartada em `34c6f7c` ("raycast bugado"). É pra retomar?

	 R. Não
21. O TODO em `GroundChaserBrain.cs:80` (inimigo descer por plataforma one-way) continua desejado agora que as plataformas existem?

	R. Isso resolverei quando voltar a mexer com inimigos
22. Por que os runtimes de cadeira, mesa, tocha e plataforma ficam em `Gameplay/Crafting/`, e porta e móveis em `Gameplay/World/Objects/`? Existe critério?

	R. Não, falta organização da minha parte.
23. `SubterraneanBackdropDebugRenderer` (`b9ae266`, "Ultima att") não é usado. Para que foi feito?

	R. Esse pode descartar, não faz mais parte do projeto.
24. "Elyra" (`ElyraSkyRenderer`) é o nome do planeta/mundo? Há lore documentada fora do repo?

	R. Sim. Elyra é o planeta onde o jogo se passa — um mundo primitivo (proto-Tissue) escolhido pelo Portador da Ruptura como local para selar a anomalia Smiley, e que amadureceu ao longo de bilhões de anos. `ElyraSkyRenderer` desenha o céu desse planeta. Lore documentada: ver [[NYVORN_BIBLIA_COMPLETA_DETALHADA_1]] e [[NYVORN_LIVRO_COMPLETO_HISTORIA_PERSONAGENS_GAMEPLAY]] em Design/Lore/.
