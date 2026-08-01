# Phase 3.2A: Runtime Test Checklist

## Objetivo
Validar visualmente que o campo SunVisibility está sendo computado corretamente e funciona com movimentação de câmera, mudanças de hora do dia, e world wrapping.

## Pré-requisitos
1. Build bem-sucedida (Debug ou Release)
2. Jogo executando normalmente
3. Câmera com movimento livre

## Ativação do Debug SunVisibility

### Hotkey
Pressione **`Ctrl+Shift+J`** para ciclar entre modos de debug. Cicle até ver:
```
Debug: SunVisibility
```

**Sequência de modos**:
- None → Classification → SunOpacity → LocalLightOpacity → SampleGrid → SunVisibility → (volta para None)

A visualização mostra:
- **Preto (0.0)**: Sol completamente bloqueado por foreground sólido
- **Branco (1.0)**: Caminho completamente livre até o Sol
- **Cinza (0.0 < x < 1.0)**: Transmissão parcial (raramente visível com opacidades binárias)

### Verificação
Ao ativar SunVisibility debug, você deve ver:
1. Uma grade de pixels coloridos (grayscale) sobreposta ao mundo
2. Sem erro de renderização
3. Pixels se atualizando enquanto a câmera se move
4. Mudança visível quando o Sol se move (dia/noite)

## Checklist de Testes Visuais

### A. Céu Aberto
**Cenário**: Localização com foreground vazio, sem estruturas.
**Esperado**: SunVisibility = 1.0 (branco sólido)
**Verificar**:
- [ ] Todos os pixels de SunVisibility são brancos
- [ ] Nenhum erro visual durante movimentação de câmera

### B. Plataforma Sólida
**Cenário**: Ficar sobre um bloco foreground sólido.
**Esperado**: SunVisibility = 0.0 (preto sólido)
**Verificar**:
- [ ] Sample dentro do foreground sólido retorna 0
- [ ] Há transição visual entre foreground e ar

### C. Fissura Diagonal
**Cenário**: Ficar embaixo de uma parede diagonal (ex: rampa de blocos).
**Esperado**: SunVisibility varia gradualmente de 0 a 1
**Verificar**:
- [ ] Gradação visual contínua
- [ ] Sem artefatos ou descontinuidades abruptas

### D. Background Wall
**Cenário**: Localização com background wall próximo.
**Esperado**: SunVisibility = 1.0 (background não bloqueia Sol)
**Verificar**:
- [ ] Background walls aparecem brancos (não bloqueiam)
- [ ] Apenas foreground sólido cria sombras

### E. Blocker Fora da Tela
**Cenário**: Ficar com um bloco foreground sólido longe para um lado da câmera, mas na trajetória do Sol.
**Esperado**: SunVisibility = 0 quando o raio atinge o blocker distante
**Verificar**:
- [ ] Ray marcher consulta geometria fora da active region
- [ ] Sem timeout ou erro de performance

### F. Câmera em Movimento
**Cenário**: Mover a câmera livremente enquanto SunVisibility debug ativo.
**Esperado**: Visualização atualiza suavemente
**Verificar**:
- [ ] Sem stuttering durante movimento
- [ ] Grid de samples permanece alinhado com tiles
- [ ] Sem "ghost" pixels de frame anterior

### G. World Seam (Horizontal Wrapping)
**Cenário**: Mover a câmera até a borda do mundo (esquerda/direita).
**Esperado**: Ray marcher detecta wrapping e continua corretamente
**Verificar**:
- [ ] Sem saltos visuais ao cruzar a costura
- [ ] Trajetória do raio é contínua (não "salta" para o outro lado)

### H. Manhã
**Cenário**: Tempo de jogo = manhã (Sol baixo no horizonte, vindo da esquerda).
**Esperado**: Sombras longas para a direita
**Verificar**:
- [ ] SunVisibility muda conforme hora do dia
- [ ] Direção das sombras corresponde ao azimute do Sol

### I. Meio-dia
**Cenário**: Tempo de jogo = meio-dia (Sol alto, quase vertical).
**Esperado**: Sombras curtas, diretamente abaixo
**Verificar**:
- [ ] Raios quase verticais funcionam
- [ ] Sombras diretas abaixo de estruturas

### J. Tarde
**Cenário**: Tempo de jogo = tarde (Sol alto, vindo da direita).
**Esperado**: Sombras longas para a esquerda
**Verificar**:
- [ ] Direção oposta à manhã
- [ ] Transição suave com o tempo

### K. Região Subterrânea (Noite)
**Cenário**: Teleportar/cavar abaixo de blocos sólidos.
**Esperado**: SunVisibility = 0.0
**Verificar**:
- [ ] Nenhuma luz do Sol penetra rock sólido
- [ ] Early-out funciona (raio termina rapidamente)

## Métricas de Performance

### Ativação
Procure por instruções no jogo para imprimir métricas (geralmente Alt+M ou console command).

### O Que Observar
```
Foundation Build Time (ms)
├─ Classification: ~0.1-0.5 ms
├─ Opacity Build: ~0.1-0.5 ms
└─ SunVisibility: ~1-5 ms (depend do number of samples)

SunVisibility Metrics
├─ Rays Computed: ~30k (típico, varia com region)
├─ Cells Traversed: ~50-200k (total ray march steps)
├─ Early Outs: ~29k+ (maioria dos raios bloqueia cedo)
└─ Average Cells per Ray: ~2-5 (baixo = eficiente)
```

### Limites de Performance
```
Foundation Average: <= 5 ms
Foundation P95:     <= 8 ms
Foundation Hard:    < 10 ms
Allocated/Update:   = 0 bytes (steady-state)
```

## Troubleshooting

### "SunVisibility mode not found"
Verifique se foi recompilado com as mudanças de Phase 3.2A. Re-build.

### "All pixels are black"
Pode ser correto se o Sol está abaixo do horizonte (noite). Verifique a hora do jogo.

### "All pixels are white"
Pode ser correto se não há bloqueadores no caminho do Sol. Mova para uma área com estruturas.

### "Visual stuttering during movement"
Verifique o tempo de Build de SunVisibility. Se > 5ms, há problema de performance. Reporte logs.

### "Visual misalignment (pixels offset from tiles)"
Verifique se o transform world-view está correto. Deve usar o mesmo matrix que Phase 2.

## Reporte para o desenvolvedor

Após completar todos os testes, reporte:

1. Qual teste passaram/falharam
2. Tempo de build SunVisibility observado (média e picos)
3. Número de samples e cells por raio
4. Allocations por update (verifique que é 0)
5. Screenshots de cada cenário (A-K)
6. Qualquer erro console ou comportamento inesperado

---

**Não marque Phase 3.2A como aprovada até submeter este checklist preenchido.**
