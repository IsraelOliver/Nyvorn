# Climate and Events System

## Ideia central

O clima de Nyvorn deve funcionar como uma camada viva acima do relogio do mundo.
O tempo continua sendo uma fonte sistemica e previsivel, mas o ceu, a luz, a chuva,
o Tissue e alguns comportamentos de gameplay respondem como se o planeta estivesse
reagindo ao ciclo.

A implementacao atual separa tres conceitos:

- `WorldDayNightCycle`: relogio, fases fixas, horario textual e indice de ciclo.
- `WorldEnvironmentSystem`: clima, eventos, ceu, residuos temporarios e leituras de gameplay.
- Renderizadores/sistemas de gameplay: consomem o estado ambiental sem decidir as regras do clima.

Essa divisao evita que o ciclo dia/noite vire uma classe gigante. O relogio diz
"quando"; o ambiente decide "como o mundo sente isso".

## Fases do dia

Um dia completo dura 30 minutos reais. `TimeOfDay01` continua sendo a fonte continua
de tempo, mas agora tambem existe uma leitura discreta por fase:

| Fase | Horario | Papel |
|---|---:|---|
| `DeepNight` | 00:00-04:00 | noite mais intensa, pico da noite corretiva |
| `PreDawn` | 04:00-05:00 | dissipacao da noite e janela de pressagios |
| `Sunrise` | 05:00-06:30 | transicao visual do amanhecer |
| `Morning` | 06:30-12:00 | dia estavel |
| `Noon` | 12:00-14:30 | luz mais forte |
| `Afternoon` | 14:30-17:30 | segunda janela de clima |
| `Sunset` | 17:30-19:30 | pressagio da noite corretiva |
| `Night` | 19:30-00:00 | noite ativa |

Os gatilhos sistemicos nao dependem de quando a cor do ceu parece mudar. Eles usam
marcos fixos:

- `DayStarted`: 06:00
- `NightStarted`: 19:30
- `DeepNightStarted`: 00:00
- `PreDawnStarted`: 04:00

Isso permite que uma regra de gameplay comece precisamente as 19:30, mesmo que o ceu
comece a ficar estranho antes, durante o por do sol.

## Estado ambiental

`WorldEnvironmentSystem` produz estados pequenos para o restante do jogo:

- `SkyState`: cores do topo e horizonte, opacidade de estrelas, brilho do sol/lua,
  nevoa, nuvens, chuva, eclipse e overlay noturno.
- `WeatherState`: chuva, vento, cobertura de nuvem, umidade e molhamento residual.
- `EventState`: eventos ativos por canal, estagio e tempo restante.
- `TissueCycleState`: intensidade da noite corretiva e pulso ambiental do Tissue.

Esses estados sao derivados e baratos de ler. Sistemas como respawn, grama, Tissue e
renderizacao nao precisam conhecer a logica interna de chance, cooldown ou calendario.

## Canais de evento

Eventos ambientais sao organizados por canal:

| Canal | Uso atual |
|---|---|
| `Astronomical` | eclipse solar |
| `Weather` | chuva |
| `Ecological` | reservado para eventos de bioma, fauna, floracao ou pragas |
| `Tissue` | noite corretiva |
| `Smiley` | reservado para eventos anormais ligados ao Smiley |

Todo evento segue a mesma linguagem de estagios:

1. `Omen`: pressagio, sinais leves, mundo antecipando algo.
2. `Transition`: o evento entra em cena.
3. `Active`: estado principal.
4. `Dissipating`: queda controlada.
5. `Residue`: sobras temporarias que afetam leitura visual ou sistemas leves.

Mesmo quando um evento ainda nao tem conteudo proprio, a estrutura ja deixa espaco
para ele entrar sem quebrar a API.

## Eventos implementados

### Noite corretiva

A noite corretiva e automatica em todo ciclo. Ela comeca como pressagio no `Sunset`,
fica ativa em `Night` e `DeepNight`, atinge o pico perto da meia-noite e dissipa em
`PreDawn`.

Efeitos atuais:

- aumenta o pulso visual ambiental do Tissue;
- reduz o delay de respawn de inimigos durante a janela mais forte;
- colore e escurece o ceu de forma mais organica;
- deixa um residuo leve no amanhecer.

A primeira versao nao aplica mutacoes permanentes no Tissue. Ela e atmosferica e
temporaria de proposito, para validar a sensacao antes de adicionar consequencias
mais pesadas.

### Chuva

A chuva pode ser escolhida de forma deterministica no comeco do dia ou a tarde, com
cooldown de um ciclo. Tambem pode ser forcada pelo console.

Efeitos atuais:

- aumenta vento, nuvens e cinza do ceu;
- desenha linhas procedurais de chuva;
- reduz levemente a luz do ambiente;
- acumula `Wetness` e deixa molhamento residual;
- aumenta de forma leve a chance de crescimento da grama enquanto o mundo esta molhado.

Ainda nao existem pocos desenhadas, tiles molhados permanentes, particulas de impacto
ou variacoes por bioma. Esses pontos dependem de arte e refinamento visual.

### Eclipse

O eclipse e um evento astronomico raro. Ele pode ser decidido a partir do `PreDawn`,
e quando confirmado entra no dia em torno de `DayStarted`. O cooldown atual e de tres
ciclos. Tambem pode ser forcado pelo console.

Efeitos atuais:

- escurece o dia;
- altera a cor e leitura do sol;
- adiciona oclusao visual sobre o sol;
- permite comportamento de respawn mais proximo da noite durante o dia;
- deixa residuo leve ate a tarde.

Por enquanto ele usa inimigos existentes como placeholder. Criaturas, musicas,
simbolos, tiles ou entidades exclusivas do eclipse ficam para uma etapa futura.

## Persistencia

`PlanetSaveData.Version` foi atualizado para `14`.

O save guarda:

- `CycleIndex`;
- eventos ativos em forma leve;
- cooldowns de chuva e eclipse;
- medidores simples: `Humidity`, `CloudCover`, `Wind`, `Wetness`.

Saves antigos (`Version <= 13`) carregam com `CycleIndex = 0`, sem eventos ativos e
com medidores ambientais neutros.

## Comandos de debug

Comandos de tempo:

- `/time status`
- `/time day`
- `/time night`
- `/time dawn`
- `/time sunrise`
- `/time noon`
- `/time sunset`
- `/time midnight`

Comandos de evento:

- `/event status`
- `/event rain start`
- `/event rain stop`
- `/event eclipse start`
- `/event eclipse stop`
- `/event clear`

Esses comandos existem para validar rapidamente transicoes, ceu, persistencia,
interacoes com gameplay e leitura de console.

## Pontos que ainda pedem arte

A base atual usa placeholders procedurais. Funciona para prototipar, mas alguns
elementos devem ganhar arte propria para ficarem com identidade de Nyvorn:

- lua;
- camadas de nuvem;
- particulas de chuva e respingos;
- pocos e brilho de tile molhado;
- silhuetas ou sinais especificos de eclipse;
- variacoes do sol durante eventos astronomicos;
- sinais visuais do Tissue durante a noite corretiva;
- assets de eventos de Smiley, quando esse canal for ativado.

Enquanto essa arte nao existir, o sistema continua valido como esqueleto jogavel:
os eventos passam por estagios, persistem no save, afetam gameplay leve e podem ser
testados pelo console.

## Proximos passos sugeridos

- Fazer smoke test visual manual para dia, entardecer, noite, chuva e eclipse.
- Ajustar duracoes e intensidades depois de sentir o ritmo em jogo.
- Criar assets definitivos de nuvem, lua e chuva antes de polir o renderer.
- Adicionar audio/ambiancia por `WorldEventStage`.
- Criar eventos reais para os canais `Ecological` e `Smiley`.
- Introduzir consequencias permanentes apenas quando a leitura temporaria estiver boa.
