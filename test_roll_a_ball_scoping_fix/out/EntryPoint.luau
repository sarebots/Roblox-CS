local GameLoop = require(script.Parent:WaitForChild("Game").GameLoop)
local Promise = require(game:GetService("ReplicatedStorage"):WaitForChild("RobloxCS.Runtime"):WaitForChild("Promise"))
local CS = require(game:GetService("ReplicatedStorage")
:WaitForChild("Include")
:WaitForChild("RuntimeLib")
)
local List = CS.List
local Dictionary = CS.Dictionary
local Struct = CS.Struct
local EntryPoint
do
  EntryPoint = setmetatable({}, {
    __tostring = function(): string
      return "EntryPoint"
    end

  })
  EntryPoint.__index = EntryPoint
  EntryPoint.__className = "EntryPoint"
  local function constructor(self: EntryPoint): EntryPoint?
    return nil
  end
  function EntryPoint.new(self: EntryPoint): EntryPoint
    local self = {} :: EntryPoint
    return constructor(self) or self
  end
  function EntryPoint.Main(): ()
    local projectName = "RollABall"
    local loop = GameLoop.new()
    local result = Promise.GetAwaitResult(loop:PlayRoundAsync())
    local winner = result.Value
    if winner == nil then
      winner = "Unknown"
    end
    local score = loop:GetScore(winner)
    print(`Bootstrapping {projectName}... {winner} reached {score} points!`)
  end
end
CS.defineGlobal("EntryPoint", EntryPoint)
type EntryPoint = typeof(EntryPoint)
return EntryPoint
