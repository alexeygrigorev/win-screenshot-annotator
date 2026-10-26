#!/usr/bin/env python3
"""Vendor the actual PocketShell annotate surface and apply only host adaptations."""
import hashlib, json, os, subprocess
from pathlib import Path
root = Path(__file__).resolve().parents[1]
core = Path(os.environ.get('POCKETSHELL_CORE', Path.home() / 'git/pocketshell-core'))
files = {
 'packages/ui/src/app/components/DoodleCanvas.vue': 'app/components/DoodleCanvas.vue',
 'packages/ui/src/app/useDoodleText.ts': 'app/useDoodleText.ts',
 'packages/ui/src/app/doodlePaint.ts': 'app/doodlePaint.ts',
 'packages/ui/src/app/doodleModel.ts': 'app/doodleModel.ts',
 'packages/ui/src/components/AppIcon.vue': 'components/AppIcon.vue',
 'packages/ui/src/tokens.css': 'tokens.css',
 **{f'src/shared/{n}.ts': f'shared/{n}.ts' for n in ['doodleGeometry','shortcuts','shortcutTable','composerAttachments']},
}
manifest = {'source': 'pocketshell-core', 'commit': subprocess.check_output(['git','-C',str(core),'rev-parse','HEAD'],text=True).strip(), 'files': {}}
for src, dest in files.items():
 raw = (core / src).read_text()
 manifest['files'][src] = hashlib.sha256(raw.encode()).hexdigest()
 if dest.endswith('doodleGeometry.ts'):
  raw = raw.replace('return Math.round(Math.max(strokeWidth * TEXT.sizeRatio, TEXT.minSize));', '// Alexey t1536u: preserve our 36px captions independently of stroke width.\n  return 36;')
 if dest.endswith('DoodleCanvas.vue'):
  raw = raw.replace('const MAX = 2048;', 'const MAX = Number.MAX_SAFE_INTEGER; // Keep original captured pixels.')
  raw = raw.replace('defineExpose({ requestClose });', 'defineExpose({ requestClose, commit, commitText, repaint, items, tool, pen, width, editing, undo, clear, loadBackdrop });')
  raw = raw.replace('        {{ saving ? \'Saving…\' : \'Attach\' }}', '        {{ saving ? \'Saving…\' : \'Copy\' }}')
  raw = raw.replace('      <button class="btn primary"', '      <button class="btn" @click="emit(\'save\')">Save PNG</button>\n      <button class="btn primary"')
  raw = raw.replace('  close: [];', '  close: [];\n  save: [];')
 target = root / 'ui/vendor' / dest
 target.parent.mkdir(parents=True,exist_ok=True)
 target.write_text(raw)
(root/'ui/vendor/provenance.json').write_text(json.dumps(manifest, indent=2)+'\n')
