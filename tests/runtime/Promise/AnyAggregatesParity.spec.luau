local Promise = CS.Promise

local function assertCondition(condition, message)
	if not condition then
		error(message, 2)
	end
end

CS.defineGlobal("RuntimeSpecs.PromiseAnyAggregateSpec", {
	ShouldExposeAllRejectionsInErrorChain = function()
		local result = Promise.GetAwaitResult(Promise.Any({
			Promise.Reject("alpha"),
			Promise.Reject("beta"),
			Promise.Reject("gamma"),
		}))

		assertCondition(not result.Success, "Promise.any should reject when every input rejects.")
		assertCondition(Promise.Error.Is(result.Error), "Rejected result should be a Promise.Error.")

		local chain = Promise.Error.GetErrorChain(result.Error)
		assertCondition(#chain == 3, `Expected 3 errors in chain, got {#chain}.`)

		assertCondition(
			Promise.Error.GetMessage(chain[1]) == "gamma",
			"Head of chain should reflect the final rejection payload."
		)
		assertCondition(
			Promise.Error.GetMessage(chain[#chain]) == "alpha",
			"Tail of chain should capture the first rejection payload."
		)
	end,

	ShouldRejectOnlyWhenAllInputsReject = function()
		local result = Promise.GetAwaitResult(Promise.Any({
			Promise.Resolve("ok"),
			Promise.Reject("fail"),
		}))

		assert(result.Success, "Promise.any should resolve when any input resolves")
		assert(result.Value == "ok", "Resolved value should match the first resolution")
	end,
})


	ShouldRejectOnlyWhenAllInputsReject = function()
		local result = Promise.GetAwaitResult(Promise.Any({
			Promise.Resolve("ok"),
			Promise.Reject("fail"),
		}))

		assert(result.Success, "Promise.any should resolve when any input resolves")
		assert(result.Value == "ok", "Resolved value should match the first resolution")
	end,
