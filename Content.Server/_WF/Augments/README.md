# Augments

Cyberware a character carries from round to round, after the augments panel on Meridian Rift (Nova Sector). Every
cybernetic part, organ and upgrading autosurgeon in the game is a `wfAugment` in `augments.yml`: plain styled limbs,
hands and feet (free), J.W.L arms, S.P.E.E.D legs, D.E.X hands, cyber eyes, IAS lungs, the faction hearts and
Sandevistans, mantis blades, dermal armor and muscle fibers. A piece of cyberware missing from it is lost at the
end of every round.

A character gets an augment three ways:

- **The creator.** The Augments tab draws the character as a doll (`WFAugmentDoll`); hovering a part outlines it
  and clicking it lists that part's limb, organs and implants as cards (`WFAugmentCard`), each with its icon, what it
  does and its price, greyed out where it can't go. An icon (`WFAugmentIcons`) is the augment's `icon`, else the sprite
  of the entity it installs; implants name one because autosurgeons all look alike. A styled limb's style is picked
  from a dropdown that draws the limb in each (`WFAugmentStylePicker`). The loadout beside it totals what confirming
  buys and pays back. The tab takes its colors and fonts from the player's Wolfgate skin (`WFAugmentTheme`) and
  folds from three columns to two where it is narrow. A `price` is taken once from the character's bank
  when it is saved with the augment (`ServerPreferencesManager.SettleAugments`), and the character owns it
  (`AugmentUnlocks`); one it no longer picks is sold back for `WFAugmentRules.RefundPercent` (half) of its price
  if it was paid for here. One kept from a round (`AugmentFound`) is scrapped for nothing, so cheap in-round
  cyberware can't be turned into bank money. Only the server adds unlocks or marks them found. Nothing is gated by
  faction: any character can buy the faction hearts and implants here.
- **Buying it in a round** from the faction lathes, uplinks or loadouts, as before, and installing it.
- **Scavenging.** `WFAugmentSelector` (the `WFAugmentScavengeTable`) drops a priced augment's bare part or organ,
  which a surgeon puts in, or an upgrade's autosurgeon. It turns up only on the floors of wrecks
  (`WFAugmentScavengeSpawner` in the wreck debris floor list, `wrecks.yml`). The weight is the inverse of the price, so a Sandevistan is about 33 times rarer than cyber eyes.

Implants (`kind: Upgrade`) are a set of their own (`AugmentImplants`), not slot choices: an implant's `slot` is the
part it goes in, matching its autosurgeon's target. A part holds `implantSlots` of them (a J.W.L arm or S.P.E.E.D leg
two, other limbs and natural parts one, the torso two, a missing limb none), and `host` says whether that part must be
synthetic: a cybernetic or trait limb, or a natural part not built of flesh (an IPC's). Mantis blades need one. A
hand or foot with no pick of its own counts as its limb's (`WFAugmentRules.LimbAbove`). An
implant that no longer fits after a limb changes is dropped. They are stored in the augments column as `Implant:`
entries; the old one-per-slot entries (`LeftArmImplant`, `Dermal`, ...) still load.

Wolfgate's own cyberware is in `implants.yml`. The tool arm and the finger lighter are deployables
(`WFDeployableImplantComponent`, one subclass each so a part can hold both): `WFDeployableImplantSystem` gives the body
an action that folds the implant's item into that side's hand and back, and an EMP folds it in and locks it. The tool
arm lists six tools, so its action opens a wheel under the mouse instead (`WFDeployableWheel`, sent and answered by
network events): picking a tool folds it out in place of the one that was out, and picking that one folds it back in.
A tool is spawned as it folds out and deleted as it folds in, so the welder comes out full each time. The
smartlink (`WFSmartlinkSystem`) tightens the spread of a gun held in its hand, and widens it past unlinked while an
EMP has it scrambled. The scrubber liver metabolises alcohol, poison and narcotics four times as fast and clears
Wolfmed toxin load three times as fast (`WolfmedOrganComponent.ClearanceFactor`).

Three implants bring back the combat traits Monolith removed (#5023), paid for and losable: the adrenal booster
adds Monolith's `Vigor`, the threat-sense sole its `TrapAvoider`, and the pain editor (`WFPainEditorSystem`) scales pain
gain down in place of Tenacity, whose crit threshold Wolfmed doesn't read.

Three more look after the body, each a component the autosurgeon puts on it. The nutriment pump
(`WFNutrimentPumpSystem`) feeds its owner a little whenever they reach starving, which leaves them peckish and food
still worth eating. The reviver (`WFReviverSystem`) is a defibrillator of its own: when the heart stops it charges
for ten seconds and shocks through `WolfmedRevivalSystem`, so it is refused for what a medic's paddles are refused
for and restarts the heart on their odds, and it heals no wound. While it charges it holds every bleed the way a
stasis weave does (`WolfmedStasisHoldComponent`, until a minute after the shock) and runs up to 60u of blood in,
towards Wolfmed's post-shock target, so a body that bled out is over the paddles' blood line when the charge is up
and over the arrest line after it. It doesn't charge for a destroyed heart. It has three shocks, fifteen seconds
apart, then recharges for ten minutes; an EMP knocks the charge out and locks it for two. The iatric monitor
(`WFIatricMonitorSystem`) gives an action that opens the health analyzer's window on its own body; the action entity
is the analyzer, and being held by the body is what keeps the window in range.

A health analyzer scan carries the cyberware in the body (`WFAugmentSystem.ReadInstalled`, sent as
`HealthAnalyzerScannedUserMessage.Augments`), and the organs tab lists it under the organs (`WFAugmentScanList`).

Installed cyberware is written back to the profile (`WFAugmentSystem.Persistence`). When the character goes into
cryo, or the round ends with the player still in it (dead or alive), `ReadBody` lists the augments in the body:
each slot's part or organ matched by prototype, and `ReadImplants` the upgrades by the components their autosurgeon
adds (`AutoSurgeonSystem.WfHasUpgrade`). Those are kept and owned, the new ones as found; what the character spawned with and lost is gone
along with its unlock, refunding nothing. A slot or the implants changed in the creator mid-round keep the new pick.
An implant put in a part it doesn't fit (a mantis blade in a flesh arm) works for the round but isn't kept. When a player uses `ghostrespawn`
with no body in cryo, the character loses every augment and unlock (`ForfeitAugments`); the admin `respawn`
command and cloning take nothing. A body the player left for a ghost role or another character isn't read.

The limbs are real Shitmed cybernetic parts: they take EMPs, repair with a welder and cable, and come off and go
back on like any chassis limb. A hand or foot a limb generates (`GenerateChildPart`) belongs to that limb, not to
a slot of its own. Markings from the Markings tab are drawn for flesh and stay off an augmented layer (its base
sprites set `allowsMarkings: false`); custom markings draw over augments like anywhere else. Head and chest
plating and missing limbs have no content yet; the integration tests use test-only prototypes for them.

Traits that fit their own limbs at spawn (Prybar Prosthetics, Bionic Legs) claim those slots in `trait_slots.yml`.
A character with one can't take augments there: validation drops them and the tab names the trait instead. A new
limb trait needs an entry there. The trait systems swap their limbs the way augments are swapped, then call
`WFAugmentSystem.StyleTraitLimbs`, which draws them in the EE Bishop style fitted to the species, so an Avali's
prosthetics keep the Avali outline.

Entry points: `augments.yml`, `WFAugmentRules` (slots, validation, storage), `SharedHumanoidAppearanceSystem.ApplyAugments`,
run from `LoadProfile` after the biotype (the client's doll calls `ApplyAugmentVisuals` from its own `LoadProfile`),
and `WFAugmentSystem`, which swaps the parts in slot order (a limb before its hand) and then puts the implants in
through `AutoSurgeonSystem.WfApplyUpgrade`. The old part is cut loose without the events that read as an injury
(`SharedBodySystem.WfCutLoose`), so a hand keeps what it holds and Wolfmed sees no amputation.

## Styles and art

Every robotic style Meridian Rift offers (cyborg, surplus, advanced, the department sets and the IPC brands) and
each Einstein Engines cyberlimb brand is a `wfAugmentStyle`. `Tools/_WF/Augments/port.py` writes the RSIs under
`Resources/Textures/_WF/Augments`, the base sprites, `styles.yml` and the style names, from a Meridian Rift checkout:

    python Tools/_WF/Augments/port.py --source <Meridian-Rift checkout>

Every style has digitigrade legs, drawn when the character's legs are digitigrade (LegStyle). Meridian Rift draws
them for the cyborg and advanced styles; for the rest each row of the digitigrade cyborg leg samples the same row of
the style's own leg. Each layer is trimmed to the human body's outline (the digitigrade human legs for digitigrade
ones), the body clothing is drawn for, so nothing shows past a sleeve or trouser leg. Sheets that draw both legs of a
side view in one state are split into the two legs, and arms and hands drawn a pixel or two off are moved onto the body
before the trim.

Species whose limbs are clearly unlike the human's (Avali, Resomi, Vox, Diona, Moth, Vulpkanin and the rest) get every
style redrawn onto their own limbs: the script groups each limb outline a roundstart species or a leg style draws that
overlaps the human's by less than `CLOSE_ENOUGH` into a `wfAugmentShape` (`shapes.yml`) and writes each style's
version of it (`fitted` on the style). Closer bodies wear the human-shaped sprites. At load the augment
draws the version fitted to the limb the body would draw there (`WFAugmentRules.Underlying`, its leg style's or its
species'), so it takes that limb's pixels and the species' clothing fits it. Rerun the script after adding a species
or a leg style.

A profile loaded onto a body that already has augments (cloning, a DNA scrambler, an admin load) only changes the
slots it picks: the rest keep their parts and their look, as a reattached limb would. The creator's doll redraws from
scratch, so an unpicked augment comes off it.

<!-- WOLFGATE-GENERATED START -->
<!-- Generated by python Tools/_WF/Ci/modules.py --write. Don't edit by hand. -->

## Files

### Server

- [`Content.Server/_WF/Augments/HealthAnalyzerSystem.Augments.cs`](HealthAnalyzerSystem.Augments.cs)
- [`Content.Server/_WF/Augments/ServerPreferencesManager.Augments.cs`](ServerPreferencesManager.Augments.cs)
- [`Content.Server/_WF/Augments/WFAugmentSystem.cs`](WFAugmentSystem.cs)
- [`Content.Server/_WF/Augments/WFAugmentSystem.Persistence.cs`](WFAugmentSystem.Persistence.cs)
- [`Content.Server/_WF/Augments/WFAugmentSystem.Traits.cs`](WFAugmentSystem.Traits.cs)
- [`Content.Server/_WF/Augments/WFDeployableImplantSystem.cs`](WFDeployableImplantSystem.cs)
- [`Content.Server/_WF/Augments/WFIatricMonitorSystem.cs`](WFIatricMonitorSystem.cs)
- [`Content.Server/_WF/Augments/WFNutrimentPumpSystem.cs`](WFNutrimentPumpSystem.cs)
- [`Content.Server/_WF/Augments/WFReviverSystem.cs`](WFReviverSystem.cs)

### Shared

- [`Content.Shared/_WF/Augments/AutoSurgeonSystem.Augments.cs`](../../../Content.Shared/_WF/Augments/AutoSurgeonSystem.Augments.cs)
- [`Content.Shared/_WF/Augments/HumanoidAppearanceComponent.Augments.cs`](../../../Content.Shared/_WF/Augments/HumanoidAppearanceComponent.Augments.cs)
- [`Content.Shared/_WF/Augments/HumanoidCharacterProfile.Augments.cs`](../../../Content.Shared/_WF/Augments/HumanoidCharacterProfile.Augments.cs)
- [`Content.Shared/_WF/Augments/SharedBodySystem.Augments.cs`](../../../Content.Shared/_WF/Augments/SharedBodySystem.Augments.cs)
- [`Content.Shared/_WF/Augments/SharedHumanoidAppearanceSystem.Augments.cs`](../../../Content.Shared/_WF/Augments/SharedHumanoidAppearanceSystem.Augments.cs)
- [`Content.Shared/_WF/Augments/WFApplyAugmentsEvent.cs`](../../../Content.Shared/_WF/Augments/WFApplyAugmentsEvent.cs)
- [`Content.Shared/_WF/Augments/WFAugmentChoice.cs`](../../../Content.Shared/_WF/Augments/WFAugmentChoice.cs)
- [`Content.Shared/_WF/Augments/WFAugmentPrototype.cs`](../../../Content.Shared/_WF/Augments/WFAugmentPrototype.cs)
- [`Content.Shared/_WF/Augments/WFAugmentRules.cs`](../../../Content.Shared/_WF/Augments/WFAugmentRules.cs)
- [`Content.Shared/_WF/Augments/WFAugmentSelector.cs`](../../../Content.Shared/_WF/Augments/WFAugmentSelector.cs)
- [`Content.Shared/_WF/Augments/WFAugmentShapePrototype.cs`](../../../Content.Shared/_WF/Augments/WFAugmentShapePrototype.cs)
- [`Content.Shared/_WF/Augments/WFAugmentSlot.cs`](../../../Content.Shared/_WF/Augments/WFAugmentSlot.cs)
- [`Content.Shared/_WF/Augments/WFAugmentStylePrototype.cs`](../../../Content.Shared/_WF/Augments/WFAugmentStylePrototype.cs)
- [`Content.Shared/_WF/Augments/WFAugmentTraitSlotsPrototype.cs`](../../../Content.Shared/_WF/Augments/WFAugmentTraitSlotsPrototype.cs)
- [`Content.Shared/_WF/Augments/WFDeployableImplantComponent.cs`](../../../Content.Shared/_WF/Augments/WFDeployableImplantComponent.cs)
- [`Content.Shared/_WF/Augments/WFIatricMonitorComponent.cs`](../../../Content.Shared/_WF/Augments/WFIatricMonitorComponent.cs)
- [`Content.Shared/_WF/Augments/WFNutrimentPumpComponent.cs`](../../../Content.Shared/_WF/Augments/WFNutrimentPumpComponent.cs)
- [`Content.Shared/_WF/Augments/WFPainEditorComponent.cs`](../../../Content.Shared/_WF/Augments/WFPainEditorComponent.cs)
- [`Content.Shared/_WF/Augments/WFPainEditorSystem.cs`](../../../Content.Shared/_WF/Augments/WFPainEditorSystem.cs)
- [`Content.Shared/_WF/Augments/WFReviverComponent.cs`](../../../Content.Shared/_WF/Augments/WFReviverComponent.cs)
- [`Content.Shared/_WF/Augments/WFSmartlinkComponent.cs`](../../../Content.Shared/_WF/Augments/WFSmartlinkComponent.cs)
- [`Content.Shared/_WF/Augments/WFSmartlinkSystem.cs`](../../../Content.Shared/_WF/Augments/WFSmartlinkSystem.cs)

### Client

- [`Content.Client/_WF/Augments/HumanoidProfileEditor.Augments.cs`](../../../Content.Client/_WF/Augments/HumanoidProfileEditor.Augments.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentBackdrop.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentBackdrop.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentCard.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentCard.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentDoll.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentDoll.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentEditor.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentEditor.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentIcon.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentIcon.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentMask.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentMask.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentScanList.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentScanList.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentStylePicker.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentStylePicker.cs)
- [`Content.Client/_WF/Augments/UI/WFAugmentTheme.cs`](../../../Content.Client/_WF/Augments/UI/WFAugmentTheme.cs)
- [`Content.Client/_WF/Augments/UI/WFDeployableWheel.cs`](../../../Content.Client/_WF/Augments/UI/WFDeployableWheel.cs)
- [`Content.Client/_WF/Augments/WFDeployableWheelSystem.cs`](../../../Content.Client/_WF/Augments/WFDeployableWheelSystem.cs)

### Integration tests

- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentBodyTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentBodyTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentClothedTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentClothedTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentEditorTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentEditorTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentImplantsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentImplantsTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentPersistenceTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentPersistenceTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentPreviewTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentPreviewTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentPurchaseTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentPurchaseTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentRespawnTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentRespawnTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentRulesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentRulesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentSpeciesTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentSpeciesTest.cs)
- [`Content.IntegrationTests/Tests/_WF/Augments/AugmentVitalImplantsTest.cs`](../../../Content.IntegrationTests/Tests/_WF/Augments/AugmentVitalImplantsTest.cs)

### Prototypes

- [`Resources/Prototypes/_WF/Augments/augments.yml`](../../../Resources/Prototypes/_WF/Augments/augments.yml)
- [`Resources/Prototypes/_WF/Augments/base_sprites.yml`](../../../Resources/Prototypes/_WF/Augments/base_sprites.yml)
- [`Resources/Prototypes/_WF/Augments/implants.yml`](../../../Resources/Prototypes/_WF/Augments/implants.yml)
- [`Resources/Prototypes/_WF/Augments/loot.yml`](../../../Resources/Prototypes/_WF/Augments/loot.yml)
- [`Resources/Prototypes/_WF/Augments/parts.yml`](../../../Resources/Prototypes/_WF/Augments/parts.yml)
- [`Resources/Prototypes/_WF/Augments/shapes.yml`](../../../Resources/Prototypes/_WF/Augments/shapes.yml)
- [`Resources/Prototypes/_WF/Augments/styles.yml`](../../../Resources/Prototypes/_WF/Augments/styles.yml)
- [`Resources/Prototypes/_WF/Augments/trait_slots.yml`](../../../Resources/Prototypes/_WF/Augments/trait_slots.yml)

### Localization

- [`Resources/Locale/en-US/_WF/Augments/augments.ftl`](../../../Resources/Locale/en-US/_WF/Augments/augments.ftl)
- [`Resources/Locale/en-US/_WF/Augments/styles.ftl`](../../../Resources/Locale/en-US/_WF/Augments/styles.ftl)

### Textures

- [`Resources/Textures/_WF/Augments/advanced.rsi/`](../../../Resources/Textures/_WF/Augments/advanced.rsi/)
- [`Resources/Textures/_WF/Augments/bishop.rsi/`](../../../Resources/Textures/_WF/Augments/bishop.rsi/)
- [`Resources/Textures/_WF/Augments/bishop2.rsi/`](../../../Resources/Textures/_WF/Augments/bishop2.rsi/)
- [`Resources/Textures/_WF/Augments/cyborg.rsi/`](../../../Resources/Textures/_WF/Augments/cyborg.rsi/)
- [`Resources/Textures/_WF/Augments/e3n.rsi/`](../../../Resources/Textures/_WF/Augments/e3n.rsi/)
- [`Resources/Textures/_WF/Augments/eebishop.rsi/`](../../../Resources/Textures/_WF/Augments/eebishop.rsi/)
- [`Resources/Textures/_WF/Augments/eehephaestus.rsi/`](../../../Resources/Textures/_WF/Augments/eehephaestus.rsi/)
- [`Resources/Textures/_WF/Augments/eemorpheus.rsi/`](../../../Resources/Textures/_WF/Augments/eemorpheus.rsi/)
- [`Resources/Textures/_WF/Augments/eeshellguard.rsi/`](../../../Resources/Textures/_WF/Augments/eeshellguard.rsi/)
- [`Resources/Textures/_WF/Augments/eewardtakahashi.rsi/`](../../../Resources/Textures/_WF/Augments/eewardtakahashi.rsi/)
- [`Resources/Textures/_WF/Augments/eexion.rsi/`](../../../Resources/Textures/_WF/Augments/eexion.rsi/)
- [`Resources/Textures/_WF/Augments/eezenghu.rsi/`](../../../Resources/Textures/_WF/Augments/eezenghu.rsi/)
- [`Resources/Textures/_WF/Augments/engineering.rsi/`](../../../Resources/Textures/_WF/Augments/engineering.rsi/)
- [`Resources/Textures/_WF/Augments/hephaestus.rsi/`](../../../Resources/Textures/_WF/Augments/hephaestus.rsi/)
- [`Resources/Textures/_WF/Augments/hephaestus2.rsi/`](../../../Resources/Textures/_WF/Augments/hephaestus2.rsi/)
- [`Resources/Textures/_WF/Augments/implants.rsi/`](../../../Resources/Textures/_WF/Augments/implants.rsi/)
- [`Resources/Textures/_WF/Augments/mariinsky.rsi/`](../../../Resources/Textures/_WF/Augments/mariinsky.rsi/)
- [`Resources/Textures/_WF/Augments/mining.rsi/`](../../../Resources/Textures/_WF/Augments/mining.rsi/)
- [`Resources/Textures/_WF/Augments/morpheus.rsi/`](../../../Resources/Textures/_WF/Augments/morpheus.rsi/)
- [`Resources/Textures/_WF/Augments/scrappy.rsi/`](../../../Resources/Textures/_WF/Augments/scrappy.rsi/)
- [`Resources/Textures/_WF/Augments/scrappytinted.rsi/`](../../../Resources/Textures/_WF/Augments/scrappytinted.rsi/)
- [`Resources/Textures/_WF/Augments/security.rsi/`](../../../Resources/Textures/_WF/Augments/security.rsi/)
- [`Resources/Textures/_WF/Augments/shellguard.rsi/`](../../../Resources/Textures/_WF/Augments/shellguard.rsi/)
- [`Resources/Textures/_WF/Augments/surplus.rsi/`](../../../Resources/Textures/_WF/Augments/surplus.rsi/)
- [`Resources/Textures/_WF/Augments/tool_arm.rsi/`](../../../Resources/Textures/_WF/Augments/tool_arm.rsi/)
- [`Resources/Textures/_WF/Augments/wardtakahashi.rsi/`](../../../Resources/Textures/_WF/Augments/wardtakahashi.rsi/)
- [`Resources/Textures/_WF/Augments/xion.rsi/`](../../../Resources/Textures/_WF/Augments/xion.rsi/)
- [`Resources/Textures/_WF/Augments/xion2.rsi/`](../../../Resources/Textures/_WF/Augments/xion2.rsi/)
- [`Resources/Textures/_WF/Augments/zenghu.rsi/`](../../../Resources/Textures/_WF/Augments/zenghu.rsi/)
- [`Resources/Textures/_WF/Augments/zhenkov.rsi/`](../../../Resources/Textures/_WF/Augments/zhenkov.rsi/)
- [`Resources/Textures/_WF/Augments/zhenkovdark.rsi/`](../../../Resources/Textures/_WF/Augments/zhenkovdark.rsi/)

### Tools

- [`Tools/_WF/Augments/port.py`](../../../Tools/_WF/Augments/port.py)

## Non-modular edits

- [`Content.Client/Humanoid/HumanoidAppearanceSystem.cs`](../../../Content.Client/Humanoid/HumanoidAppearanceSystem.cs)
  - the doll shows the picked augments, client-side only
  - an augment's layer takes no markings
- [`Content.Client/Lobby/UI/HumanoidProfileEditor.xaml`](../../../Content.Client/Lobby/UI/HumanoidProfileEditor.xaml)
  - the wfa xmlns on the root tag below is for the augment editor.
  - augments tab (xmlns:wfa), after anatomy so earlier tabs keep their indices
- [`Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs`](../../../Content.Client/Lobby/UI/HumanoidProfileEditor.xaml.cs)
  - a trait with its own limbs claims those slots
  - a trait with its own limbs takes those slots
  - augments the new species can't take are dropped
- [`Content.Server.Database/Model.cs`](../../../Content.Server.Database/Model.cs): augments picked per slot (slot:augment[:style] entries) and priced ones paid for.
- [`Content.Server/_Mono/Traits/Physical/BionicLegsSystem.cs`](../../_Mono/Traits/Physical/BionicLegsSystem.cs)
  - drawn to the species' own leg shape
  - cut the old leg loose like an augment swap, so the body keeps its leg markings
- [`Content.Server/_Mono/Traits/Physical/PrybarProstheticsSystem.cs`](../../_Mono/Traits/Physical/PrybarProstheticsSystem.cs)
  - drawn to the species' own arm shape
  - cut the old arm loose like an augment swap, so the body keeps its arm markings
- [`Content.Server/_NF/Commands/GhostRespawnCommand.cs`](../../_NF/Commands/GhostRespawnCommand.cs): respawning instead of cryo loses the character's augments
- [`Content.Server/Database/ServerDbBase.cs`](../../Database/ServerDbBase.cs)
- [`Content.Server/Medical/HealthAnalyzerSystem.cs`](../../Medical/HealthAnalyzerSystem.cs): the cyberware in the body
- [`Content.Server/Preferences/Managers/ServerPreferencesManager.cs`](../../Preferences/Managers/ServerPreferencesManager.cs)
  - a client save can't unlock augments; new priced ones are bought from the bank here
  - the creator shows the balance, unlocks and dropped augments the save left
- [`Content.Shared/_Goobstation/Autosurgeon/AutoSurgeonSystem.cs`](../../../Content.Shared/_Goobstation/Autosurgeon/AutoSurgeonSystem.cs): augments reapply and detect upgrades
- [`Content.Shared/Body/Systems/SharedBodySystem.Parts.cs`](../../../Content.Shared/Body/Systems/SharedBodySystem.Parts.cs): a part cut loose for an augment isn't lost
- [`Content.Shared/Humanoid/SharedHumanoidAppearanceSystem.cs`](../../../Content.Shared/Humanoid/SharedHumanoidAppearanceSystem.cs): swaps in the augmented parts and draws their styles
- [`Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs`](../../../Content.Shared/MedicalScanner/HealthAnalyzerScannedUserMessage.cs): the cyberware in the body, for the organs tab
- [`Content.Shared/Preferences/HumanoidCharacterProfile.cs`](../../../Content.Shared/Preferences/HumanoidCharacterProfile.cs): augments the species can't take are dropped
- [`Resources/Prototypes/_NF/Entities/World/Debris/wrecks.yml`](../../../Resources/Prototypes/_NF/Entities/World/Debris/wrecks.yml): scavenged cyberware, the dearer the rarer

<!-- WOLFGATE-GENERATED END -->
