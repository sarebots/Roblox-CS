local Promise = CS.Promise

local capturedUnhandledMessages = {}
local capturedUnhandledErrors = {}
Promise.OnUnhandledRejection(function(_, errorValue)
	table.insert(capturedUnhandledErrors, errorValue)
	table.insert(capturedUnhandledMessages, Promise.Error.GetMessage(errorValue) or tostring(errorValue))
end)

local function assert(condition, message)
	if not condition then
		error(message, 2)
	end
end

local function collectAllSettledEntries(entries)
	local resolved = nil
	local rejected = nil

	if type(entries) == "table" then
		resolved = entries[1] or entries["1"]
		rejected = entries[2] or entries["2"]
	end

	return resolved, rejected
end

CS.defineGlobal("RuntimeSpecs.PromiseAggregatesSpec", {
	ShouldShapeAllSettledResults = function()
		table.clear(capturedUnhandledMessages)
		table.clear(capturedUnhandledErrors)

		local settled = Promise.allSettled({
			Promise.Resolve("ok"),
			Promise.Reject("nope"),
		})

		local result = Promise.GetAwaitResult(settled)
		assert(result.Success, "Promise.allSettled should resolve")

		local resolvedEntry, rejectedEntry = collectAllSettledEntries(result.Value)
		assert(type(resolvedEntry) == "table", "Expected resolved entry table")
		assert(type(rejectedEntry) == "table", "Expected rejected entry table")

		assert(resolvedEntry.status == Promise.Status.Resolved, "Resolved entry should have status Resolved")
		assert(resolvedEntry.value == "ok", "Resolved entry should contain resolved value")

		assert(rejectedEntry.status == Promise.Status.Rejected, "Rejected entry should have status Rejected")
		assert(Promise.Error.Is(rejectedEntry.reason), "Rejected entry reason should be Promise.Error")
		assert(Promise.Error.GetMessage(rejectedEntry.reason) == "nope", "Rejected entry should preserve rejection message")
		assert(capturedUnhandledMessages[#capturedUnhandledMessages] == "nope", "Unhandled rejection callback should capture original reason")
	end,

	ShouldSurfaceAnyFinalRejection = function()
		table.clear(capturedUnhandledMessages)
		table.clear(capturedUnhandledErrors)

		local rejection = Promise.any({
			Promise.Reject("a"),
			Promise.Reject("b"),
		})

		local result = Promise.GetAwaitResult(rejection)
		assert(not result.Success, "Promise.any should reject when all inputs reject")
		assert(Promise.Error.Is(result.Error), "Promise.any rejection should be Promise.Error")
		assert(Promise.Error.GetMessage(result.Error) == "b", "Promise.any should forward the final rejection payload")
		local chain = Promise.Error.GetErrorChain(result.Error)
		assert(#chain == 2, `Expected error chain length of 2, got {#chain}`)
		assert(Promise.Error.GetMessage(chain[#chain]) == "a", "Error chain should retain earliest rejection payload")
		assert(capturedUnhandledMessages[#capturedUnhandledMessages] == "b", "Unhandled rejection callback should capture final rejection payload")
		local unhandledError = capturedUnhandledErrors[#capturedUnhandledErrors]
		assert(unhandledError == result.Error, "Unhandled rejection should receive the aggregated error instance")
		local unhandledChain = Promise.Error.GetErrorChain(unhandledError)
		assert(#unhandledChain == #chain, "Unhandled error chain should match result chain length")

		local lateInvoked = false
		local unsubscribe = Promise.OnUnhandledRejection(function(_, errorValue)
			if errorValue == result.Error then
				lateInvoked = true
			end
		end)

		Promise.GetAwaitResult(Promise.Delay(0.05))
		unsubscribe()

		assert(lateInvoked, "Handlers registered after rejection should still observe the aggregated error")
	end,
})
