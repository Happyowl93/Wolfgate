-- Assembles <dir>/<layer>_<frame>.png into a layered, animated .aseprite.
-- Params: dir, out (full path, no extension), layers (comma list, bottom-to-top), frames (count),
-- hidden (comma list), delays (comma list of seconds, optional), tags (name:from:to comma list, optional).
local dir = app.params["dir"]
local out = app.params["out"]
local count = tonumber(app.params["frames"] or "1")
local hidden = {}
for n in string.gmatch(app.params["hidden"] or "", "[^,]+") do hidden[n] = true end

local spr
for name in string.gmatch(app.params["layers"], "[^,]+") do
  local first = Image{ fromFile = dir .. "/" .. name .. "_1.png" }
  local layer
  if not spr then
    spr = Sprite(first.width, first.height, ColorMode.RGB)
    layer = spr.layers[1]
    for f = 2, count do spr:newEmptyFrame() end
  else
    layer = spr:newLayer()
  end
  layer.name = name
  for f = 1, count do
    spr:newCel(layer, spr.frames[f], Image{ fromFile = dir .. "/" .. name .. "_" .. f .. ".png" }, Point(0, 0))
  end
  if hidden[name] then layer.isVisible = false end
end

local f = 1
for d in string.gmatch(app.params["delays"] or "", "[^,]+") do
  spr.frames[f].duration = tonumber(d)
  f = f + 1
end
for name, from, to in string.gmatch(app.params["tags"] or "", "([^:,]+):(%d+):(%d+)") do
  local tag = spr:newTag(tonumber(from), tonumber(to))
  tag.name = name
end

spr:saveAs(out .. ".aseprite")
print("saved " .. out .. ".aseprite")
