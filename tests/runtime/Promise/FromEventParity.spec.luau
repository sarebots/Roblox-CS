local Promise = CS.Promise

local function createMockEvent()
	local listeners = {}

	local event = {}

	function event:Connect(callback)
		table.insert(listeners, callback)
		local active = true
		return {
			Disconnect = function()
				if not active then
					return
				end
				active = false
				for index, value in ipairs(listeners) do
					if value == callback then
						table.remove(listeners, index)
						break
					end
				end
			end,
		}
	end

	function event:Fire(...)
		for _, listener in ipairs(listeners) do
			listener(...)
		end
	end

	function event:GetListenerCount()
		return #listeners
	end

	return event
end

CS.defineGlobal("RuntimeSpecs.PromiseFromEventSpec", {
	ShouldResolveWhenPredicateReturnsTrue = function()
		local event = createMockEvent()

		local promise = Promise.FromEvent(event, function(value)
			return value == "resolve"
		end)

		event:Fire("ignore")
		event:Fire("resolve")

		local result = Promise.GetAwaitResult(promise)
		assert(result.Success, "Promise.FromEvent should resolve when predicate returns true")
		assert(result.Value == "resolve", "Resolved value should match event payload")
		assert(event:GetListenerCount() == 0, "Listener should disconnect after resolution")
	end,

	ShouldRemainPendingWhenPredicateReturnsFalse = function()
		local event = createMockEvent()
		local resolved = false

		local chained = Promise.FromEvent(event, function()
			return false
		end):Then(function()
			resolved = true
		end)

		event:Fire("ignored")
		Promise.GetAwaitResult(Promise.Delay(0.01))
		assert(not resolved, "Promise should stay pending when predicate returns false")
	local status = Promise.GetAwaitResult(chained)
	assert(not status.Success, "Promise should reject when cancelled")
	assert(Promise.Error.IsKind(status.Error, Promise.Error.Kind.AlreadyCancelled), "Cancellation should surface AlreadyCancelled kind")
	end,

	ShouldRejectWhenPredicateReturnsNonBoolean = function()
		local event = createMockEvent()

		local promise = Promise.FromEvent(event, function()
			return "not-a-boolean"
		end)

		local waited = Promise.GetAwaitResult(Promise.Delay(0.01))
		assert(waited.Success, "Delay should complete")

		event:Fire()
		local result = Promise.GetAwaitResult(promise)
		assert(not result.Success, "Promise should reject when predicate returns non-boolean")
		assert(Promise.Error.Is(result.Error), "Non-boolean predicate rejection should be Promise.Error")
	end,
})
