local UnityAliases = {}

local function applyCommonProperties(instance, name, parent)
	if name ~= nil then
		instance.Name = name
	end

	if parent ~= nil then
		instance.Parent = parent
	end

	return instance
end

function UnityAliases.CreateGameObject(name, parent)
	local model = Instance.new("Model")
	return applyCommonProperties(model, name, parent)
end

function UnityAliases.Log(...)
	print("[Unity]", ...)
end

function UnityAliases.LogWarning(...)
	warn("[Unity][Warning]", ...)
end

function UnityAliases.LogError(...)
	warn("[Unity][Error]", ...)
end

local function safeDisconnect(connection)
	if connection == nil then
		return
	end

	local connectionType = typeof(connection)
	if connectionType == "RBXScriptConnection" then
		connection:Disconnect()
	elseif connectionType == "table" then
		local disconnectMethod = connection.Disconnect or connection.disconnect
		if typeof(disconnectMethod) == "function" then
			disconnectMethod(connection)
		end
	end
end

function UnityAliases.AttachLifecycle(instance, callbacks)
	assert(typeof(instance) == "Instance", "UnityAliases.AttachLifecycle expects an Instance")
	callbacks = callbacks or {}
	assert(type(callbacks) == "table", "UnityAliases.AttachLifecycle expects callbacks to be a table")

	local started = false
	local updateConnection
	local destroyConnection

	if type(callbacks.Start) == "function" then
		task.defer(function()
			if instance.Parent ~= nil or instance:IsDescendantOf(game) then
				started = true
				callbacks.Start(instance)
			else
				started = true
				callbacks.Start(instance)
			end
		end)
	else
		started = true
	end

	local function tryGetRunService(target)
		if target == nil then
			return nil
		end

		local getService = target.GetService or target.getService
		if type(getService) ~= "function" then
			return nil
		end

		local ok, service = pcall(getService, target, "RunService")
		if ok then
			return service
		end

		return nil
	end

	local runService = nil
	if typeof(game) == "Instance" or type(game) == "table" then
		runService = tryGetRunService(game)
	end

	if runService == nil then
		local globalGame = rawget(_G, "game")
		if globalGame ~= nil and globalGame ~= game and (typeof(globalGame) == "Instance" or type(globalGame) == "table") then
			runService = tryGetRunService(globalGame)
		end
	end

	if runService == nil then
		runService = rawget(_G, "RunService")
	end

	local heartbeat = runService and runService.Heartbeat or nil
	if heartbeat ~= nil and type(callbacks.Update) == "function" then
		local heartbeatType = typeof(heartbeat)
		if heartbeatType == "RBXScriptSignal" or (type(heartbeat) == "table" and type(heartbeat.Connect) == "function") then
			updateConnection = heartbeat:Connect(function(dt)
				if not started then
					return
				end
				callbacks.Update(instance, dt)
			end)
		end
	end

	local function cleanup()
		safeDisconnect(updateConnection)
		safeDisconnect(destroyConnection)
		updateConnection = nil
		destroyConnection = nil

		if type(callbacks.Destroy) == "function" then
			callbacks.Destroy(instance)
		end
	end

	if typeof(instance.Destroying) == "RBXScriptSignal" then
		destroyConnection = instance.Destroying:Connect(cleanup)
	elseif typeof(instance.AncestryChanged) == "RBXScriptSignal" then
		destroyConnection = instance.AncestryChanged:Connect(function(_, parent)
			if parent == nil then
				cleanup()
			end
		end)
	end

	return cleanup
end

return UnityAliases
