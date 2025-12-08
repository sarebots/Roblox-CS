local Promise = CS.Promise

local function assertCondition(condition, message)
	if not condition then
		error(message, 2)
	end
end

CS.defineGlobal("RuntimeSpecs.PromiseTimeoutSpec", {
	ShouldCancelUnderlyingPromiseOnTimeout = function()
		local cancelled = false
		local slow = Promise.new(function(_, _, onCancel)
			onCancel(function()
				cancelled = true
			end)
		end)

		local timed = slow:Timeout(0.05)
		local result = Promise.GetAwaitResult(timed)

		assertCondition(not result.Success, "Timeout wrapper should reject when slow promise does not resolve")
		assertCondition(Promise.Error.IsKind(result.Error, Promise.Error.Kind.TimedOut), "Timeout rejection should be TimedOut")
		assertCondition(cancelled, "Underlying promise should receive cancellation hook")
	end,
})
