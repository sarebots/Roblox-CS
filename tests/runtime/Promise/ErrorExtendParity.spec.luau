local Promise = CS.Promise

local function assertCondition(condition, message)
	if not condition then
		error(message, 2)
	end
end

CS.defineGlobal("RuntimeSpecs.PromiseErrorParitySpec", {
	ShouldExtendErrorPreserveParentChain = function()
		local baseError = Promise.Error.new({
			error = "root",
			kind = Promise.Error.Kind.ExecutionError,
			context = "base",
		})

		local extended = Promise.Error.extend(baseError, {
			kind = Promise.Error.Kind.AlreadyCancelled,
			context = "child",
		})

		assertCondition(Promise.Error.Is(extended), "Extended value should be a Promise.Error")
		assertCondition(Promise.Error.GetKind(extended) == Promise.Error.Kind.AlreadyCancelled, "Extended error kind should override")

		local chain = Promise.Error.GetErrorChain(extended)
		assertCondition(#chain == 2, "Error chain should contain extended and base entries")
		assertCondition(chain[2] == baseError, "Parent error should be preserved in the chain")
	end,

	ShouldInheritMessageWhenUnset = function()
		local baseError = Promise.Error.new({
			error = "root message",
			kind = Promise.Error.Kind.ExecutionError,
		})

		local extended = Promise.Error.extend(baseError, {
			kind = Promise.Error.Kind.ExecutionError,
		})

		assertCondition(Promise.Error.GetMessage(extended) == "root message", "Extended error should inherit message when none provided")
	end,
})
