# Nyvorn Content

Esta pasta guarda apenas os assets fonte usados pelo `Content.mgcb`.

## Estrutura

- `blocks/`: tiles, blocos e estruturas colocáveis.
- `entities/`: sprites de criaturas e personagem.
- `entities/player/movesets/`: movesets compartilhados do personagem, como animações de ferramenta.
- `trees/`: sprites e atlases de vegetação.
- `ui/`: fontes e texturas de interface.
- `weapons/`: overlays e sprites de armas/ferramentas equipáveis.

## Convenções

- Assets carregados pelo jogo devem estar registrados em `Content.mgcb`.
- Use caminhos sem extensão no código, por exemplo `blocks/raw_wood`.
- Mantenha arquivos gerados fora do versionamento: `Content/bin/`, `Content/obj/`, `*.xnb` e `*.mgcontent`.
- Ao adicionar novas artes, prefira nomes estáveis e descritivos em `snake_case` ou no padrão já existente do asset.
