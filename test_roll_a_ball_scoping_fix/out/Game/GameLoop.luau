local PlayerController = require(script.Parent.PlayerController)
local Promise = require(game:GetService("ReplicatedStorage"):WaitForChild("RobloxCS.Runtime"):WaitForChild("Promise"))
local Scoreboard = require(script.Parent.Parent:WaitForChild("UI").Scoreboard)
local CS = require(game:GetService("ReplicatedStorage")
:WaitForChild("Include")
:WaitForChild("RuntimeLib")
)
local List = CS.List
local Dictionary = CS.Dictionary
local Struct = CS.Struct
local GameLoop
do
  GameLoop = setmetatable({}, {
    __tostring = function(): string
      return "GameLoop"
    end

  })
  GameLoop.__index = GameLoop
  GameLoop.__className = "GameLoop"
  local function constructor(self: GameLoop): GameLoop?
    self.ActivePlayers = nil
    self.Scores = nil
    self.IsRunning = false
    self.LastCollector = ""
    self._elapsed = 0
    self._playerController = PlayerController.new()
    self._running = false
    self._scoreboard = Scoreboard.new()
    return nil
  end
  function GameLoop.new(self: GameLoop): GameLoop
    local self = (setmetatable({}, GameLoop) :: any) :: GameLoop
    return constructor(self) or self
  end
  GameLoop.DefaultPlayerName = "Runner"
  function GameLoop:Start(playerName: string?): ()
    if playerName == nil then
      playerName = "Runner"
    end
    if self.conn_OnCollect ~= nil then
      self.conn_OnCollect:Disconnect()
    end
    self._scoreboard:Reset()
    self._scoreboard:AddPlayer(playerName)
    self._playerController:ResetPlayer()
    self.conn_OnCollect = self._playerController.OnCollect:Connect(function(playerName: string): ()
      return self:HandleCollect(playerName)
    end
)
    self._elapsed = 0
    self._running = true
  end
  function GameLoop:PlayRoundAsync(targetCollects: number?, tickDelta: number?, delaySeconds: number?): Promise
    if targetCollects == nil then
      targetCollects = 3
    end
    if tickDelta == nil then
      tickDelta = 0.5
    end
    if delaySeconds == nil then
      delaySeconds = 0.05
    end
    self:Start()
    return self:WaitForWinnerAsync(targetCollects, tickDelta, delaySeconds)
  end
  function GameLoop:Tick(deltaTime: number): ()
    if not self._running then
      return 
    end
    self._elapsed += deltaTime
    self._playerController:Update(deltaTime)
  end
  function GameLoop:Stop(clearScores: boolean?): ()
    if clearScores == nil then
      clearScores = false
    end
    self._running = false
    if self.conn_OnCollect ~= nil then
      self.conn_OnCollect:Disconnect()
    end
    if clearScores then
      self._scoreboard:Reset()
    end
    self._elapsed = 0
  end
  function GameLoop:GetScore(playerName: string): number
    return self._scoreboard:GetScore(playerName)
  end
  function GameLoop:WaitForWinnerAsync(targetCollects: number, tickDelta: number, delaySeconds: number): Promise
    if not self._running then
      return Promise.Reject("GameLoop has been stopped.")
    end
    if self._scoreboard:TryGetWinner(targetCollects, function(...: any): string
      local _val = ...
      if select("#", ...) ~= 0 then
        winner = _val
      end
      return winner
    end
) then
      local winner
      self:Stop(false)
      return Promise.Resolve(winner)
    end
    self:Tick(tickDelta)
    return Promise.Delay(delaySeconds, true):Then(function(_: any): any
      return self:WaitForWinnerAsync(targetCollects, tickDelta, delaySeconds)
    end
)
  end
  function GameLoop:HandleCollect(playerName: string): ()
    self._scoreboard:RecordCollect(playerName)
  end
end
CS.defineGlobal("GameLoop", GameLoop)
type GameLoop = typeof(GameLoop)
return GameLoop
