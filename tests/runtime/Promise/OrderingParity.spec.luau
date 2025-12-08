local Promise = CS.Promise

local function assertCondition(condition, message)
	if not condition then
		error(message, 2)
	end
end

CS.defineGlobal("RuntimeSpecs.PromiseOrderingSpec", {
	ShouldMaintainChainedResolutionOrder = function()
		local log = {}

		local chained = Promise.Resolve(0)
			:Then(function(value)
				table.insert(log, value)
				return Promise.Delay(0.01, value + 1)
			end)
			:Then(function(value)
				table.insert(log, value)
				return Promise.Resolve(value + 1)
			end)
			:Then(function(value)
				table.insert(log, value)
				return value
			end)

		local result = Promise.GetAwaitResult(chained)
		assertCondition(result.Success, "Chained promise should resolve.")
		assertCondition(#log == 3, `Expected 3 log entries, got {#log}.`)
		assertCondition(
			log[1] == 0 and log[2] == 1 and log[3] == 2,
			`Unexpected chained ordering: {table.concat(log, ", ")}`
		)
	end,

	ShouldInvokeParallelThensInRegistrationOrder = function()
		local log = {}
		local root = Promise.Resolve("seed")

		local first = root:Then(function(value)
			table.insert(log, value .. ":first")
			return value
		end)

		local second = root:Then(function(value)
			table.insert(log, value .. ":second")
			return value
		end)

		local combined = Promise.All({ first, second })
		local result = Promise.GetAwaitResult(combined)
		assertCondition(result.Success, "Parallel thens should resolve collectively.")
		assertCondition(#log == 2, `Expected 2 log entries, got {#log}.`)
		assertCondition(
			log[1] == "seed:first" and log[2] == "seed:second",
			`Unexpected parallel ordering: {table.concat(log, ", ")}`
		)
	end,
})

