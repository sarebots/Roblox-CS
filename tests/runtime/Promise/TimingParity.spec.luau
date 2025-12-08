local Promise = CS.Promise

local function assertClose(actual, expected, tolerance, message)
	if math.abs(actual - expected) > tolerance then
		error(string.format("%s (expected %.3f, got %.3f)", message, expected, actual), 2)
	end
end

CS.defineGlobal("RuntimeSpecs.PromiseTimingSpec", {
	ShouldRespectRetryWithDelayTiming = function()
		local attempts = 0
		local start = os.clock()

		local result = Promise.GetAwaitResult(Promise.RetryWithDelay(function()
			attempts += 1
			if attempts < 2 then
				return Promise.Reject("again")
			end
			return Promise.Resolve("ok")
		end, 2, 0.05))

		local elapsed = os.clock() - start
		assert(result.Success, "RetryWithDelay should eventually resolve")
		assertClose(elapsed, 0.05, 0.02, "RetryWithDelay should wait close to requested delay")
		assert(attempts == 2, `Expected two attempts, got {attempts}`)
	end,
})
