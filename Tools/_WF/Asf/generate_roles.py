"""Writes Resources/Prototypes/_WF/Asf/Roles/jobs.yml from the roster below.

Run from the repo root: python Tools/_WF/Asf/generate_roles.py
"""
from pathlib import Path

OUT = Path("Resources/Prototypes/_WF/Asf/Roles/jobs.yml")

# id, locale key, icon state, weight, overall playtime requirement (s), access group, extra equipment, backpack
# contents, ASF loadout groups. Clothing a loadout group covers stays out of the starting gear: loadouts are
# equipped first, so a starting-gear item for the same slot would land on the floor.
JOBS = [
    ("Envoy", "envoy", "Envoy", 40, 0, "WFAsfCommand",
     {},
     ["RadioHandheldNF", "WFAsfVoucherEscort"],
     ["WFAsfJumpsuitService", "WFAsfHeadEnvoy", "WFAsfNeckEnvoy", "WFAsfOuterEnvoy", "WFAsfGloves"]),
    ("Enforcer", "enforcer", "Enforcer", 30, 0, "WFAsfSecurity",
     {"outerClothing": "ClothingOuterArmorBasic", "pocket1": "WeaponDisabler"},
     ["RadioHandheldNF", "Handcuffs", "Handcuffs", "WFAsfWeaponPistolCoil", "WFAsfMagazinePistolCoil"],
     ["WFAsfJumpsuitService", "WFAsfHeadEnforcer", "WFAsfNeck", "WFAsfGloves"]),
    ("FieldResearcher", "field-researcher", "Researcher", 20, 0, "WFAsfResearch",
     {"outerClothing": "ClothingOuterCoatLab"},
     ["RadioHandheldNF", "HandheldHealthAnalyzer", "AnomalyScanner"],
     ["WFAsfJumpsuitResearcher", "WFAsfHead", "WFAsfNeck", "WFAsfGloves"]),
    ("Colonist", "colonist", "Colonist", 10, 0, "WFAsfCrew",
     {"belt": "ClothingBeltUtilityFilled"},
     ["RadioHandheldNF"],
     ["WFAsfJumpsuitColonist", "WFAsfHead", "WFAsfNeck", "WFAsfGlovesColonist"]),
]

# PDA screen accent per icon state, matching the trim drawn by draw_ids.py.
PDA_ACCENTS = {"Envoy": "#EBC913", "Enforcer": "#F2EEFB", "Researcher": "#C9BFE0"}

# Shared groups after each role's own: ASF bags, then the contractor extras other factions' roles offer.
# NFSpeciesSpecific is hidden and gives Vox their nitrogen and Avali their auto-injector.
LOADOUT_GROUPS = ["WFAsfBackpack", "MercenaryGlasses", "ContractorEncryptionKey", "ContractorBoxSurvival",
                  "ContractorWallet", "ContractorCartridge", "ContractorImplanter", "ContractorUtility",
                  "ContractorFun", "ContractorTrinkets", "ContractorBureaucracy", "NFSpeciesSpecific",
                  "ContractorArmorPlates"]


def job(jid, key, icon, weight, req, group, extra, storage, groups):
    j = "WFAsf" + jid
    supervisors = "wf-asf-job-supervisors-council" if jid == "Envoy" else "wf-asf-job-supervisors-envoy"
    equipment = {"shoes": "WFAsfClothingShoesBoots", "ears": "WFAsfClothingHeadset", "id": j + "PDA", **extra}
    lines = [
        "- type: job",
        f"  id: {j}",
        f"  name: wf-asf-job-{key}",
        f"  description: wf-asf-job-{key}-description",
        f"  playTimeTracker: Job{j}",
    ]
    if req:
        lines += ["  requirements:", "  - !type:OverallPlaytimeRequirement", f"    time: {req} # {req // 3600} hrs"]
    lines += [
        f"  startingGear: {j}Gear",
        "  alwaysUseSpawner: true",
        "  hideConsoleVisibility: true",
        f"  icon: JobIcon{j}",
        f"  supervisors: {supervisors}",
        "  assignedCompany: WFAsf",
        "  canBeAntag: false",
        f"  weight: {weight}",
        f"  displayWeight: {weight}",
        "  accessGroups:",
        "  - GeneralAccess",
        f"  - {group}",
        "",
        "- type: playTimeTracker",
        f"  id: Job{j}",
        "",
        "- type: startingGear",
        f"  id: {j}Gear",
        "  equipment:",
    ]
    lines += [f"    {slot}: {proto}" for slot, proto in equipment.items()]
    lines += ["  storage:", "    back:"] + [f"    - {s}" for s in storage]
    lines += ["", "- type: roleLoadout", f"  id: Job{j}", "  groups:"] + [f"  - {g}" for g in groups + LOADOUT_GROUPS]
    lines += [
        "",
        "- type: jobIcon",
        "  parent: JobIcon",
        f"  id: JobIcon{j}",
        "  icon:",
        "    sprite: /Textures/_WF/Asf/Interface/job_icons.rsi",
        f"    state: {icon}",
        f"  jobName: wf-asf-job-{key}",
        "  allowSelection: false",
        "",
        "- type: entity",
        "  parent: IDCardStandard",
        f"  id: {j}IDCard",
        "  components:",
        "  - type: Sprite",
        "    sprite: _WF/Asf/Objects/Misc/id_cards.rsi",
        "    layers:",
        "    - state: asfbase",
        f"    - state: idasf{icon.lower()}",
        "  - type: PresetIdCard",
        f"    job: {j}",
        "",
        "- type: entity",
        "  parent: BasePDA",
        f"  id: {j}PDA",
        "  components:",
        "  - type: Pda",
        f"    id: {j}IDCard",
        "  - type: Sprite",
        "    sprite: _WF/Asf/Objects/Devices/pda.rsi",
        "  - type: Icon",
        "    sprite: _WF/Asf/Objects/Devices/pda.rsi",
        f"    state: pda-{icon.lower()}",
        "  - type: Appearance",
        "    appearanceDataInit:",
        "      enum.PdaVisuals.PdaType:",
        "        !type:String",
        f"        pda-{icon.lower()}",
        "  - type: PdaBorderColor",
        '    borderColor: "#7B52B8"',
    ]
    if icon in PDA_ACCENTS:
        lines.append(f'    accentVColor: "{PDA_ACCENTS[icon]}"')
    lines += [
        "",
        "- type: entity",
        "  parent: SpawnPointJobBase",
        f"  id: WFAsfSpawnPoint{jid}",
        "  components:",
        "  - type: SpawnPoint",
        f"    job_id: {j}",
        "  - type: Sprite",
        "    sprite: _WF/Asf/Structures/Banners/federation.rsi",
        "    state: asf-federation-banner",
        "",
    ]
    return "\n".join(lines)


header = "# Generated by Tools/_WF/Asf/generate_roles.py; edit the roster there.\n\n"
OUT.parent.mkdir(parents=True, exist_ok=True)
OUT.write_text(header + "\n".join(job(*j) for j in JOBS), encoding="utf-8", newline="\n")
print("wrote", OUT)
