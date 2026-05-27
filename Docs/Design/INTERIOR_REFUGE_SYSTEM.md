# Interior Refuge System

## Ideia central

O sistema de interiores do Nyvorn não deve ser apenas uma validação técnica de casa, como um requisito para NPC ou crafting. Ele pode se tornar uma mecânica atmosférica própria: quando o jogador constrói um espaço fechado em um mundo hostil, o jogo reconhece aquele lugar como um refúgio.

A casa deixa de ser somente uma estrutura funcional e passa a ser uma resposta emocional do jogador ao mundo. Fora dela existe ameaça, ruído, clima, criaturas e corrupção. Dentro dela existe uma pequena pausa. Um lugar onde o mundo parece respirar mais devagar.

## Experiência desejada

Quando o jogador entra em um interior válido, o jogo pode mudar sutilmente a percepção do ambiente:

- câmera mais próxima e focada no cômodo;
- mundo externo escurecido ou menos destacado;
- sons externos abafados;
- chuva, vento e ruídos hostis com volume reduzido;
- interface levemente mais calma;
- monstros fora da casa menos visualmente dominantes;
- sensação de segurança temporária, mesmo que frágil.

A intenção não é transformar o interior em uma tela separada. O jogador continua no mesmo mundo, mas o enquadramento, o som e a leitura visual indicam que aquele espaço tem significado.

## Diferença em relação a Terraria

Em Terraria, casas são muito associadas a requisitos de NPCs e organização funcional. No Nyvorn, a ideia é usar interiores como parte da identidade do jogo.

O sistema pode começar parecido com um reconhecimento de cômodo fechado, mas a função principal deve ser atmosférica e narrativa. A casa não é apenas válida ou inválida. Ela pode ser acolhedora, instável, ameaçada ou corrompida.

## Relação com horror cósmico

Como Nyvorn tem uma direção de horror cósmico, o conceito de refúgio pode ser usado de forma cruel e interessante.

Quanto mais o tecido do planeta, a corrupção ou forças desconhecidas avançarem, mais o sistema de refúgio pode começar a falhar. A casa continua fisicamente fechada, mas o mundo deixa de reconhecê-la como segura.

Possíveis sinais dessa falha:

- a luz interna pisca;
- o foco da câmera demora mais para estabilizar;
- o escurecimento fora da sala fica mais denso;
- sons externos voltam aos poucos mesmo dentro da casa;
- paredes de fundo parecem pulsar ou respirar;
- o interior deixa de reduzir completamente a sensação de ameaça;
- a casa ainda existe, mas já não parece totalmente protegida.

Isso cria uma tensão importante: o jogador constrói abrigo, mas o universo pode lentamente invadir até os lugares que deveriam ser seguros.

## Separação conceitual sugerida

Para evitar misturar regras demais em um único sistema, vale separar dois conceitos:

### Interior

Um espaço fechado com parede de fundo, detectado pelo sistema de flood fill. Serve para foco visual, câmera, atmosfera e leitura espacial.

### Shelter

Um interior que também possui condições de segurança, como porta, fechamento adequado e talvez distância de corrupção. Serve para regras de gameplay, proteção, spawn, conforto, NPCs ou bônus.

Essa separação permitiria que cavernas, bases improvisadas, cápsulas alienígenas e salas orgânicas fossem reconhecidas como interiores sem necessariamente serem abrigos seguros.

## Possíveis usos futuros

- bônus de descanso ou recuperação dentro de shelters;
- redução de spawn de inimigos próximos;
- ponto de respawn se o abrigo estiver estável;
- NPCs exigindo shelter, não apenas interior;
- conforto afetado por tamanho, objetos, iluminação e corrupção;
- eventos onde a corrupção invade um interior antes considerado seguro;
- variações visuais e sonoras conforme o estado emocional/ambiental do cômodo.

## Regra de arquitetura

O `InteriorFocusSystem` deve continuar responsável apenas por detectar interiores e oferecer dados visuais, como bounds e foco de câmera.

Regras futuras como conforto, proteção, spawn, sanidade, corrupção interna ou NPCs devem ficar em sistemas separados que leem o resultado do interior, em vez de transformar o `InteriorFocusSystem` em uma classe gigante.

## Estado atual

A branch `feature/v0.4-mining-progression` já possui uma base inicial com:

- detecção de interior por flood fill;
- exigência de parede de fundo;
- limites por blocos sólidos ou portas;
- tamanho mínimo e máximo de sala;
- foco de câmera no cômodo;
- escurecimento do mundo fora do interior;
- intensidade diferente no modo construção.

Essa base deve ser mantida simples por enquanto. O próximo passo ideal é consolidar a v0.4 antes de expandir o sistema emocional de interiores.
