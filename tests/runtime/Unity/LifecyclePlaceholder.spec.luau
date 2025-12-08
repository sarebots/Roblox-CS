local UnityAliases = require("RobloxCS.Runtime.UnityAliases")

local function withRunService()
	local previousGame = rawget(_G, "game")
	local previousRunService = rawget(_G, "RunService")

	local heartbeatEvent = Instance.new("BindableEvent")
	local runService = {
		Heartbeat = heartbeatEvent.Event,
		_step = function(dt)
			heartbeatEvent:Fire(dt)
		end,
	}

	local gameProxy = {
		GetService = function(_, serviceName)
			if serviceName == "RunService" then
				return runService
			end

			if previousGame ~= nil then
				local getService = previousGame.GetService or previousGame.getService
				if type(getService) == "function" then
					return getService(previousGame, serviceName)
				end
			end

			return nil
		end,
	}

	rawset(_G, "game", gameProxy)
	rawset(_G, "RunService", runService)

	return function()
		heartbeatEvent:Destroy()
		if previousGame == nil then
			rawset(_G, "game", nil)
		else
			rawset(_G, "game", previousGame)
		end

		if previousRunService == nil then
			rawset(_G, "RunService", nil)
		else
			rawset(_G, "RunService", previousRunService)
		end
	end, runService
end

local function waitForDeferredTasks()
	task.wait()
end

CS.defineGlobal("RuntimeSpecs.UnityLifecycleSpec", {
	ShouldInvokeLifecycleCallbacks = function()
		local restore, runService = withRunService()

		local startCount = 0
		local updateCount = 0
		local destroyCount = 0
		local capturedDt = nil

		local model = Instance.new("Model")
		local cleanup = UnityAliases.AttachLifecycle(model, {
			Start = function()
				startCount += 1
			end,
			Update = function(_, dt)
				updateCount += 1
				capturedDt = dt
			end,
			Destroy = function()
				destroyCount += 1
			end,
		})

		waitForDeferredTasks()
		assert(typeof(runService.Heartbeat) == "RBXScriptSignal", "Expected RunService.Heartbeat to be an RBXScriptSignal")

		runService._step(0.03)

		cleanup()
		runService._step(0.02)
		restore()

		assert(startCount == 1, "Start should run once")
		assert(updateCount == 1, "Update should run before cleanup")
		assert(capturedDt == 0.03, "Update should receive the heartbeat delta")
		assert(destroyCount == 1, "Destroy should run during cleanup")
	end,

	ShouldCleanupWhenRemovedFromAncestry = function()
		local restore, runService = withRunService()

		local destroyCount = 0
		local updatesAfterRemoval = 0

		local root = Instance.new("Folder")
		local container = Instance.new("Folder")
		container.Parent = root

		local model = Instance.new("Model")
		model.Parent = container

		local cleanup = UnityAliases.AttachLifecycle(model, {
			Update = function()
				updatesAfterRemoval += 1
			end,
			Destroy = function()
				destroyCount += 1
			end,
		})

		waitForDeferredTasks()
		runService._step(0.01)

		model.Parent = nil
		waitForDeferredTasks()
		runService._step(0.01)

		cleanup()
		restore()

		assert(destroyCount >= 1, "Destroy callback should run when model leaves ancestry")
		assert(updatesAfterRemoval == 0, "Update callbacks should stop after removal")
	end,
})
