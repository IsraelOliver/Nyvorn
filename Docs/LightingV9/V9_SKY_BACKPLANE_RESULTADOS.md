# V9 Gameplay Probe — Sky Backplane, 24/09/2026

**Resultado:** hipótese implementada como modelo paralelo, opt-in por `--v9-probe-sky-backplane`. O natural anterior continua padrão. Sem promoção, commit, alteração do worldgen ou Sol.

**Recomendação:** seguir com Sky Backplane como direção arquitetural do próximo experimento, em lugar de acrescentar regras de conectividade/linha de visada ao conceito de céu. A hipótese resolve a fenda curva e unifica Surface/Shallow. **Não substituir o modo padrão nesta entrega:** a escolha visual ainda depende do usuário, e o protótipo apresenta regressões de custo em recenter/edições. A semântica de BG precisa ser aceita: um recinto fechado apenas em FG continua exposto ao céu de fundo.

## Modelo e limites preservados

- FG sólido: esconde o céu, bloqueia o transporte e pode receber somente a seção terminal existente.
- BG presente, FG vazio: esconde a exposição direta; recebe o glow geométrico vindo de aberturas próximas.
- FG e BG vazios: céu diretamente exposto, com peso determinado apenas pela profundidade.
- A classificação não usa `OpenAtmosphere` como exceção de Surface/Shallow, `entrySum`, envelope, conectividade ou fontes pontuais. Reutiliza o classificador geométrico existente para distinguir FG/BG/ar.
- FG continua significando a colisão `Solid` já adotada pelo probe. Plataformas não ocluem. Isso não é uma redefinição geral dos materiais do jogo.
- A descoberta das cenas de teste ainda usa o envelope antigo para encontrar a mesma entrada. Isso não participa da iluminação do novo modo.

Tochas, DDA, raio 144, potência 4, RGB, falloff, shader, composição artificial, occupancy mask e cache artificial não foram recalibrados. A seção sólida continua com limite 6 px, absorção 2,8 px e incidência existentes. O laboratório e o gameplay normal não mudaram.

## Uma força de céu, três receptores

`t = clamp((yEmTiles − Surface.StartY) / (Shallow.EndY + 1 − Surface.StartY), 0, 1)`

`peso = 1 − t²(3 − 2t)`

No Small: Surface começa em 109, Shallow em 199, Cavern em 271. Não há condição de troca entre Surface e Shallow. A curva e sua derivada chegam a zero no início da Cavern; permanecem zero na Deep.

| Posição | Peso aproximado |
|---|---:|
| Acima/início da Surface | 1 |
| Superfície da entrada, linha 145 | 0,874 |
| Início do Shallow, linha 199 | 0,417 |
| Meio da vista de fissuras, linha 233 | 0,139 |
| Centro da última linha do Shallow, 270,5 | 0,0000285 |
| Cavern e Deep | 0 |

O ar exposto recebe `OpenSky × peso × RGB frio` (`OpenSky=5,2806554`, herdado do lab). A apresentação do céu usa a mesma classificação e o peso no centro do tile. A irradiância direta usa a profundidade por pixel; a cobertura de apresentação continua discretizada em tiles, como no probe anterior.

O BG reaproveita a propagação geodésica 5/7 existente, apenas através de BG, com FG bloqueando e sem cortar quinas. Alcance de 40 px preservado. No experimento, o pico vale **0,35 do céu aberto**, multiplicado pelo peso da abertura e pela queda quadrática com distância; o peso também é limitado pela profundidade do receptor. O resultado é mais fraco que a exposição direta e não cria fonte na Cavern. A referência mantém seu pico e seu fade anteriores intactos.

Cada face de FG consulta o vizinho de ar: se for céu, recebe a exposição; se for BG iluminado, recebe **0,25 do campo de glow nesse vizinho**, passando pelo mesmo `V9LightMath.Visibility` terminal. Usa o melhor lado, não soma várias contribuições do mesmo céu. Não há propagação dentro da pedra. Esses dois fatores são parâmetros novos do experimento; a absorção/incidência/profundidade anteriores não mudaram.

O domínio do glow inclui um tile extra de margem para as consultas de faces na borda da janela. Isso evita fazer a resposta depender do recenter da câmera.

## Evidência funcional e visual

**35 estados capturados; 255 PASS, 0 FAIL, 1 SKIP.** O SKIP é a abertura lateral natural que não existe nessa seed, já documentada. A referência ampliada tem 41 estados capturados; 453 PASS, 0 FAIL, 1 SKIP. Seis variações antigas de Sol/pico/alcance não são executadas no experimento, porque não pertencem a esta comparação.

| Caso | Resultado |
|---|---|
| Fenda inclinada | Toda a parte sem FG/BG dentro da faixa natural mostra céu e ilumina as bordas, inclusive após a curva fora da linha de visada da boca. |
| Túnel/abrigo fechados só em FG | Continuam expostos onde o BG está vazio. É consequência deliberada da hipótese, não vazamento de luz por pedra. |
| Sala com BG completo | Zero texels internos com natural, inclusive quando há cinco tochas. |
| Abertura separada da parede por dois tiles de BG | **288 texels da parede** recebem a contribuição fraca derivada do glow. |
| Pilar FG completo entre abertura e região de BG | **Zero texels iluminados atrás do pilar** no interior da sala. |
| Fechar BG e reabrir | Fecha a luz da sala; reabrir restaura o campo bit a bit. |
| Fechar FG da entrada e reabrir | Reabertura restaura o campo bit a bit. O fechamento só oculta as células cobertas, não o céu atrás do restante do túnel. |
| Recenter / wrap | Texels sobrepostos e relabel por uma largura de mundo idênticos. |
| FG | Profundidade máxima medida **5,5 px**; glow bruto continua zero em FG. |
| Cavern | Buraco de BG continua coberto, sem natural. Zero natural em todos os texels amostrados a partir dessa profundidade. Deep é zero pela mesma curva; não houve captura dedicada na Deep. |

[OBSERVAÇÃO VISUAL] A fenda mantém uma borda estreita iluminada ao longo da inclinação, com a massa sólida preta. No Shallow, céu e glow ficam consideravelmente mais escuros que na referência; as áreas protegidas permanecem pretas. A contribuição de glow no FG distante é discreta no frame final e clara no diagnóstico próprio. As cinco tochas preservam os focos quentes; o contexto frio muda porque o natural mudou. O abrigo sem BG mostra o cenário distante entre suas paredes, mesmo com o acesso lateral fechado.

Não foi feita recalibração após essas observações. A aprovação artística é do usuário.

## Comparação e preservação

- Referência anterior versus referência após a implementação: **37/37 estados históricos com os hashes dos campos total/natural/artificial e glow idênticos**; sete diagnósticos PNG por estado idênticos.
- Frames finais e albedos da referência: **zero pixels diferentes fora da caixa do personagem** nos 37 estados. A caixa foi excluída porque o personagem acompanha o cursor real entre execuções.
- Referência versus Sky Backplane: **35/35 buffers artificiais com SHA-256 idêntico**, incluindo as quatro cenas novas. Os 35 PNGs artificiais também são idênticos.
- Verificação do novo natural: reconstrução do zero sobre a solidez do mundo versus atualização da janela, composição com tochas na ordem e comparação dos canais float. As checagens de classificação, exposição direta, profundidade, geometria construída, recenter e wrap complementam essa referência, que reutiliza o avaliador do experimento.
- Build Release passou, com os dois CS0649 preexistentes. Nenhuma dependência/perfil foi alterado.

## Custo medido (CPU, uma execução por modo)

| Cenário | Draw mediano atual → Backplane | Observação |
|---|---:|---|
| Surface parada | 1,26 → 1,27 ms | Estável em cache |
| Caverna parada, 5 tochas | 1,36 → 1,30 ms | Dentro da variação da execução |
| Uma tocha móvel | 10,00 → 9,49 ms | Caminho artificial preservado; não atribuir a otimização |
| Alternar tile de caverna | 42,62 → 77,34 ms | Regressão |
| Alternar BG no Shallow | 11,93 → 45,48 ms | Regressão |
| Alternar FG no Shallow | 11,86 → 45,60 ms | Regressão |
| Fechar/abrir entrada | 63,06 → 39,17 ms | Evita entradas naturais pontuais |

Máximo de atualização do campo durante os percursos com recenter: cerca de 15–18 ms na referência e 35–42 ms no experimento. O novo natural reconstrói sua janela inteira quando há revisão do mapa/recenter; não foi implementada otimização de região suja. Não é uma proposta de desempenho pronta para produção. GPU não medida; os benchmarks são amostras únicas, não uma distribuição entre várias execuções.

`NaturalBytes=0` nos metadados significa que não há cache de **fontes pontuais naturais**, não ausência de buffers. O experimento mantém aproximadamente 20 bytes/pixel adicionais para campo natural e diagnósticos de face (cerca de 9,66 MiB na janela 904×560), além do glow compartilhado e seus buffers. Não se reabriu cache por fonte nem occupancy mask.

## Executar e revisar

Na raiz do repositório:

```powershell
dotnet build .\Nyvorn\Nyvorn.csproj -c Release --no-restore
# Natural atual
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe
# Experimento paralelo
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-sky-backplane
# Evidência automatizada / benchmark (executar separadamente)
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-sky-backplane --v9-probe-capture --v9-output screenshots/v9-gameplay-probe/sky-backplane/capture
dotnet .\Nyvorn\bin\Release\net8.0\Nyvorn.dll --lighting-v9-gameplay-probe --v9-probe-sky-backplane --v9-probe-bench --v9-output screenshots/v9-gameplay-probe/sky-backplane/bench
```

Evidências locais em `screenshots/v9-gameplay-probe/sky-backplane/`:

- [Comparador de PNGs originais](../../screenshots/v9-gameplay-probe/sky-backplane/comparison.html): seleção de cena e frame/diagnóstico, referência e experimento lado a lado.
- [Comparação numérica](../../screenshots/v9-gameplay-probe/sky-backplane/comparison.json).
- [Checks do experimento](../../screenshots/v9-gameplay-probe/sky-backplane/capture/checks.txt) e [da referência](../../screenshots/v9-gameplay-probe/sky-backplane/reference-after/checks.txt).
- `reference/`: captura antes da integração completa; `reference-after/`: padrão após a integração; `capture/`: experimento.
- `bench/` e `bench-reference/`: medições; `before-hashes.json`: manifesto anterior à edição.
- `*_fg-from-glow.png`: contribuição terminal originada no campo do BG, isolada. Os outros mapas continuam disponíveis.

[Verify-V9Backplane.py](Verify-V9Backplane.py) refaz a comparação e gera o comparador HTML, usando Python com Pillow. PNGs não são editados.

## Arquivos e continuidade

Novos: `V9ProbeBackplane.cs`, `PlayingState.V9ProbeBackplane.cs`, este relatório e o verificador Python.

Integrações delimitadas: `V9ProbeOptions.cs`, `V9ProbeField.cs`, `V9ProbeSkyGlow.cs`, `PlayingState.V9Probe.cs`, `PlayingState.V9ProbeCapture.cs` e `PlayingState.V9ProbeShallowGlow.cs`. Os documentos de estado/handoff apontam para este relatório. O caminho de referência do glow segue idêntico quando não recebe a função de profundidade do backplane.

Branch `physics`, HEAD `f7edee1e5902c5f05265d6c50076aff042afb8c9`. Checkout sujo preservado; nenhuma operação de reset/checkout/clean/commit. Capturas ficam ignoradas pelo Git: devem acompanhar uma transferência de workspace.

A próxima decisão é artística/semântica: aceitar céu visível em toda abertura FG/BG nessa faixa e o Shallow mais escuro desta curva. Se aceita, o Backplane é uma base conceitual mais simples; antes de promoção do renderer, seu custo de reconstrução precisa de trabalho próprio, separado e autorizado. Nenhuma conectividade especial, regra de ar selado ou Sol foi adicionada para contornar essa decisão.
