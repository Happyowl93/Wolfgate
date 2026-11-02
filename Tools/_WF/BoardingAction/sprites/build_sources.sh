#!/bin/sh
# Rebuilds the layered .aseprite sources. Usage: build_sources.sh <layers dir> <output dir>
# Paths must be Windows-style (cygpath -m) for the Steam build of Aseprite.
A="/c/Program Files (x86)/Steam/steamapps/common/Aseprite/Aseprite.exe"
L="$1"
O="$2"
HERE=$(cygpath -m "$(dirname "$0")")

build() {
    name="$1"; layers="$2"; frames="$3"; hidden="$4"; delays="$5"; tags="$6"
    "$A" -b --script-param dir="$L/$name" --script-param out="$O/$name" --script-param layers="$layers" \
        --script-param frames="$frames" --script-param hidden="$hidden" --script-param delays="$delays" \
        --script-param tags="$tags" --script "$HERE/build_layered.lua"
}

build hack_harpoon_gun pods,frame,cable,dart,reel,lamps 1
build hack_dart body,lamp 4 "" 0.4,0.1,0.12,0.1
build ftl_disruptor base,idle,locking,locked 4 idle,locked 0.2,0.2,0.2,0.2
build hack_arc arc 6 "" 0.09,0.09,0.09,0.09,0.09,0.09
build harpoon_turret stand,drum,grips,launcher,harpoon 1
build harpoon harpoon 1
