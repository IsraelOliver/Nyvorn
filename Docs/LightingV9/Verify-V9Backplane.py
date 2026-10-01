"""Compare captured runtime evidence. Run with the bundled Python (Pillow required).
Usage: python Docs/LightingV9/Verify-V9Backplane.py [evidence-root]
Does not modify PNGs. Exit nonzero on an invariant regression.
"""
import hashlib
import json
import sys
from pathlib import Path
from PIL import Image, ImageChops, ImageDraw

root = Path(sys.argv[1] if len(sys.argv) > 1 else "screenshots/v9-gameplay-probe/sky-backplane")
before, after, candidate = (root / p for p in ("reference", "reference-after", "capture"))
result = {"default_regressions": [], "artificial_regressions": [], "default_states": 0,
          "candidate_states": 0, "pixel_differences_outside_player": {}, "candidate_checks": ""}

def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest()

for path in sorted(after.glob("*_metrics.json")):
    name = path.name.removesuffix("_metrics.json")
    a = json.loads(path.read_text(encoding="utf-8-sig"))
    bpath = before / path.name
    if bpath.exists():
        b = json.loads(bpath.read_text(encoding="utf-8-sig"))
        result["default_states"] += 1
        if a["hashes"] != b["hashes"] or a["skyGlow"]["hash"] != b["skyGlow"]["hash"]:
            result["default_regressions"].append(name + ": float hashes")
        for diagnostic in ("irradiance-diagnostic", "natural-diagnostic", "artificial-diagnostic", "sky-classes", "sky-glow", "fg-sky-response", "sky-visible"):
            file = f"{name}_{diagnostic}.png"
            if digest(before / file) != digest(after / file):
                result["default_regressions"].append(file)
        for suffix in ("final", "albedo"):
            file = f"{name}_{suffix}.png"
            diff = ImageChops.difference(Image.open(before / file).convert("RGB"), Image.open(after / file).convert("RGB"))
            # Player faces the real mouse even in scripted captures. Exclude only a conservative sprite rectangle.
            draw = ImageDraw.Draw(diff)
            for m in (a, b):
                x = (m["player"]["x"] - m["viewTopLeft"]["x"]) * m["zoom"]
                y = (m["player"]["y"] - m["viewTopLeft"]["y"]) * m["zoom"]
                draw.rectangle((x - 48, y - 80, x + 48, y + 16), fill=(0, 0, 0))
            count = sum(p != (0, 0, 0) for p in diff.get_flattened_data())
            result["pixel_differences_outside_player"][file] = count
            if count:
                result["default_regressions"].append(file + ": pixels")
    cpath = candidate / path.name
    if cpath.exists():
        c = json.loads(cpath.read_text(encoding="utf-8-sig"))
        result["candidate_states"] += 1
        if a["field"]["origin"] != c["field"]["origin"] or a["hashes"]["artificial"] != c["hashes"]["artificial"]:
            result["artificial_regressions"].append(name)
        file = name + "_artificial-diagnostic.png"
        if digest(after / file) != digest(candidate / file):
            result["artificial_regressions"].append(file)

checks = (candidate / "checks.txt").read_text(encoding="utf-8-sig")
result["candidate_checks"] = checks.splitlines()[-1]
reference_checks = (after / "checks.txt").read_text(encoding="utf-8-sig")
result["reference_checks"] = reference_checks.splitlines()[-1]
result["passed"] = (result["default_states"] == len(list(before.glob("*_metrics.json"))) > 0
    and result["candidate_states"] == len(list(candidate.glob("*_metrics.json"))) > 0) and not (
    result["default_regressions"] or result["artificial_regressions"] or "\nFAIL " in checks or "\nFAIL " in reference_checks)
(root / "comparison.json").write_text(json.dumps(result, indent=2), encoding="utf-8")
print(json.dumps({k: v for k, v in result.items() if k != "pixel_differences_outside_player"}, indent=2))
names = sorted(p.name.removesuffix("_metrics.json") for p in candidate.glob("*_metrics.json") if (after / p.name).exists())
gallery = """<!doctype html><html lang="pt-BR"><meta charset="utf-8"><title>V9 · Sky Backplane</title>
<meta name="viewport" content="width=device-width, initial-scale=1">
<style>body{background:#11141b;color:#e2e7ef;font:16px system-ui;margin:32px}h1{font-size:28px}p{max-width:1000px;line-height:1.5;color:#b8c4d4}
select,button{font:inherit;padding:9px;background:#232e3e;color:white;border:1px solid #64748b;border-radius:5px;margin:5px}
.grid{display:grid;grid-template-columns:1fr 1fr;gap:16px}img{display:block;width:100%;image-rendering:pixelated;background:black}
figure{margin:12px 0}figcaption{margin:12px 0;color:#b8d4fc}a{color:#a5cdfc}@media(max-width:800px){.grid{grid-template-columns:1fr}body{margin:12px}}</style>
<h1>V9 · Sky Backplane</h1><p>Comparação de PNGs originais do runtime, sem ajuste de brilho. O modelo atual continua padrão.
O experimento troca exposição por linha de visada pela presença de FG/BG, com uma curva contínua de profundidade. Recintos sem BG continuam expostos, mesmo fechados em FG.</p>
<label>Cena <select id="scene"></select></label><label>Camada <select id="layer"><option value="final">Frame final</option><option value="albedo">Albedo / apresentação</option><option value="natural-diagnostic">Natural (diagnóstico)</option><option value="artificial-diagnostic">Artificial (diagnóstico)</option><option value="sky-visible">Céu visível (diagnóstico)</option><option value="sky-glow">Background Glow (diagnóstico)</option><option value="fg-sky-response">Resposta do FG (diagnóstico)</option></select></label>
<div class="grid"><figure><figcaption>Natural atual · exposição + aberturas</figcaption><a id="oldLink"><img id="old" alt="Modelo atual"></a></figure>
<figure><figcaption>Experimento · Sky Backplane</figcaption><a id="newLink"><img id="new" alt="Experimento Sky Backplane"></a></figure></div>
<p>As imagens de diagnóstico representam campos, não o frame final. Clique na imagem para ver os pixels originais. A resposta fraca de FG derivada do glow está nos arquivos <code>*_fg-from-glow.png</code> da pasta capture.</p>
<p><a href="comparison.json">Verificação numérica</a> · <a href="capture/checks.txt">Testes do experimento</a> · <a href="reference-after/checks.txt">Testes da referência</a></p>
<script>const names=__NAMES__;const scene=document.querySelector('#scene'),layer=document.querySelector('#layer');
for(const name of names){const o=new Option(name,name);scene.add(o)}scene.value='entrance-rim';
function show(){for(const [id,dir] of [['old','reference-after'],['new','capture']]){const path=dir+'/'+scene.value+'_'+layer.value+'.png';document.getElementById(id).src=path;document.getElementById(id+'Link').href=path}}
scene.onchange=layer.onchange=show;show();</script></html>"""
(root / "comparison.html").write_text(gallery.replace("__NAMES__", json.dumps(names)), encoding="utf-8")
sys.exit(0 if result["passed"] else 1)
