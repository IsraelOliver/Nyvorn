---
verificado_em: 2026-09-14
commit: ec39358c71c67fd05a05063c5f52aa148fb35583
---

# Decisões — iluminação

Registro vivo. Os motivos vêm das respostas do desenvolvedor às perguntas 5 a 11 de [[04-perguntas]], dadas em 2026-09-14. Os períodos vêm de [[02-iluminacao-historico]].

| Versão | Período | Motivo do abandono | Uma linha de detalhe |
|---|---|---|---|
| V1 | 2026-07-12 → 2026-07-29 | Conceito visual | Iluminação no estilo Terraria que funcionava, mas não era o visual desejado. |
| V2 | 2026-07-30 (1 dia) | Risco de engenharia | Mexeu em partes desnecessárias e quase destruiu a base; não foi por resultado ruim de iluminação. |
| V3 | 2026-07-31 → 2026-08-03 | Performance | Travou por FPS; a técnica escolhida não escalava. |
| V4 | Não recuperável (probe entra em `cc0f24d`, 2026-08-05) | Performance | Travou por FPS junto com a V3; o que foram V4.0–V4.2A segue em aberto (pergunta 7). |
| V5 | 2026-08-05 → 2026-08-10 | Fundação conceitual errada | Primeira a dar resultado visual; as ideias que funcionaram foram reaproveitadas na V6. |
| V6 | 2026-08-10 → 2026-09-11 | Apodrecimento de código | Primeira perto do visual desejado, mas acumulou bug sobre bug; mantê-la como "Legacy" (F9) segue em aberto. |
| V7 | 2026-09-11 (4 commits) | Conceito | Não aprovada; seguia um caminho que deixou de fazer sentido para o jogo. |

Nenhuma versão morreu pelo mesmo motivo que outra, com uma exceção: V3 e V4 travaram ambas por performance. As demais pararam cada uma por uma razão própria: conceito visual (V1), risco de engenharia (V2), fundação conceitual errada (V5), apodrecimento de código (V6) e conceito (V7).

A V8 é a substituta pretendida da V7, não um experimento paralelo. Em 2026-09-14, com a sombra projetada rodando in-game em mundo procedural, o resultado visual foi aprovado: a sombra projetada nítida é a estética escolhida para o jogo. Ficam duas pendências. A primeira é o custo, de ~20 FPS em jogo. A segunda é a iluminação de superfície de dia, que ainda não está pronta e por isso não pode ser avaliada.
