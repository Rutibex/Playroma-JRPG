#!/usr/bin/env python3
"""Static release checks for Playroma Text Studio Phase 14.
This supplements (never replaces) the .NET build/test pass.
"""
from __future__ import annotations
import json
from pathlib import Path
import xml.etree.ElementTree as ET

ROOT = Path(__file__).resolve().parents[2]
checks: list[tuple[str, bool, str]] = []

def check(name: str, condition: bool, detail: str = "") -> None:
    checks.append((name, bool(condition), detail))

def lexical_ok(text: str) -> bool:
    stack=[]; i=0; state='code'; verbatim=False; pairs={')':'(',']':'[','}':'{'}
    while i < len(text):
        c=text[i]; n=text[i+1] if i+1 < len(text) else ''
        if state=='code':
            if c=='/' and n=='/': state='line'; i+=2; continue
            if c=='/' and n=='*': state='block'; i+=2; continue
            if c=='@' and n=='"': state='string'; verbatim=True; i+=2; continue
            if c=='"': state='string'; verbatim=False; i+=1; continue
            if c=="'": state='char'; i+=1; continue
            if c in '([{': stack.append(c)
            elif c in ')]}':
                if not stack or stack[-1] != pairs[c]: return False
                stack.pop()
        elif state=='line':
            if c=='\n': state='code'
        elif state=='block':
            if c=='*' and n=='/': state='code'; i+=2; continue
        elif state=='string':
            if verbatim:
                if c=='"' and n=='"': i+=2; continue
                if c=='"': state='code'; i+=1; continue
            else:
                if c=='\\': i+=2; continue
                if c=='"': state='code'; i+=1; continue
        elif state=='char':
            if c=='\\': i+=2; continue
            if c=="'": state='code'; i+=1; continue
        i+=1
    return state not in ('block','string','char') and not stack

required = [
    'src/Playroma.Text.Core/Generation/AdvancedSearchModels.cs',
    'src/Playroma.Text.Core/AI/NeuralTextModels.cs',
    'src/Playroma.Text.Generators/Candidates/AdvancedCandidateSearchEngine.cs',
    'src/Playroma.Text.Generators/AI/ProcessNeuralTextProvider.cs',
    'src/Playroma.Text.Generators/Stages/NeuralTextStage.cs',
    'src/Playroma.Text.Authoring/Release/TextProjectDoctor.cs',
    'src/Playroma.Text.Editor.WinForms/Controls/AiProviderLabControl.cs',
    'src/Playroma.Text.Editor.WinForms/Controls/TextProjectDoctorControl.cs',
    'tests/Playroma.Text.Phase14.Tests/Playroma.Text.Phase14.Tests.csproj',
    'tests/Playroma.Text.Phase14.FuzzTests/Playroma.Text.Phase14.FuzzTests.csproj',
    'Playroma.Text.Phase14.slnf',
    'PlayromaTextAssets/Phase14/example-advanced-search.playtext.json',
    'PlayromaTextAssets/Phase14/example-optional-ai.playtext.json',
]
for rel in required: check('exists: '+rel, (ROOT/rel).exists())

for rel in ['Playroma.Text.Phase14.slnf', 'PlayromaTextAssets/Phase14/example-advanced-search.playtext.json', 'PlayromaTextAssets/Phase14/example-optional-ai.playtext.json', 'PlayromaTextAssets/Phase1/playtext.recipe.schema.json', 'PlayromaTextAssets/Phase6/playtext.candidate-pipeline.schema.json']:
    try: json.loads((ROOT/rel).read_text()); check('json: '+rel, True)
    except Exception as exc: check('json: '+rel, False, str(exc))

for p in [ROOT/'tests/Playroma.Text.Phase14.Tests/Playroma.Text.Phase14.Tests.csproj', ROOT/'tests/Playroma.Text.Phase14.FuzzTests/Playroma.Text.Phase14.FuzzTests.csproj']:
    try: ET.parse(p); check('xml: '+p.name, True)
    except Exception as exc: check('xml: '+p.name, False, str(exc))

engine=(ROOT/'src/Playroma.Text.Core/Generation/GenerationModels.cs').read_text()
check('engine is 1.0.0-phase14', '1.0.0-phase14' in engine)
catalog=(ROOT/'src/Playroma.Text.Generators/GeneratorCatalog.cs').read_text()
check('phase14 registry', 'CreatePhase14Registry' in catalog and 'NeuralTextStage' in catalog)
recipe_catalog=(ROOT/'src/Playroma.Text.Generators/BuiltInRecipeCatalog.cs').read_text()
check('phase14 recipes in catalog', 'BuiltInPhase14Recipes.All' in recipe_catalog)
ui=(ROOT/'src/Playroma.Text.Editor.WinForms/Controls/TextStudioControl.cs').read_text()
check('AI Providers UI', 'Optional AI Providers' in ui and 'OpenAiProviders' in ui)
check('Project Doctor UI', 'Project Doctor' in ui and 'OpenProjectDoctor' in ui)
check('keyboard hardening', 'ProcessCmdKey' in ui and 'Keys.Control | Keys.Enter' in ui)

for p in list((ROOT/'src/Playroma.Text.Core').rglob('*.cs')) + list((ROOT/'src/Playroma.Text.Generators').rglob('*.cs')) + list((ROOT/'src/Playroma.Text.Authoring').rglob('*.cs')) + list((ROOT/'src/Playroma.Text.Editor.WinForms').rglob('*.cs')) + list((ROOT/'tests/Playroma.Text.Phase14.Tests').rglob('*.cs')) + list((ROOT/'tests/Playroma.Text.Phase14.FuzzTests').rglob('*.cs')):
    check('lexical: '+str(p.relative_to(ROOT)), lexical_ok(p.read_text(errors='replace')))

sln=(ROOT/'Playroma.JRPG.sln').read_text(errors='replace')
check('phase14 regression project in solution', 'Playroma.Text.Phase14.Tests' in sln)
check('phase14 fuzz project in solution', 'Playroma.Text.Phase14.FuzzTests' in sln)

failed=[x for x in checks if not x[1]]
print(f'Phase 14 static checks: {len(checks)-len(failed)}/{len(checks)} passed')
for name, ok, detail in failed:
    print('FAIL:', name, detail)
raise SystemExit(1 if failed else 0)
