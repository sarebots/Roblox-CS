local CS = require(game:GetService("ReplicatedStorage")
:WaitForChild("Include")
:WaitForChild("RuntimeLib")
)
local List = CS.List
local Dictionary = CS.Dictionary
local Struct = CS.Struct
local Scoreboard
do
  Scoreboard = setmetatable({}, {
    __tostring = function(): string
      return "Scoreboard"
    end

  })
  Scoreboard.__index = Scoreboard
  Scoreboard.__className = "Scoreboard"
  local function constructor(self: Scoreboard): Scoreboard?
    self.Players = nil
    self.Scores = nil
    self.LastCollector = ""
    self._players = List.new()
    self._scores = Dictionary.new()
    return nil
  end
  function Scoreboard.new(self: Scoreboard): Scoreboard
    local self = (setmetatable({}, Scoreboard) :: any) :: Scoreboard
    return constructor(self) or self
  end
  function Scoreboard:AddPlayer(name: string): ()
    if not (name == nil or name:match("^%s*$")
 ~= nil) and not self._players:Contains(name) then
      self._players:Add(name)
      if not self._scores:ContainsKey(name) then
        self._scores[name] = 0
      end
    end
  end
  function Scoreboard:RecordCollect(name: string, points: number??): ()
    if points == nil then
      points = 1
    end
    self:AddPlayer(name)
    local score = nil
    if not self._scores:TryGetValue(name, function(...: any): number
      local _val = ...
      if select("#", ...) ~= 0 then
        score = _val
      end
      return score
    end
) then
      score = 0
    end
    self._scores[name] = score + math.max(1, if points ~= nil then points.Value else 1)
    self.LastCollector = name
  end
  function Scoreboard:GetScore(playerName: string): number
    local score = nil
    if self._scores:TryGetValue(playerName, function(...: any): number
      local _val = ...
      if select("#", ...) ~= 0 then
        score = _val
      end
      return score
    end
) then
      return score
    end
    return 0
  end
  function Scoreboard:TryGetWinner(targetScore: number, winner: (string?) -> string): boolean
    for _entry_key, _entry_value in pairs(self._scores) do
      local entry = {
        Key = _entry_key, 
        Value = _entry_value
      }
      if entry.Value >= targetScore then
        winner(entry.Key)
        return true
      end
    end
    winner(string.Empty)
    return false
  end
  function Scoreboard:Reset(): ()
    self._players:Clear()
    self._scores:Clear()
    self.LastCollector = nil
  end
end
CS.defineGlobal("Scoreboard", Scoreboard)
type Scoreboard = typeof(Scoreboard)
return Scoreboard
