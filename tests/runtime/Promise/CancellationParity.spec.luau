local Promise = CS.Promise

local function assert(condition, message)
	if not condition then
		error(message, 2)
	end
end

CS.defineGlobal("RuntimeSpecs.PromiseCancellationSpec", {
	ShouldTriggerOnCancelForRetry = function()
		local cancelled = false
		local retrying = Promise.Retry(function()
			return Promise.new(function(_, _, onCancel)
				onCancel(function()
					cancelled = true
				end)
			end)
		end, 3)

		Promise.Cancel(retrying, "stop")
		local result = Promise.GetAwaitResult(retrying)

		assert(not result.Success, "Cancelled retry should reject.")
		assert(cancelled, "Retry inner promise should receive cancellation hook.")
	end,

	ShouldPropagateCancellationThroughAny = function()
		local cancelled = false
		local pending = Promise.new(function(_, _, onCancel)
			onCancel(function()
				cancelled = true
			end)
		end)

		local aggregate = Promise.Any({ pending })
		Promise.Cancel(aggregate, "stop")
		local result = Promise.GetAwaitResult(aggregate)

		assert(not result.Success, "Cancelled any should reject.")
		assert(cancelled, "Promise.any should forward cancellation to child promise.")
	end,

	ShouldCancelAllAnyParticipants = function()
		local cancelledFlags = { false, false }
		local makePending = function(index)
			return Promise.new(function(_, _, onCancel)
				onCancel(function()
					cancelledFlags[index] = true
				end)
			end)
		end

		local aggregate = Promise.Any({
			makePending(1),
			makePending(2),
		})

		task.defer(function()
			Promise.Cancel(aggregate, "stop")
		end)

		local result = Promise.GetAwaitResult(aggregate)
		assert(not result.Success, "Cancelled Promise.any should reject.")
		assert(Promise.Error.IsKind(result.Error, Promise.Error.Kind.AlreadyCancelled), "Cancellation should surface AlreadyCancelled kind.")
		assert(cancelledFlags[1] and cancelledFlags[2], "All pending inputs should receive cancellation callbacks.")
	end,

	ShouldCancelRetryWithDelayDuringWait = function()
		local attempts = 0
		local cancelled = false
		local retrying = Promise.RetryWithDelay(function()
			attempts += 1
			return Promise.new(function(_, _, onCancel)
				onCancel(function()
					cancelled = true
				end)
				return Promise.Reject("try-again")
			end)
		end, 3, 0.1)

		task.defer(function()
			Promise.Cancel(retrying, "stop")
		end)

		local result = Promise.GetAwaitResult(retrying)
		assert(not result.Success, "Cancelled retry with delay should reject.")
		assert(cancelled, "Cancellation should propagate to in-flight attempt.")
		assert(attempts == 1, `Expected to stop after first attempt, got {attempts}.`)
	end,

	ShouldInvokeCancelHookForTimeout = function()
		local cancelled = false
		local slow = Promise.new(function(_, _, onCancel)
			onCancel(function()
				cancelled = true
			end)
		end)

		local timed = Promise.Timeout(slow, 0.01)
		local result = Promise.GetAwaitResult(timed)

		assert(not result.Success, "Promise.Timeout should reject when inner promise never resolves.")
		assert(Promise.Error.IsKind(result.Error, Promise.Error.Kind.TimedOut), "Timeout should surface TimedOut kind.")
		assert(cancelled, "Timeout should trigger cancellation hook on the inner promise.")
	end,
})
