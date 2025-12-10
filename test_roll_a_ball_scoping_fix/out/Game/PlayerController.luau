local rbxcs_include = game:GetService("ReplicatedStorage"):WaitForChild("Include")
local Signal = require(rbxcs_include.Signal)
local CS = require(game:GetService("ReplicatedStorage")
:WaitForChild("Include")
:WaitForChild("RuntimeLib")
)
local List = CS.List
local Dictionary = CS.Dictionary
local Struct = CS.Struct
local PlayerController
do
  PlayerController = setmetatable({}, {
    __tostring = function(): string
      return "PlayerController"
    end

  })
  PlayerController.__index = PlayerController
  PlayerController.__className = "PlayerController"
  local function constructor(self: PlayerController): PlayerController?
    self._score = 0
    self.OnCollect = Signal.new()
    return nil
  end
  function PlayerController.new(self: PlayerController): PlayerController
    local self = (setmetatable({}, PlayerController) :: any) :: PlayerController
    return constructor(self) or self
  end
  PlayerController.MoveSpeed = 12
  function PlayerController:ResetPlayer(): ()
    self._score = 0
    print(`[{"PlayerController"}] Resetting player for RollABall`)
  end
  function PlayerController:Update(deltaTime: number): ()
    self._score += deltaTime * PlayerController.MoveSpeed
    if self._score >= 10 then
      self._score = 0
      (function(): ()
        local _optional_target = self.OnCollect
        if _optional_target == nil then
          return nil
        end
        return _optional_target:Invoke("Runner")
      end)()
    end
  end
end
CS.defineGlobal("PlayerController", PlayerController)
type PlayerController = typeof(PlayerController)
return PlayerController
