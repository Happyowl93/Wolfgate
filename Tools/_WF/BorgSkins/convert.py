#!/usr/bin/env python3
"""Converts Bubberstation's tall (32x64) and wide (64x32) borg DMIs into borg subtype RSIs.

    python Tools/_WF/BorgSkins/convert.py <dir with the .dmi files> <Bubberstation commit sha>

Rewrites Resources/Textures/_WF/BorgSkins, the subtype and chassis prototypes and the chassis names.
Needs Pillow. The DMIs are modular_skyrat/modules/borgs/icons/{tallrobot,widerobot}*.dmi.
"""
import json
import math
import re
import shutil
import sys
from pathlib import Path

from PIL import Image

ROOT = Path(__file__).resolve().parents[3]
TEXTURES = ROOT / 'Resources/Textures/_WF/BorgSkins'
PROTOTYPES = ROOT / 'Resources/Prototypes/_WF/BorgSkins'
LOCALE = ROOT / 'Resources/Locale/en-US/_WF/BorgSkins'
SOURCE = 'https://github.com/Bubberstation/Bubberstation/blob/{sha}/modular_skyrat/modules/borgs/icons/{file}'

# Bubber department -> (folder and ID part, borg type, word used in the chassis name).
# Departments with no borg type here are offered under the generic type.
DEPTS = {
    'eng': ('Engineering', 'engineering', 'engineering'),
    'jani': ('Janitor', 'janitor', 'janitor'),
    'med': ('Medical', 'medical', 'medical'),
    'mine': ('Mining', 'mining', 'salvage'),
    'serv': ('Service', 'service', 'service'),
    'cargo': ('Cargo', 'generic', 'cargo'),
    'sec': ('Security', 'generic', 'security'),
    'pk': ('Peacekeeper', 'generic', 'peacekeeper'),
    'ninja': ('Ninja', 'generic', 'ninja'),
    'syndi': ('Syndicate', 'generic', 'syndicate'),
    'clown': ('Clown', 'generic', 'clown'),
}

# Borg type -> (body state, the type has a spriteBodyMovementState).
TYPES = {
    'generic': ('robot', False),
    'engineering': ('engineer', False),
    'mining': ('miner', True),
    'janitor': ('janitor', True),
    'medical': ('medical', True),
    'service': ('service', False),
}

# kind, department, skin name, icon state, state the eye and lamp overlays come from if not its own.
# Names and light keys follow Bubberstation's borg_skins lists.
SKINS = [
    ('tall', 'eng', 'Meka', 'mekaengi', None),
    ('tall', 'eng', 'K4T (Technician)', 'k4tengi', None),
    ('tall', 'eng', 'K4T (Hazard)', 'k4tengi_alt1', None),
    ('tall', 'eng', 'NiKA', 'fmekaeng', None),
    ('tall', 'eng', 'NiKO', 'mmekaeng', None),
    ('tall', 'eng', 'NiKO (Alt)', 'sfmekaengi', None),
    ('tall', 'jani', 'Meka', 'mekajani', None),
    ('tall', 'jani', 'K4T (Viscera)', 'k4tjani', None),
    ('tall', 'jani', 'K4T (Domestic)', 'k4tjani_alt1', None),
    ('tall', 'jani', 'NiKA', 'fmekajani', None),
    ('tall', 'jani', 'NiKO', 'mmekajani', None),
    ('tall', 'med', 'Meka', 'mekamed', None),
    ('tall', 'med', 'K4T (Doc)', 'k4tmed', None),
    ('tall', 'med', 'K4T (Field Medic)', 'k4tmed_alt1', None),
    ('tall', 'med', 'NiKA', 'fmekamed', None),
    ('tall', 'med', 'NiKO', 'mmekamed', None),
    ('tall', 'med', 'NiKO (Alt)', 'sfmekamed', None),
    ('tall', 'mine', 'Meka', 'mekamine', None),
    ('tall', 'mine', 'K4T (Rookie)', 'k4tmine', None),
    ('tall', 'mine', 'K4T (Veteran)', 'k4tmine_alt1', None),
    ('tall', 'mine', 'NiKA', 'fmekamine', None),
    ('tall', 'mine', 'NiKO', 'mmekamine', None),
    ('tall', 'mine', 'NiKA (Alt)', 'sfmekamine', None),
    ('tall', 'serv', 'Meka', 'mekaserve', None),
    ('tall', 'serv', 'Meka (Alt)', 'mekaserve_alt', 'mekaserve'),
    ('tall', 'serv', 'K4T (Barista)', 'k4tserve', None),
    ('tall', 'serv', 'K4T (80s)', 'k4tserve_alt1', None),
    ('tall', 'serv', 'K4T (Dispenser)', 'k4tserve_alt2', None),
    ('tall', 'serv', 'NiKA', 'fmekaserv', None),
    ('tall', 'serv', 'NiKO', 'mmekaserv', None),
    ('tall', 'serv', 'NiKO (Alt)', 'sfmekaserv', None),
    ('tall', 'cargo', 'Meka', 'mekacargo', None),
    ('tall', 'cargo', 'K4T (Loader)', 'k4tcargo', None),
    ('tall', 'cargo', 'K4T (Turtleneck)', 'k4tcargo_alt1', None),
    ('tall', 'cargo', 'NiKA', 'fmekacargo', None),
    ('tall', 'cargo', 'NiKO', 'mmekacargo', None),
    ('tall', 'cargo', 'NiKO (Alt)', 'sfmekacargo', None),
    ('tall', 'sec', 'Meka', 'mekasec', None),
    ('tall', 'sec', 'K4T', 'k4tsec', None),
    ('tall', 'sec', 'NiKA', 'fmekasec', None),
    ('tall', 'sec', 'NiKO', 'mmekasec', None),
    ('tall', 'pk', 'Meka', 'mekapeace', None),
    ('tall', 'pk', 'K4T', 'k4tpeace', None),
    ('tall', 'pk', 'NiKA', 'fmekapeace', None),
    ('tall', 'pk', 'NiKO', 'mmekapeace', None),
    ('tall', 'pk', 'NiKO (Alt)', 'sfmekapeace', None),
    ('tall', 'ninja', 'Meka', 'mekaninja', None),
    ('tall', 'ninja', 'K4T', 'k4tninja', None),
    ('tall', 'ninja', 'NiKA', 'fmekaninja', None),
    ('tall', 'ninja', 'NiKO', 'mmekaninja', None),
    ('tall', 'syndi', 'Meka', 'mekasyndi', None),
    ('tall', 'syndi', 'K4T', 'k4tsyndi', None),
    ('tall', 'syndi', 'NiKA', 'fmekasyndi', None),
    ('tall', 'syndi', 'NiKO', 'mmekasyndi', None),
    ('tall', 'clown', 'K4T', 'k4tclown', None),
    ('wide', 'eng', 'Drake', 'drakeeng', None),
    ('wide', 'eng', 'Vale', 'valeeng', None),
    ('wide', 'eng', 'Hound', 'engihound', None),
    ('wide', 'eng', 'Darkhound', 'engihounddark', None),
    ('wide', 'eng', 'Borgi', 'borgi-eng', None),
    ('wide', 'eng', 'Otie', 'otiee', None),
    ('wide', 'eng', 'Pup Dozer', 'pupdozer', None),
    ('wide', 'eng', 'Alina', 'alina-eng', 'alina'),
    ('wide', 'jani', 'Drake', 'drakejanit', None),
    ('wide', 'jani', 'Vale', 'J9', None),
    ('wide', 'jani', 'Scrubpuppy', 'scrubpup', None),
    ('wide', 'jani', 'Borgi', 'borgi-jani', None),
    ('wide', 'jani', 'Otie', 'otiej', None),
    ('wide', 'med', 'Drake', 'drakemed', None),
    ('wide', 'med', 'Vale', 'valemed', None),
    ('wide', 'med', 'Hound', 'medihound', None),
    ('wide', 'med', 'Darkhound', 'medihounddark', None),
    ('wide', 'med', 'Borgi', 'borgi-medi', None),
    ('wide', 'med', 'Alina', 'alina-med', 'alina'),
    ('wide', 'mine', 'Drake', 'drakemine', None),
    ('wide', 'mine', 'Vale', 'valemine', None),
    ('wide', 'mine', 'Hound', 'cargohound', None),
    ('wide', 'mine', 'Darkhound', 'cargohounddark', None),
    ('wide', 'mine', 'Otie', 'otiec', None),
    ('wide', 'mine', 'Blade', 'blade', None),
    ('wide', 'serv', 'Drake', 'drakeserv', None),
    ('wide', 'serv', 'Vale', 'valeserv', None),
    ('wide', 'serv', 'Vale (Dark)', 'valeservdark', None),
    ('wide', 'serv', 'Darkhound', 'k50', None),
    ('wide', 'serv', 'Partyhound', 'k69', None),
    ('wide', 'serv', 'Borgi', 'borgi-serv', None),
    ('wide', 'cargo', 'Drake', 'drakecargo', None),
    ('wide', 'cargo', 'Vale', 'valecargo', None),
    ('wide', 'cargo', 'Hound', 'cargohound', None),
    ('wide', 'cargo', 'Darkhound', 'cargohounddark', None),
    ('wide', 'cargo', 'Borgi', 'borgi-cargo', None),
    ('wide', 'sec', 'Drake', 'drakesec', None),
    ('wide', 'sec', 'Vale', 'valesec', None),
    ('wide', 'sec', 'Hound', 'k9', None),
    ('wide', 'sec', 'Darkhound', 'k9dark', None),
    ('wide', 'sec', 'Borgi', 'borgi-sec', None),
    ('wide', 'sec', 'Otie', 'oties', None),
    ('wide', 'sec', 'Alina', 'alina-sec', 'alina'),
    ('wide', 'pk', 'Drake', 'drakepeace', None),
    ('wide', 'pk', 'Vale', 'valepeace', None),
    ('wide', 'pk', 'Borgi', 'borgi', None),
]

FILES = {
    ('tall', d): f'tallrobot_{d}.dmi' for d in DEPTS
} | {
    ('wide', d): ('widerobots_cargo.dmi' if d == 'cargo' else f'widerobot_{d}.dmi') for d in DEPTS
}

# Extra poses kept in each skin's RSI; nothing uses them yet.
POSES = ['rest', 'sit', 'bellyup', 'wreck', 'tipped']
# The eng sheet has the Alina body but not its overlays.
LIGHT_FALLBACK = {'alina': 'widerobot_med.dmi'}


class Dmi:
    """One .dmi: a PNG sheet plus a text chunk listing its states."""

    def __init__(self, path):
        image = Image.open(path)
        self.name = path.name
        self.width = self.height = 32
        self.states = {}
        self.used = set()
        state = None
        index = 0
        for line in image.info['Description'].splitlines():
            if '=' not in line:
                continue
            key, value = (part.strip() for part in line.split('=', 1))
            if key == 'width':
                self.width = int(value)
            elif key == 'height':
                self.height = int(value)
            elif key == 'state':
                if state:
                    index += state['dirs'] * state['frames']
                state = {'name': value.strip('"'), 'dirs': 1, 'frames': 1, 'delay': None, 'move': False,
                         'rewind': False}
                state['start'] = index
                self.states.setdefault(state['name'], []).append(state)
            elif state is not None:
                if key == 'dirs':
                    state['dirs'] = int(value)
                elif key == 'frames':
                    state['frames'] = int(value)
                elif key == 'delay':
                    state['delay'] = [float(x) for x in value.split(',')]
                elif key == 'movement':
                    state['move'] = value == '1'
                elif key == 'rewind':
                    state['rewind'] = value == '1'
        self.sheet = image.convert('RGBA')
        self.columns = self.sheet.width // self.width

    def find(self, name, move=False):
        for state in self.states.get(name, []):
            if state['move'] == move:
                return state
        return None

    def take(self, name, move=False):
        """Returns (frames[dir][frame], delays) for a state and marks it converted."""
        state = self.find(name, move)
        if state is None:
            return None
        self.used.add((name, move))
        order = list(range(state['frames']))
        delays = state['delay'] or [1.0] * state['frames']
        if state['rewind'] and state['frames'] > 2:
            order += order[-2:0:-1]
        frames = []
        for direction in range(state['dirs']):
            row = []
            for frame in order:
                i = state['start'] + frame * state['dirs'] + direction
                x, y = (i % self.columns) * self.width, (i // self.columns) * self.height
                row.append(self.sheet.crop((x, y, x + self.width, y + self.height)))
            frames.append(row)
        return frames, [round(delays[f] / 10, 3) for f in order]

    def blank(self):
        return [[Image.new('RGBA', (self.width, self.height))] for _ in range(4)], [0.1]


def write_state(folder, name, frames, delays):
    """Writes one RSI state sheet and returns its meta.json entry."""
    flat = [frame for row in frames for frame in row]
    width, height = flat[0].size
    columns = math.ceil(math.sqrt(len(flat)))
    sheet = Image.new('RGBA', (columns * width, math.ceil(len(flat) / columns) * height))
    for i, frame in enumerate(flat):
        sheet.paste(frame, ((i % columns) * width, (i // columns) * height))
    sheet.save(folder / f'{name}.png', optimize=True)
    entry = {'name': name}
    if len(frames) > 1:
        entry['directions'] = len(frames)
    if len(frames[0]) > 1:
        entry['delays'] = [delays] * len(frames)
    return entry


def write_meta(folder, dmi, entries, credit, sha):
    meta = {
        'version': 1,
        'license': 'CC-BY-SA-3.0',
        'copyright': f'{credit}Taken from Bubberstation at {SOURCE.format(sha=sha, file=dmi.name)}',
        'size': {'x': dmi.width, 'y': dmi.height},
        'states': entries,
    }
    (folder / 'meta.json').write_text(json.dumps(meta, indent=2) + '\n', encoding='utf-8', newline='\n')


def credit_for(kind, icon):
    if kind == 'tall':
        if icon.startswith('k4t'):
            return 'K4T tallborg by zy.dras. '
        return 'Tallborg and MekaNIKA skinline by TheOOZ (ghostsheep). '
    if icon.startswith('drake'):
        return 'Drake borg originally by Navier. '
    return ''


def safe(name):
    return re.sub(r'[^a-z0-9_-]+', '_', name.lower().replace('+', 'p')).strip('_')


def main():
    source, sha = Path(sys.argv[1]), sys.argv[2]
    for folder in ('Tall', 'Wide'):
        shutil.rmtree(TEXTURES / folder, ignore_errors=True)
    dmis = {}

    def load(file):
        if file not in dmis:
            dmis[file] = Dmi(source / file)
        return dmis[file]

    subtypes, chassis, names = [], [], []
    for kind, dept, name, icon, light_key in SKINS:
        folder_name, borg_type, word = DEPTS[dept]
        body, type_moves = TYPES[borg_type]
        dmi = load(FILES[kind, dept])
        ident = folder_name + ''.join(w[0].upper() + w[1:] for w in re.findall(r'[A-Za-z0-9]+', name))
        snake = re.sub(r'[^a-z0-9]+', '_', name.lower()).strip('_')
        rsi = TEXTURES / kind.title() / folder_name / f'{snake}.rsi'
        rsi.mkdir(parents=True)

        lights = light_key or icon
        light_dmi = dmi
        if dmi.find(f'{lights}_e') is None and lights in LIGHT_FALLBACK:
            light_dmi = load(LIGHT_FALLBACK[lights])

        idle = dmi.take(icon)
        if idle is None:
            raise SystemExit(f'{dmi.name} has no state {icon}')
        eyes = light_dmi.take(f'{lights}_e') or dmi.blank()
        no_mind = light_dmi.take(f'{lights}_e_r') or eyes
        lamp = light_dmi.take(f'{lights}_l') or dmi.blank()
        states = [(body, idle), (f'{body}_e', eyes), (f'{body}_e_r', no_mind), (f'{body}_l', lamp)]

        walk = dmi.take(icon, True)
        if walk:
            states += [
                (f'{body}_moving', walk),
                (f'{body}_e_moving', light_dmi.take(f'{lights}_e', True) or eyes),
                (f'{body}_e_r_moving', light_dmi.take(f'{lights}_e_r', True) or light_dmi.take(f'{lights}_e', True) or no_mind),
                (f'{body}_l_moving', light_dmi.take(f'{lights}_l', True) or lamp),
            ]
        elif type_moves:
            states.append((f'{body}_moving', idle))
        animated = any(len(frames[0]) > 1 for _, (frames, _) in states)

        for pose in POSES:
            taken = dmi.take(f'{icon}-{pose}')
            if taken:
                states.append((pose, taken))
        if kind == 'tall':
            taken = dmi.take('k4tcredit' if icon.startswith('k4t') else 'credit')
            if taken:
                states.append(('credit', taken))

        entries = [write_state(rsi, state, frames, delays) for state, (frames, delays) in states]
        write_meta(rsi, dmi, entries, credit_for(kind, icon), sha)

        subtype = f'WFBorgSkin{ident}'
        dummy = f'WFBorgChassis{ident}'
        path = rsi.relative_to(ROOT / 'Resources/Textures').as_posix()
        entry = (f'- type: borgSubtype\n  id: {subtype}\n  parentBorgType: {borg_type}\n'
                 f'  spritePath: {path}\n  dummyPrototype: {dummy}\n')
        extra = []
        if kind == 'tall':
            extra.append('  offset: 0, 0.5')
        if animated:
            extra.append('  animated: true')
        if walk:
            extra.append('  walks: true')
        if extra:
            entry += f'\n- type: wfBorgSkin\n  id: {subtype}\n' + '\n'.join(extra) + '\n'
        subtypes.append(((borg_type, kind), entry))
        parent = {'generic': 'BorgChassisGeneric', 'engineering': 'BorgChassisEngineer', 'mining': 'BorgChassisMining',
                  'janitor': 'BorgChassisJanitor', 'medical': 'BorgChassisMedical',
                  'service': 'BorgChassisService'}[borg_type]
        chassis.append(f'- type: entity\n  id: {dummy}\n  parent: {parent}\n  categories: [ HideSpawnMenu ]\n'
                       f'  components:\n  - type: BorgSwitchableSubtype\n    borgSubtype: {subtype}\n')
        names.append(f'ent-{dummy} = {name} {word} cyborg\n')

    # Whatever no skin claimed (cover and weapon overlays, sleeper lights, spare skins) is kept per sheet.
    for (kind, dept), file in FILES.items():
        dmi = dmis.get(file)
        if dmi is None:
            continue
        entries = []
        folder = TEXTURES / kind.title() / DEPTS[dept][0] / 'extras.rsi'
        for name, variants in dmi.states.items():
            for state in variants:
                if (name, state['move']) in dmi.used or name in ('credit', 'k4tcredit'):
                    continue
                folder.mkdir(parents=True, exist_ok=True)
                out = safe(name) + ('_moving' if state['move'] else '')
                while any(e['name'] == out for e in entries):
                    out += '_alt'
                entries.append(write_state(folder, out, *dmi.take(name, state['move'])))
        if entries:
            write_meta(folder, dmi, entries, '', sha)

    PROTOTYPES.mkdir(parents=True, exist_ok=True)
    LOCALE.mkdir(parents=True, exist_ok=True)
    header = '# Generated by Tools/_WF/BorgSkins/convert.py; edit the skin table there.\n\n'
    subtypes.sort(key=lambda pair: pair[0])
    (PROTOTYPES / 'subtypes.yml').write_text(header + '\n'.join(e for _, e in subtypes), encoding='utf-8', newline='\n')
    (PROTOTYPES / 'chassis.yml').write_text(header + '\n'.join(chassis), encoding='utf-8', newline='\n')
    (LOCALE / 'chassis.ftl').write_text(header + ''.join(names), encoding='utf-8', newline='\n')
    print(f'{len(SKINS)} skins from {len(dmis)} sheets')


if __name__ == '__main__':
    main()
