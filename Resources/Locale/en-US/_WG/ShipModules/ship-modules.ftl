wg-ship-module-key = { $label } { $charges }/{ $max }
wg-ship-module-tooltip = Fire with { $key } while piloting, or press this key.
wg-ship-module-status-ready = READY
wg-ship-module-status-charging = CHARGING { $seconds }s
wg-ship-module-status-unpowered = HELM UNPOWERED
wg-ship-module-status-shields = SHIELDS UP
wg-ship-module-status-ftl = IN FTL
wg-ship-module-status-docked = DOCKED
wg-ship-module-status-anchored = ANCHORED

wg-ship-module-refused-unpowered = The helm has no power to fire it.
wg-ship-module-refused-shields = The shield field is holding the hull. Lower it and give the field a few seconds to clear.
wg-ship-module-refused-ftl = Not during a jump.
wg-ship-module-refused-docked = Not while docked.
wg-ship-module-refused-anchored = The hull is held fast.
wg-ship-module-refused-empty = No charge ready.

wg-ship-module-jets-name = MJ-3 "Sidestep" Maneuvering Jets
wg-ship-module-jets-description = Cold-gas jets plumbed through the hull. Each burn kicks the ship along whichever way the pilot is steering, hard enough to step a small hull out of a salvo. Three charges, one back every 8 seconds. A raised shield field holds the hull too stiffly to fire, though a deflector's light field doesn't. Every hull gets the same push, so a big ship barely shifts and a laden one shifts less still.
wg-ship-module-jets-label = JETS
wg-ship-module-jets-no-direction = Hold a direction to burn along.

ui-options-function-wg-ship-module-first = Ship module 1
ui-options-function-wg-ship-module-second = Ship module 2
ui-options-function-wg-ship-module-third = Ship module 3

cmd-shipmodule-desc = Fits a ship module into a slot of any ship, free and at once, or empties the slot.
cmd-shipmodule-help = Usage: { $command } <grid entity | here> <slot 1-3> <module id | none>
cmd-shipmodule-hint-slot = <slot 1-3>
cmd-shipmodule-hint-module = <module id | none>
cmd-shipmodule-unknown = "{ $module }" isn't a ship module.
cmd-shipmodule-fitted = Fitted { $module } into slot { $slot }.
cmd-shipmodule-fitted-elsewhere = { $module } is already fitted in slot { $slot }; a ship carries each module once.
cmd-shipmodule-emptied = Emptied slot { $slot }.
