# LightingV3: Arquitetura Completa Corrigida

**Status**: Aprovado para Fase 1  
**Última Atualização**: Pós-Fase 0  
**Baseline**: FASE 0 isolamento mensurável implementado

---

## 1. RESOLUÇÃO LUMINOSA SUB-TILE

### Terminologia Corrigida

```csharp
// Nomenclatura clara:
public struct LightingSamplingConfig
{
    /// Amostras por eixo (X e Y)
    public const int SamplesPerAxis = 2;
    
    /// Total de amostras por tile (SamplesPerAxis²)
    public const int SamplesPerTile = SamplesPerAxis * SamplesPerAxis;  // = 4
    
    /// Espaçamento entre amostras em unidades de tile
    public const float SampleSpacingTiles = 1.0f / SamplesPerAxis;  // = 0.5
}

// Grid 2×2 por tile:
// VisibleRegionTiles = 60×40
// LightMapSamples = (60 * 2) × (40 * 2) = 120 × 80 = 9600 samples
```

### Configurabilidade

```csharp
// Escalável: SamplesPerAxis pode ser 2, 3, 4, ou maior
enum LightingSamplingResolution
{
    Low = 2,       // 4 samples/tile
    Medium = 4,    // 16 samples/tile
    High = 6,      // 36 samples/tile
}

// A escolha afeta:
// - Precisão de sombras (> samples = sombras mais precisas)
// - Tamanho do light map (> samples = mais memória)
// - Custo computacional (> samples = mais cálculos)
```

### Precisão Representável

**Com 2×2 (4 samples/tile)**:
- Resolução: 0.5 tile = 8 pixels (se tile = 16px)
- Representa: ~0.5 tile depth de iluminação de foreground
- Sombras de galhos: ~1 amostra de largura
- Penumbra: suave com interpolação bilinear

**Futuro: 4×4 (16 samples/tile)**:
- Resolução: 0.25 tile = 4 pixels
- Melhor para: oclusores finos (galhos individuais)
- Trade-off: 4× mais memória e cálculos

---

## 2. OCCLUDER FIELD REAL (Separado por Tipo de Luz)

### Arquitetura Multi-Fonte

```csharp
interface IOccluderProvider
{
    IReadOnlyList<OccluderGeometry> GetStaticOccluders(Rectangle region);
    IReadOnlyList<OccluderGeometry> GetDynamicOccluders(Rectangle region);
    OccluderMask GetSubTileOccluderMask(int tileX, int tileY);
}

struct OccluderGeometry
{
    public Vector2 position;
    public Vector2 size;
    public float opacity;           // 0 = transparent, 1 = opaque
    public OccluderMaterial material;
}

enum OccluderMaterial
{
    SolidForeground,    // Rocha, árvore: bloqueia Sun E Local
    Transmissive,       // Galhos esparsos: bloqueia parcial
    BackgroundWall,     // Parede de fundo: bloqueia Local APENAS, Sun passa
    Decor               // Decoração: configurable
}
```

### Separação por Tipo de Luz (CRÍTICO)

```csharp
class OccluderField
{
    // Dois campos de opacidade completamente independentes:
    
    /// Para cálculo de visibilidade solar (sombra direcional)
    private float[] sunOpacityPerSample;
    
    /// Para cálculo de oclusão de luz local (tochas)
    private float[] localLightOpacityPerSample;
    
    public void Build(IOccluderProvider provider, LightingGridResources resources, Rectangle region)
    {
        for (int sampleIdx = 0; sampleIdx < totalSamples; sampleIdx++)
        {
            var occlusion = QueryOccluders(sampleIdx);
            
            // BackgroundWall: bloqueia LOCAL light, MAS NÃO bloqueia Sun
            if (occlusion.material == OccluderMaterial.BackgroundWall)
            {
                sunOpacityPerSample[sampleIdx] = 0.0f;        // SUN PASSA
                localLightOpacityPerSample[sampleIdx] = occlusion.opacity;  // LOCAL bloqueia
            }
            // SolidForeground: bloqueia TUDO
            else if (occlusion.material == OccluderMaterial.SolidForeground)
            {
                sunOpacityPerSample[sampleIdx] = occlusion.opacity;
                localLightOpacityPerSample[sampleIdx] = occlusion.opacity;
            }
            // ... etc
        }
    }
    
    public float GetSunOccluderOpacity(int sampleX, int sampleY)
        => sunOpacityPerSample[sampleY * width + sampleX];
    
    public float GetLocalLightOccluderOpacity(int sampleX, int sampleY)
        => localLightOpacityPerSample[sampleY * width + sampleX];
}
```

---

## 3. RAIOS SOLARES PARALELOS (Algoritmo Correto)

### Sem Cones de Projeção

**Problema anterior**: O pseudocódigo incluía projeção de cone:
```
for (float d = depth + stepSize; d < maxShadowDist; d += stepSize)
    // marcar cone inteiro como sombreado
```

Esta abordagem é O(depth × coneWidth) e gera sombras artificialmente largas.

### Algoritmo de Raios Paralelos Verdadeiro

```csharp
public void ComputeSunVisibility_ParallelRays(
    Vector2 sunDirection,
    OccluderField occluderField,
    SolarOcclusionContext occlusionCtx,
    LightingGridResources resources,
    float[] sunVisibilityMapOutput)
{
    // 1. Preparar direções de raio
    Vector2 rayDir = -sunDirection;  // Direção dos raios (de onde sol vem)
    Vector2 perpDir = new Vector2(-rayDir.Y, rayDir.X);  // Perpendicular
    
    // Normalizar para resolução de amostra (não de tile)
    float rayStepX = rayDir.X * SamplesPerAxis;
    float rayStepY = rayDir.Y * SamplesPerAxis;
    float perpStepX = perpDir.X * SamplesPerAxis;
    float perpStepY = perpDir.Y * SamplesPerAxis;
    
    // 2. Iterar cada linha de raio paralelo
    // Número de linhas perpendiculares = altura da região naquela direção
    int numPerpLines = (int)Math.Ceiling(
        Math.Abs(perpDir.X) * resources.LightSampleWidth +
        Math.Abs(perpDir.Y) * resources.LightSampleHeight) + SafeMargin;
    
    for (int perpIdx = -numPerpLines; perpIdx <= numPerpLines; perpIdx++)
    {
        // Ponto de entrada desta linha na borda da oclusão region
        Vector2 rayStart = (Vector2)occlusionCtx.OcclusionRegion.Location +
                          perpDir * perpIdx * SampleSpacingTiles;
        
        float rayStartSampleX = rayStart.X * SamplesPerAxis;
        float rayStartSampleY = rayStart.Y * SamplesPerAxis;
        
        // 3. Traçar este raio uma ÚNICA VEZ, profundidade zero até maxShadow
        bool rayBlocked = false;
        
        for (float depth = 0; depth <= occlusionCtx.MaxShadowDistance; depth += 0.5f)
        {
            float sampleX = rayStartSampleX + rayStepX * depth;
            float sampleY = rayStartSampleY + rayStepY * depth;
            
            int sampleXInt = (int)sampleX;
            int sampleYInt = (int)sampleY;
            
            if (!IsValidSample(sampleXInt, sampleYInt, resources))
                break;  // Saiu da región
            
            // 4. Consultar oclusor apenas NESTE ponto
            float occlusion = occluderField.GetSunOccluderOpacity(sampleXInt, sampleYInt);
            
            if (occlusion > 0.5f)
            {
                rayBlocked = true;
                // UMA VEZ bloqueado, todo ponto posterior está em sombra
                break;
            }
        }
        
        // 5. Escrever resultado para este raio
        // Todos os samples desta linha recebem o mesmo resultado (bloqueado ou não)
        for (float depth = 0; depth <= occlusionCtx.MaxShadowDistance; depth += 0.5f)
        {
            float sampleX = rayStartSampleX + rayStepX * depth;
            float sampleY = rayStartSampleY + rayStepY * depth;
            
            int sampleXInt = (int)sampleX;
            int sampleYInt = (int)sampleY;
            
            if (!IsValidSample(sampleXInt, sampleYInt, resources))
                continue;
            
            int sampleIdx = sampleYInt * resources.LightSampleWidth + sampleXInt;
            sunVisibilityMapOutput[sampleIdx] = rayBlocked ? 0.0f : 1.0f;
        }
    }
}
```

### Características

✓ **O(numberOfLightSamples)**: Uma passagem por raio, sem loops internos por raio  
✓ **Geometria real**: BackgroundWall não bloqueia (queryOccluderField decide)  
✓ **Sombras precisas**: Primeira oclusão interrompe raio  
✓ **Sem projeção de cone**: Sombras não se alargam artificialmente  
✓ **Suporta sol diagonal**: Funciona com qualquer sunDirection  
✓ **Suporta wrapping**: Clipa ao mundo com segurança

---

## 4. CONDIÇÃO DE ENTRADA SOLAR: Chunks com Transmittance

### Problema Anterior

Sair da `ActiveRegion` não significa atingir céu aberto. Oclusores fora da região podem projetar sombra.

### Solução: Cache de Chunks

```csharp
struct SolarChunkExposure
{
    public int chunkX, chunkY;
    public int lastUpdateFrame;
    
    /// Transmittância ENTRANTE (borda solar do chunk)
    /// [0]: zero, chunk totalmente bloqueado pela entrada
    /// [1]: chunk parcialmente iluminado
    public float[] incomingTransmittance;  // [num_perpendicular_samples]
    
    /// Transmittância SAINTE (borda oposta/frente)
    /// O que sai da frente do chunk para o próximo chunk
    public float[] outgoingTransmittance;  // [num_perpendicular_samples]
}

class SolarExposureChunkCache
{
    private Dictionary<(int, int), SolarChunkExposure> chunkCache;
    
    public SolarChunkExposure GetOrComputeChunk(int chunkX, int chunkY, int frame)
    {
        if (chunkCache.TryGetValue((chunkX, chunkY), out var cached) &&
            cached.lastUpdateFrame == frame)
            return cached;
        
        // Compute: processar este chunk
        var exposure = ComputeChunkExposure(chunkX, chunkY);
        chunkCache[(chunkX, chunkY)] = exposure;
        return exposure;
    }
    
    private SolarChunkExposure ComputeChunkExposure(int chunkX, int chunkY)
    {
        // 1. Iterar cada linha solar perpendicular que atravessa este chunk
        // 2. Para cada linha, traçar raio UP-STREAM de entrada
        // 3. Determinar se linha já estava bloqueada na entrada
        // 4. Calcular outgoing: incoming - perdas do chunk
        
        var exposure = new SolarChunkExposure
        {
            chunkX = chunkX,
            chunkY = chunkY,
            lastUpdateFrame = Time.CurrentFrame,
            incomingTransmittance = new float[numPerpLines],
            outgoingTransmittance = new float[numPerpLines]
        };
        
        for (int perpIdx = 0; perpIdx < numPerpLines; perpIdx++)
        {
            // Determinar incoming de chunks a MONTANTE
            float incoming = GetIncomingFromUpstreamChunk(chunkX, chunkY, perpIdx);
            
            // Processar oclusores neste chunk
            float blockage = ComputeBlockageInChunk(chunkX, chunkY, perpIdx);
            
            // Calcular outgoing
            exposure.outgoingTransmittance[perpIdx] = incoming * (1.0f - blockage);
        }
        
        return exposure;
    }
}
```

### Validação de Entrada Solar

```csharp
// Ao começar raios paralelos:

// 1. Determinar chunk de ENTRADA (onde sun rays começam)
int sunEntryChunkX = ComputeEntryChunkX(sunDirection);
int sunEntryChunkY = ComputeEntryChunkY(sunDirection);

// 2. Iterar chunks NA ORDEM DA DIREÇÃO SOLAR
// Se sunDir aponta para baixo-direita, processar chunks nessa ordem
foreach (var chunkPath in GetChunkTraversalOrder(sunEntryChunkX, sunEntryChunkY, sunDirection))
{
    var chunkExposure = GetOrComputeChunk(chunkPath.X, chunkPath.Y, currentFrame);
    
    // Usa cached transmittance para acelerar cálculos
    // Raios já sabem se chegaram bloqueados
}

// 3. Invalidar cache quando:
// - sunDirection muda além de threshold quantizado (ex: 2°)
// - geometria muda (tile changes)
// - oclusores dinâmicos se movem

InvalidationReason invalidate = CheckInvalidation(lastSunDir, currentSunDir, lastFrameChanges);
if (invalidate != InvalidationReason.None)
    chunkCache.Clear();
```

---

## 5. BACKGROUND GLOW: Preservando Fonte + Até 3 Tiles

### Separação de Fontes Simultâneas

```csharp
struct BackgroundApertureEntry
{
    public int sampleX, sampleY;
    public float intensity;
    public Vector3 color;
    public ApertureType type;  // SkyAmbient ou DirectSun
    public float distance;     // Para ordenação
}

enum ApertureType
{
    SkyAmbientOnly,      // Céu, sem raio solar direto
    DirectSunActive,     // Raio solar direto entrando
}

public void ComputeBackgroundGlow(
    float[] sunVisibilityField,
    Vector2 sunDirection,
    Vector3 sunColor,
    float sunIntensity,
    Vector3 skyColor,
    float skyIntensity,
    SceneWorldClassifier classifier,
    LightingGridResources resources,
    Color[] backgroundGlowBuffer)
{
    // 1. Identificar TODAS as aberturas geométricas
    List<BackgroundApertureEntry> entries = new();
    Queue<int> bfsQueue = new();
    float[] distanceMap = new float[resources.LightSampleWidth * resources.LightSampleHeight];
    Array.Fill(distanceMap, -1f);
    
    for (int sampleIdx = 0; sampleIdx < resources.TotalSamples; sampleIdx++)
    {
        int sampleX = sampleIdx % resources.LightSampleWidth;
        int sampleY = sampleIdx / resources.LightSampleWidth;
        
        int tileX = (int)(sampleX / SamplesPerAxis);
        int tileY = (int)(sampleY / SamplesPerAxis);
        
        if (classifier.ClassifyTile(tileX, tileY) != TileClassification.VisibleBackground)
            continue;
        
        // Check adjacência a OpenAtmosphere
        bool isEntry = false;
        for (int dy = -1; dy <= 1; dy++)
        {
            for (int dx = -1; dx <= 1; dx++)
            {
                if (classifier.ClassifyTile(tileX + dx, tileY + dy) == TileClassification.EmptyBackground)
                {
                    isEntry = true;
                    break;
                }
            }
            if (isEntry) break;
        }
        
        if (!isEntry)
            continue;
        
        // 2. Classificar tipo de entrada
        float sunVis = sunVisibilityField[sampleIdx];
        var entryType = sunVis > 0.1f ? ApertureType.DirectSunActive : ApertureType.SkyAmbientOnly;
        
        // 3. Calcular intensidade da semente
        Vector3 seedColor;
        float seedIntensity;
        
        if (entryType == ApertureType.DirectSunActive)
        {
            // Sun + Sky
            Vector2 normalAtEntry = EstimateBackgroundNormal(tileX, tileY, classifier);
            float sunIncidence = Math.Max(0, Vector2.Dot(normalAtEntry, sunDirection));
            
            seedColor = (skyColor * skyIntensity * 0.2f) +  // Sky base
                       (sunColor * sunIntensity * sunVis * sunIncidence * 0.8f);  // Direct sun
            seedIntensity = Math.Max(seedColor.X, Math.Max(seedColor.Y, seedColor.Z));
        }
        else
        {
            // Só céu
            seedColor = skyColor * (skyIntensity * 0.15f);
            seedIntensity = Math.Max(seedColor.X, Math.Max(seedColor.Y, seedColor.Z));
        }
        
        if (seedIntensity < 0.01f)
            continue;
        
        var entry = new BackgroundApertureEntry
        {
            sampleX = sampleX,
            sampleY = sampleY,
            intensity = seedIntensity,
            color = seedColor,
            type = entryType,
            distance = 0
        };
        
        entries.Add(entry);
        bfsQueue.Enqueue(sampleIdx);
        distanceMap[sampleIdx] = 0;
    }
    
    // 4. BFS limited to ~3 tiles (6 samples em 2×2 grid)
    int maxDistanceSamples = 6;
    
    // Manter referência à melhor FONTE por amostra
    var bestSourcePerSample = new int[resources.TotalSamples];
    Array.Fill(bestSourcePerSample, -1);
    for (int i = 0; i < entries.Count; i++)
        bestSourcePerSample[entries[i].sampleY * resources.LightSampleWidth + entries[i].sampleX] = i;
    
    while (bfsQueue.Count > 0)
    {
        int currentIdx = bfsQueue.Dequeue();
        int curSampleY = currentIdx / resources.LightSampleWidth;
        int curSampleX = currentIdx % resources.LightSampleWidth;
        float curDist = distanceMap[currentIdx];
        
        if (curDist >= maxDistanceSamples)
            continue;
        
        // 4-connected neighbors
        int[] nDx = { -1, 1, 0, 0 };
        int[] nDy = { 0, 0, -1, 1 };
        
        for (int dir = 0; dir < 4; dir++)
        {
            int nSampleX = curSampleX + nDx[dir];
            int nSampleY = curSampleY + nDy[dir];
            
            if (nSampleX < 0 || nSampleX >= resources.LightSampleWidth ||
                nSampleY < 0 || nSampleY >= resources.LightSampleHeight)
                continue;
            
            int nIdx = nSampleY * resources.LightSampleWidth + nSampleX;
            
            if (distanceMap[nIdx] >= 0)
                continue;  // Already visited
            
            int nTileX = (int)(nSampleX / SamplesPerAxis);
            int nTileY = (int)(nSampleY / SamplesPerAxis);
            
            if (classifier.ClassifyTile(nTileX, nTileY) != TileClassification.VisibleBackground)
                continue;  // Não propaga fora de VisibleBackground
            
            float newDist = curDist + 1.0f;
            distanceMap[nIdx] = newDist;
            
            // Registrar qual fonte melhor alimenta este sample
            int sourceIdx = bestSourcePerSample[currentIdx];
            if (sourceIdx >= 0)
                bestSourcePerSample[nIdx] = sourceIdx;
            
            bfsQueue.Enqueue(nIdx);
        }
    }
    
    // 5. Aplicar glow com falloff dependente da fonte
    for (int sampleIdx = 0; sampleIdx < resources.TotalSamples; sampleIdx++)
    {
        float dist = distanceMap[sampleIdx];
        
        if (dist < 0)
            continue;  // Not reached
        
        int sourceIdx = bestSourcePerSample[sampleIdx];
        if (sourceIdx < 0)
            continue;  // Sem fonte
        
        var source = entries[sourceIdx];
        
        // Raio DEPENDE DA INTENSIDADE DA FONTE
        float maxRadius = Lerp(
            2.0f,  // Mínimo: céu só
            6.0f,  // Máximo: 3 tiles
            Math.Clamp(source.intensity, 0, 1));
        
        float t = Math.Clamp(dist / maxRadius, 0, 1);
        float falloff = 1.0f - SmoothStep(0, 1, t);
        
        Vector3 glowColor = source.color * falloff;
        backgroundGlowBuffer[sampleIdx] = new Color(
            (byte)(Math.Clamp(glowColor.X, 0, 1) * 255),
            (byte)(Math.Clamp(glowColor.Y, 0, 1) * 255),
            (byte)(Math.Clamp(glowColor.Z, 0, 1) * 255),
            255);
    }
}
```

---

## 6. FOREGROUND SURFACE LIGHT (~0.5 Tile)

### Amostragem Corrigida

```csharp
public void ComputeForegroundSurfaceLight(
    float[] sunVisibilityField,
    Vector2 sunDirection,
    Vector3 sunColor,
    float sunIntensity,
    OccluderField occluderField,
    SceneWorldClassifier classifier,
    LightingGridResources resources,
    Color[] foregroundSurfaceBuffer)
{
    const float SampleEpsilon = 0.01f;  // Distância fora do sólido
    
    for (int sampleIdx = 0; sampleIdx < resources.TotalSamples; sampleIdx++)
    {
        int sampleY = sampleIdx / resources.LightSampleWidth;
        int sampleX = sampleIdx % resources.LightSampleWidth;
        
        float tileX = sampleX / SamplesPerAxis;
        float tileY = sampleY / SamplesPerAxis;
        
        int tileXInt = (int)tileX;
        int tileYInt = (int)tileY;
        
        // 1. Check sólido foreground
        if (classifier.ClassifyTile(tileXInt, tileYInt) != TileClassification.SolidForeground)
        {
            foregroundSurfaceBuffer[sampleIdx] = Color.Black;
            continue;
        }
        
        // 2. Check superfície (adjacente a não-sólido)
        if (!IsAtSurfaceEdge(tileXInt, tileYInt, classifier))
        {
            foregroundSurfaceBuffer[sampleIdx] = Color.Black;
            continue;
        }
        
        // 3. Estimar normal da superfície
        Vector2 surfaceNormal = EstimateSurfaceNormal(tileXInt, tileYInt, classifier);
        if (surfaceNormal.Length() < 0.01f)
        {
            foregroundSurfaceBuffer[sampleIdx] = Color.Black;
            continue;
        }
        
        // 4. Criar sample point FORA do sólido
        float tileXCenter = tileXInt + 0.5f;
        float tileYCenter = tileYInt + 0.5f;
        
        // Face point: na borda do tile
        float faceX = tileXCenter + surfaceNormal.X * 0.5f;
        float faceY = tileYCenter + surfaceNormal.Y * 0.5f;
        
        // Sample point: um pouco fora
        float samplePointX = faceX + surfaceNormal.X * SampleEpsilon;
        float samplePointY = faceY + surfaceNormal.Y * SampleEpsilon;
        
        // Converter para coordenadas de amostra
        int samplePointSampleX = (int)(samplePointX * SamplesPerAxis);
        int samplePointSampleY = (int)(samplePointY * SamplesPerAxis);
        
        // 5. Query SUN visibility neste ponto EXTERNO
        if (!IsValidSample(samplePointSampleX, samplePointSampleY, resources))
        {
            foregroundSurfaceBuffer[sampleIdx] = Color.Black;
            continue;
        }
        
        float visibility = sunVisibilityField[samplePointSampleY * resources.LightSampleWidth + samplePointSampleX];
        
        if (visibility < 0.01f)
        {
            foregroundSurfaceBuffer[sampleIdx] = Color.Black;
            continue;
        }
        
        // 6. Calcular incidência solar
        // sunDirection aponta para o sol, então usamos como-está
        float incidence = Math.Max(0, Vector2.Dot(surfaceNormal, sunDirection));
        
        // 7. Combinar: incidência × visibilidade × intensidade
        float illumination = incidence * visibility * sunIntensity;
        
        Vector3 litColor = sunColor * illumination;
        foregroundSurfaceBuffer[sampleIdx] = new Color(
            (byte)(Math.Clamp(litColor.X, 0, 1) * 255),
            (byte)(Math.Clamp(litColor.Y, 0, 1) * 255),
            (byte)(Math.Clamp(litColor.Z, 0, 1) * 255),
            255);
    }
}
```

---

## 7. SKY AMBIENT vs DIRECT SUNLIGHT (Separados)

```csharp
public void ComputeBackgroundReception_Separated(
    float[] sunVisibilityField,
    Vector2 sunDirection,
    Vector3 sunColor,
    float sunIntensity,
    Vector3 skyColor,
    float skyIntensity,
    SceneWorldClassifier classifier,
    LightingGridResources resources,
    Color[] skyAmbientBuffer,
    Color[] directSunBuffer)
{
    // === SKY AMBIENT ===
    // Sempre presente, independente de SunDirection
    // Fornece base suave em qualquer abertura
    
    for (int sampleIdx = 0; sampleIdx < resources.TotalSamples; sampleIdx++)
    {
        int sampleY = sampleIdx / resources.LightSampleWidth;
        int sampleX = sampleIdx % resources.LightSampleWidth;
        
        int tileX = (int)(sampleX / SamplesPerAxis);
        int tileY = (int)(sampleY / SamplesPerAxis);
        
        if (classifier.ClassifyTile(tileX, tileY) == TileClassification.VisibleBackground)
        {
            // Background sempre recebe céu suave
            Vector3 skyAmbient = skyColor * (skyIntensity * 0.2f);
            skyAmbientBuffer[sampleIdx] = new Color(
                (byte)(Math.Clamp(skyAmbient.X, 0, 1) * 255),
                (byte)(Math.Clamp(skyAmbient.Y, 0, 1) * 255),
                (byte)(Math.Clamp(skyAmbient.Z, 0, 1) * 255),
                255);
        }
        else
        {
            skyAmbientBuffer[sampleIdx] = Color.Black;
        }
    }
    
    // === DIRECT SUNLIGHT ===
    // Só onde visibilidade solar > threshold
    // Depende de SunDirection e sombra
    
    for (int sampleIdx = 0; sampleIdx < resources.TotalSamples; sampleIdx++)
    {
        float visibility = sunVisibilityField[sampleIdx];
        
        if (visibility < 0.01f)
        {
            directSunBuffer[sampleIdx] = Color.Black;
            continue;
        }
        
        int sampleY = sampleIdx / resources.LightSampleWidth;
        int sampleX = sampleIdx % resources.LightSampleWidth;
        
        int tileX = (int)(sampleX / SamplesPerAxis);
        int tileY = (int)(sampleY / SamplesPerAxis);
        
        if (classifier.ClassifyTile(tileX, tileY) == TileClassification.VisibleBackground)
        {
            // Glow solar direto (mais brilhante que ambient)
            Vector3 sunReception = sunColor * (sunIntensity * visibility * 0.6f);
            directSunBuffer[sampleIdx] = new Color(
                (byte)(Math.Clamp(sunReception.X, 0, 1) * 255),
                (byte)(Math.Clamp(sunReception.Y, 0, 1) * 255),
                (byte)(Math.Clamp(sunReception.Z, 0, 1) * 255),
                255);
        }
        else
        {
            directSunBuffer[sampleIdx] = Color.Black;
        }
    }
}

// Composição final:
// BackgroundReception = SkyAmbient + DirectSun (aditivo)
```

---

## 8. SUN SHAFT SYSTEM (Em Feixes, não por Amostra)

```csharp
class SunShaftSystem
{
    public struct SunShaftBeam
    {
        public Vector2 rayOrigin;
        public Vector2 rayDirection;
        public Vector3 color;
        public float intensity;
        public float width;  // Largura do feixe em tiles
    }
    
    public void ComputeSunShafts(
        float[] sunVisibilityField,
        Vector2 sunDirection,
        Vector3 sunColor,
        float sunIntensity,
        OccluderField occluderField,
        SceneWorldClassifier classifier,
        LightingGridResources resources,
        Color[] sunShaftBuffer)
    {
        // 1. Não renderizar CADA amostra, mas FEIXES
        // Um feixe = múltiplas amostras paralelas que compartilham origem e direção
        
        List<SunShaftBeam> beams = new();
        
        // 2. Iterar cada linha solar paralela
        Vector2 rayDir = -sunDirection;
        Vector2 perpDir = new Vector2(-rayDir.Y, rayDir.X);
        
        int numPerpLines = (int)(resources.LightSampleWidth + resources.LightSampleHeight);
        for (int perpIdx = 0; perpIdx < numPerpLines; perpIdx++)
        {
            Vector2 rayOrigin = (Vector2)resources.Region.Location +
                               perpDir * (perpIdx - numPerpLines / 2) * 0.5f;
            
            // 3. AGRUPAR raios vizinhos que SÃO CONTINUAMENTE iluminados
            // (não há oclusor bloqueando entre eles)
            
            bool isRayVisible = true;
            float rayVisibilityStart = 0;
            
            for (float depth = 0; depth <= MaxShaftDepth; depth += 0.5f)
            {
                Vector2 checkPos = rayOrigin + rayDir * depth;
                int sampleX = (int)(checkPos.X * SamplesPerAxis);
                int sampleY = (int)(checkPos.Y * SamplesPerAxis);
                
                if (!IsValidSample(sampleX, sampleY, resources))
                    break;
                
                float occlusion = occluderField.GetSunOccluderOpacity(sampleX, sampleY);
                bool nowVisible = occlusion < 0.5f;
                
                // 4. Detectar transição: visível → bloqueado
                if (isRayVisible && !nowVisible)
                {
                    // Criar feixe do ponto anterior até aqui
                    beams.Add(new SunShaftBeam
                    {
                        rayOrigin = rayOrigin,
                        rayDirection = rayDir,
                        color = sunColor,
                        intensity = sunIntensity,
                        width = 0.5f  // Largura do feixe
                    });
                    
                    isRayVisible = false;
                }
                else if (!isRayVisible && nowVisible)
                {
                    isRayVisible = true;
                }
            }
            
            // 5. Se raio terminou iluminado, criar feixe até fim
            if (isRayVisible)
            {
                beams.Add(new SunShaftBeam
                {
                    rayOrigin = rayOrigin,
                    rayDirection = rayDir,
                    color = sunColor,
                    intensity = sunIntensity,
                    width = 0.5f
                });
            }
        }
        
        // 6. Renderizar cada feixe (não cada amostra)
        foreach (var beam in beams)
        {
            RenderSunShaftBeam(beam, sunShaftBuffer, resources);
        }
    }
    
    private void RenderSunShaftBeam(
        SunShaftBeam beam,
        Color[] shaftBuffer,
        LightingGridResources resources)
    {
        // Renderizar feixe com suavização perpendicular curta
        // Gaussiana 1-2 tiles de raio para borda suave
        
        for (float depth = 0; depth <= MaxShaftDepth; depth += 0.5f)
        {
            Vector2 rayPoint = beam.rayOrigin + beam.rayDirection * depth;
            
            // Aplicar gaussiana perpendicular (perpDir)
            Vector2 perpDir = new Vector2(-beam.rayDirection.Y, beam.rayDirection.X);
            for (float perpDist = -beam.width; perpDist <= beam.width; perpDist += 0.25f)
            {
                Vector2 samplePoint = rayPoint + perpDir * perpDist;
                int sampleX = (int)(samplePoint.X * SamplesPerAxis);
                int sampleY = (int)(samplePoint.Y * SamplesPerAxis);
                
                if (!IsValidSample(sampleX, sampleY, resources))
                    continue;
                
                float gaussianFalloff = GaussianFalloff(perpDist, beam.width);
                float shaftIntensity = beam.intensity * gaussianFalloff;
                
                int idx = sampleY * resources.LightSampleWidth + sampleX;
                Vector3 shaftColor = beam.color * shaftIntensity;
                
                shaftBuffer[idx] = new Color(
                    (byte)Math.Min(255, shaftBuffer[idx].R + (int)(shaftColor.X * 255)),
                    (byte)Math.Min(255, shaftBuffer[idx].G + (int)(shaftColor.Y * 255)),
                    (byte)Math.Min(255, shaftBuffer[idx].B + (int)(shaftColor.Z * 255)),
                    255);
            }
        }
    }
}
```

---

## 9. LOCAL LIGHT OCCLUSION (Distância Segura)

```csharp
public struct LocalLightOcclusionMap
{
    public Vector2 lightPosition;
    public float maxRange;
    public int angularResolution;  // Mínimo 128, preferível 256
    
    public float[] occluderDistance;  // [ray] = distância ao primeiro oclusor
    public int lastUpdateFrame;
    public Vector2 lastLightPosition;
}

public LocalLightOcclusionMap ComputeOcclusionMap(
    Vector2 lightPos,
    float maxRange,
    OccluderField occluderField,
    LightingGridResources resources,
    int angularResolution = 256)
{
    var map = new LocalLightOcclusionMap
    {
        lightPosition = lightPos,
        maxRange = maxRange,
        angularResolution = angularResolution,
        occluderDistance = new float[angularResolution],
        lastUpdateFrame = Time.CurrentFrame,
        lastLightPosition = lightPos
    };
    
    for (int rayIdx = 0; rayIdx < angularResolution; rayIdx++)
    {
        float angle = (rayIdx / (float)angularResolution) * MathF.PI * 2f;
        Vector2 rayDir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        
        float occluderDist = maxRange;
        
        for (float dist = 0.5f; dist <= maxRange; dist += 0.5f)
        {
            Vector2 checkPos = lightPos + rayDir * dist;
            
            int sampleX = (int)(checkPos.X * SamplesPerAxis);
            int sampleY = (int)(checkPos.Y * SamplesPerAxis);
            
            if (sampleX < 0 || sampleX >= resources.LightSampleWidth ||
                sampleY < 0 || sampleY >= resources.LightSampleHeight)
                break;
            
            float occlusion = occluderField.GetLocalLightOccluderOpacity(sampleX, sampleY);
            
            if (occlusion > 0.5f)
            {
                occluderDist = dist - 0.25f;
                break;
            }
        }
        
        map.occluderDistance[rayIdx] = occluderDist;
    }
    
    return map;
}

public float QueryLocalLightOcclusion(
    LocalLightOcclusionMap map,
    Vector2 pointPos)
{
    Vector2 relative = pointPos - map.lightPosition;
    float distance = relative.Length();
    
    if (distance > map.maxRange)
        return 0;
    
    float angle = MathF.Atan2(relative.Y, relative.X);
    if (angle < 0) angle += MathF.PI * 2f;
    
    float sectorAngleF = (angle / (MathF.PI * 2f)) * map.angularResolution;
    int sector = (int)sectorAngleF % map.angularResolution;
    int sectorNext = (sector + 1) % map.angularResolution;
    float blend = sectorAngleF - sector;
    
    // === INTERPOLAÇÃO SEGURA: usar MÍNIMO para evitar vazamento em cantos ===
    float occluderDistAtAngle = Math.Min(
        map.occluderDistance[sector],
        map.occluderDistance[sectorNext]);
    // Se quer maior oclusão: Math.Max (mais conservador)
    
    // Soft penumbra
    const float EPSILON = 0.1f;
    const float PENUMBRA_WIDTH = 0.5f;
    
    float visibility;
    if (distance < occluderDistAtAngle - EPSILON)
        visibility = 1.0f;  // Completamente iluminado
    else if (distance > occluderDistAtAngle + PENUMBRA_WIDTH)
        visibility = 0.0f;  // Totalmente na sombra
    else
        visibility = 1.0f - SmoothStep(0, 1, 
            (distance - (occluderDistAtAngle - EPSILON)) / (PENUMBRA_WIDTH + EPSILON));
    
    return visibility;
}
```

---

## 10. CÁLCULOS EM FLOAT/VECTOR3 (Não Acumular em Color)

### Problema Anterior

Acumular iluminação diretamente em `Color` (8-bit) causa perda de precisão:

```csharp
// ERRADO:
color[idx] += new Color(R, G, B);  // Clipping early, perda de dados

// CERTO:
Vector3 light = ...;
lightAccumulator[idx] += light;     // Float acumula sem perda
```

### Padrão Correto

```csharp
// Buffer de acumulação FLOAT durante cálculos
class LightingCalculationBuffers
{
    // Todos em Vector3 (ou float[] separados para R, G, B)
    public Vector3[] ambientAccumulator;
    public Vector3[] glowAccumulator;
    public Vector3[] sunAccumulator;
    public Vector3[] localAccumulator;
    public Vector3[] shaftAccumulator;
    public Vector3[] finalAccumulator;
}

public void ComputeAllLighting(LightingCalculationBuffers buffers, ...)
{
    // 1. Computar cada camada em Vector3
    ComputeAmbient(buffers.ambientAccumulator, ...);
    ComputeGlow(buffers.glowAccumulator, ...);
    ComputeSun(buffers.sunAccumulator, ...);
    ComputeLocal(buffers.localAccumulator, ...);
    
    // 2. Compor em Vector3
    for (int i = 0; i < buffers.finalAccumulator.Length; i++)
    {
        buffers.finalAccumulator[i] =
            buffers.ambientAccumulator[i] +
            buffers.glowAccumulator[i] +
            buffers.sunAccumulator[i] +
            buffers.localAccumulator[i] +
            buffers.shaftAccumulator[i];
        
        // Clamp apenas no final
        buffers.finalAccumulator[i] = Vector3.Clamp(
            buffers.finalAccumulator[i],
            Vector3.Zero,
            Vector3.One);
    }
    
    // 3. Converter para Color APENAS para output
    Color[] finalBuffer = new Color[buffers.finalAccumulator.Length];
    for (int i = 0; i < buffers.finalAccumulator.Length; i++)
    {
        Vector3 value = buffers.finalAccumulator[i];
        finalBuffer[i] = new Color(
            (byte)(value.X * 255),
            (byte)(value.Y * 255),
            (byte)(value.Z * 255),
            255);
    }
    
    // 4. Upload final buffer para GPU texture
    lightingTexture.SetData(finalBuffer);
}
```

### Validação de Overflow

```csharp
[Conditional("DEBUG")]
private void ValidateLightingRange(Vector3[] buffer)
{
    for (int i = 0; i < buffer.Length; i++)
    {
        if (buffer[i].X > 1.01f || buffer[i].Y > 1.01f || buffer[i].Z > 1.01f)
            Console.WriteLine($"[Warning] Light overflow at sample {i}: {buffer[i]}");
    }
}
```

---

## RESUMO: CORREÇÕES IMPLEMENTADAS

| # | Área | Antes | Depois |
|---|------|-------|--------|
| 1 | Terminologia | "Resolução 0.25 tile" | SamplesPerAxis=2, SamplesPerTile=4, SampleSpacingTiles=0.5 |
| 2 | OccluderField | Um campo único | sunOpacity + localLightOpacity separados |
| 3 | Raios Solares | Projeção de cone | Raios paralelos verdadeiros, O(samples) |
| 4 | Entrada Solar | Sair da região = visível | Chunks com incomingTransmittance |
| 5 | Background Glow | Seeds[0] genérico | Múltiplas fontes preservadas, raio ~3 tiles |
| 6 | Foreground Surface | Sample no centro | Sample fora + epsilon |
| 7 | Sky vs Sun | Misturado | Separados: ambient + direct |
| 8 | Sun Shafts | Cada amostra | Feixes agrupados |
| 9 | Oclusão Local | Interpolação linear | Min() para evitar cantos |
| 10 | Acumulação | Color 8-bit | Vector3 float até final |

---

**Pronto para Fase 1: Implementação Foundation GPU isolada.**
