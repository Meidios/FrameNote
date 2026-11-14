-- Per-request entry point passed to REAPER -nonewinst. Paths and notes are data, not Lua code.
local dir=debug.getinfo(1,"S").source:sub(2):match("^(.*)[/\\]")
local f=io.open(dir.."/request","rb"); if not f then return end
local lines={};for l in f:lines() do lines[#lines+1]=(l:gsub("\r$","")) end;f:close()
local function unhex(v) return (v:gsub("%x%x",function(s)return string.char(tonumber(s,16)) end)) end
local function write(name,value) local out=assert(io.open(dir.."/"..name,"wb"));out:write(value);out:close() end
write("started","1")
local ok,err=pcall(function()
  local _,rpp=reaper.EnumProjects(-1,"")
  local folder=rpp and rpp:match("^(.*)[/\\]") or nil
  if not folder then folder=os.getenv("LOCALAPPDATA").."/FrameNote/REAPER" end
  local receiver=assert(loadfile(unhex(lines[1])))("receive")
  local result,why=receiver(lines,folder,dir:match("([^/\\]+)$"))
  assert(result,why)
end)
write("result.tmp",ok and "OK" or tostring(err))
os.rename(dir.."/result.tmp",dir.."/result")
