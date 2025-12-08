local Promise = CS.Promise

local function assertCondition(condition, message)
	if not condition then
		error(message, 2)
	end
end

CS.defineGlobal("RuntimeSpecs.PromiseTrySpec", {
	ShouldWrapStringErrors = function()
		local outcome = Promise.GetAwaitResult(Promise.Try(function()
			error("boom")
		end))

		assertCondition(not outcome.Success, "Promise.Try with string error should reject")
		assertCondition(Promise.Error.Is(outcome.Error), "Rejection should be Promise.Error")
		assertCondition(
			Promise.Error.IsKind(outcome.Error, Promise.Error.Kind.ExecutionError),
			"String error should map to ExecutionError kind"
		)
		assertCondition(Promise.Error.GetMessage(outcome.Error) == "boom", "Error message should surface original text")
	end,

	ShouldWrapTableErrorsWithStringMessage = function()
		local payload = { reason = "table-error" }
		local expectedMessage = tostring(payload)

		local outcome = Promise.GetAwaitResult(Promise.Try(function()
			error(payload)
		end))

		assertCondition(not outcome.Success, "Promise.Try with table error should reject")
		assertCondition(Promise.Error.Is(outcome.Error), "Table rejection should be wrapped as Promise.Error")
		assertCondition(
			Promise.Error.IsKind(outcome.Error, Promise.Error.Kind.ExecutionError),
			"Table error should map to ExecutionError kind"
		)
		assertCondition(
			Promise.Error.GetMessage(outcome.Error) == expectedMessage,
			"Wrapped table error should stringify the payload"
		)
	end,
})
