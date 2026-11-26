-- Per-request entry point passed to REAPER -nonewinst. Paths and notes are data, not Lua code.
local dir=debug.getinfo(1,"S").source:sub(2):match("^(.*)[/\\]")
local f=io.open(dir.."/request","rb"); if not f then return end
local lines={};for l in f:lines() do lines[#lines+1]=(l:gsub("\r$","")) end;f:close()
local function unhex(v) return (v:gsub("%x%x",function(s)return string.char(tonumber(s,16)) end)) end
local locale=io.open(dir.."/language","rb")
local language=locale and locale:read("*a") or "zh-CN"
if locale then locale:close() end
local function write(name,value) local out=assert(io.open(dir.."/"..name,"wb"));out:write(value);out:close() end
write("started","1")
local ok,err=pcall(function()
  local hwnd=tostring(reaper.GetMainHwnd()):match("(%x+)$")
  assert(hwnd and tonumber(hwnd,16)==tonumber(lines[2],16),language=="en" and "REAPER routed the request to a different window. Import canceled." or "REAPER 未将请求交给目标窗口，已取消导入。")
  local _,rpp=reaper.EnumProjects(-1,"")
  local folder=rpp and rpp:match("^(.*)[/\\]") or nil
  if not folder then folder=os.getenv("LOCALAPPDATA").."/FrameNote/REAPER" end
  local receiver=assert(loadfile(unhex(lines[1])))("receive",language)
  local result,why=receiver(lines,folder,dir:match("([^/\\]+)$"),function(fraction)
    local status=io.open(dir.."/progress","w")
    if status then status:write(math.floor(85+fraction*14));status:close() end
  end)
  assert(result,why)
end)
write("result.tmp",ok and "OK" or tostring(err))
os.rename(dir.."/result.tmp",dir.."/result")
