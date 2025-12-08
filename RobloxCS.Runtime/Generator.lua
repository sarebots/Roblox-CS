local Generator = {}

type GeneratorState<T> = {
	buffer: { T },
	closed: boolean,
	co: thread?,
	current: T?,
}

local CHANNEL_PAUSE = nil

local function assertState(state: any)
	if typeof(state) ~= "table" or typeof(state.buffer) ~= "table" then
		error("Generator state is invalid")
	end
end

function Generator.newState<T>()
	return {
		buffer = {},
		closed = false,
		co = nil,
	} :: GeneratorState<T>
end

local function ensureCoroutine<T>(state: GeneratorState<T>, producer: () -> ())
	if state.co ~= nil then
		return
	end

	state.co = coroutine.create(function()
		producer()
		state.closed = true
	end)
end

function Generator.yieldValue<T>(state: GeneratorState<T>, value: T)
	assertState(state)
	state.current = value
	coroutine.yield(CHANNEL_PAUSE)
end

function Generator.close(state: GeneratorState<any>)
	assertState(state)
	state.closed = true
end

function Generator.consume<T>(state: GeneratorState<T>, producer: () -> (), consumeFn: (T) -> ())
	assertState(state)
	ensureCoroutine(state, producer)

	while state.co ~= nil and coroutine.status(state.co) ~= "dead" do
		local ok, err = coroutine.resume(state.co)
		if not ok then
			error(err)
		end

		if state.current ~= nil then
			consumeFn(state.current)
			state.current = nil
		end

		if state.closed then
			break
		end
	end
end

function Generator.toEnumerator<T>(state: GeneratorState<T>, producer: () -> ())
	assertState(state)

	local enumeratorState = {
		_items = {},
		_isAdvanced = true,
		_index = 0,
		__producer = producer,
	}

	function enumeratorState:MoveNext()
		local advancedItem
		Generator.consume(state, self.__producer, function(value)
			advancedItem = value
		end)

		if advancedItem == nil then
			return false
		end

		self.Current = advancedItem
		return true
	end

	return enumeratorState
end

return Generator
