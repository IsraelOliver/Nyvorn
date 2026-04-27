# Changelog

Todas as mudancas importantes do projeto serao registradas aqui.

## v0.3.0 - 2026-04-27

### Adicionado
- Novo sistema de animacao do player dividido em parte de baixo e parte de cima.
- Suporte a root/pivo no centro dos pes, com offset por frame para respeitar o bounce da caminhada.
- Nova hotbar centralizada com 6 slots, selecao por rodinha do mouse e teclas numericas.
- Contador de pilha na hotbar.
- Nova spritesheet de terra com autotile por conexoes.
- Picareta como primeira ferramenta/arma funcional.
- Alcance maior para quebrar blocos ao usar a picareta equipada.
- Dano e empurrao da picareta durante o frame ativo do arco.

### Alterado
- Player passou a desenhar camadas separadas para corpo inferior, corpo superior e ferramenta.
- Sistema de combate usa dados da arma equipada para dano, knockback e duracao do ataque.
- Itens coletados priorizam a hotbar antes do inventario quando nao existe pilha igual no inventario.
- UI do inventario foi centralizada.
- Ordem de desenho ajustada para o player ficar visualmente integrado ao chao.

### Removido
- Animacoes antigas quebradas do player.
- Armas e testes antigos que nao fazem parte da versao atual, incluindo spear e shortstick.
- Sistema antigo de animacao baseado em uma unica textura do player.

## v0.2.0 - 2026-03-21

### Adicionado
- Atualizacao de geracao de mundo registrada na tag original `v0.2.0`.
