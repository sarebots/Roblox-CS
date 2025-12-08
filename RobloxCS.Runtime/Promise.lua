--!strict

local Promise = {}
Promise.__index = Promise

Promise.Status = {
	Started = "Started",
	Resolved = "Resolved",
	Rejected = "Rejected",
	Cancelled = "Cancelled",
}

Promise._unhandledRejectionCallbacks = {}

local PromiseErrorKind = {
	ExecutionError = "ExecutionError",
	AlreadyCancelled = "AlreadyCancelled",
	NotResolvedInTime = "NotResolvedInTime",
	TimedOut = "TimedOut",
}

local PromiseError = {}
PromiseError.__index = PromiseError
PromiseError.Kind = PromiseErrorKind

function PromiseError.new(options, parent)
	options = options or {}

	local message = options.error
	if typeof(message) ~= "string" or message == "" then
		message = "[This error has no error text.]"
	end

	return setmetatable({
		error = message,
		trace = options.trace,
		context = options.context,
		kind = options.kind,
		parent = parent,
		_createdTrace = debug.traceback(nil, 2),
		_kindOverride = options.kind,
	}, PromiseError)
end

function PromiseError.is(value)
	return type(value) == "table" and getmetatable(value) == PromiseError
end

PromiseError.Is = PromiseError.is

function PromiseError.isKind(value, kind)
	if not PromiseError.is(value) then
		return false
	end

	local currentKind = value._kindOverride or value.kind
	if currentKind == kind then
		return true
	end

	if currentKind == PromiseError.Kind.ExecutionError and kind == PromiseError.Kind.AlreadyCancelled then
		local message = value.error
		if type(message) == "string" and string.find(string.lower(message), "cancel") then
			return true
		end
	end

	return false
end

PromiseError.IsKind = PromiseError.isKind

function PromiseError.getKind(value)
	if PromiseError.is(value) then
		local currentKind = value._kindOverride or value.kind
		if currentKind == PromiseError.Kind.ExecutionError then
			local message = value.error
			if type(message) == "string" and string.find(string.lower(message), "cancel") then
				return PromiseError.Kind.AlreadyCancelled
			end
		end
		return currentKind
	end

	return nil
end

PromiseError.GetKind = PromiseError.getKind

function PromiseError.getMessage(value)
	if PromiseError.is(value) then
		return value.error
	end

	return nil
end

PromiseError.GetMessage = PromiseError.getMessage

function PromiseError.extend(self, options)
	assert(PromiseError.is(self), "Promise.Error.extend expects a Promise.Error object")
	options = options or {}
	options.kind = options.kind or self.kind
	options.context = options.context or self.context
	local extended = PromiseError.new(options, self)
	if not extended._kindOverride then
		extended._kindOverride = self._kindOverride or extended.kind
	end
	return extended
end

PromiseError.Extend = PromiseError.extend

function PromiseError.getErrorChain(self)
	assert(PromiseError.is(self), "Promise.Error.getErrorChain expects a Promise.Error object")

	local chain = { self }
	while chain[#chain].parent do
		table.insert(chain, chain[#chain].parent)
	end

	return chain
end

PromiseError.GetErrorChain = PromiseError.getErrorChain

function PromiseError:__tostring()
	local pieces = { string.format("-- Promise.Error(%s) --", self.kind or "?") }
	for _, runtimeError in ipairs(PromiseError.getErrorChain(self)) do
		local trace = runtimeError.trace or runtimeError.error
		local context = runtimeError.context
		if context and context ~= "" then
			trace = string.format("%s\n%s", trace, context)
		end
		table.insert(pieces, trace)
	end

	return table.concat(pieces, "\n")
end

Promise.Error = PromiseError

local DEFAULT_TIMEOUT_MESSAGE = "Promise timed out"

local function packArgs(...)
	return table.pack(...)
end

local function unpackArgs(args)
	if args == nil then
		return
	end

	return table.unpack(args, 1, args.n or #args)
end

local function isCallable(value)
	return type(value) == "function"
end

local function warnHookError(err)
	warn(`Promise cancellation hook threw: {err}`)
end

local function defer(callback)
	if task and task.defer then
		task.defer(callback)
	elseif task and task.spawn then
		task.spawn(callback)
	else
		local thread = coroutine.create(callback)
		coroutine.resume(thread)
	end
end

local function coercePromiseList(methodName, ...)
	local count = select("#", ...)
	if count == 0 then
		return {}
	end

	local first = ...
	if count == 1 and type(first) == "table" and not Promise.is(first) then
		return first
	end

	local list = table.create(count)
	for index = 1, count do
		list[index] = select(index, ...)
	end

	return list
end

local function wrapToPromise(value)
	if Promise.is(value) then
		return value
	end

	return Promise.resolve(value)
end

function Promise.is(value)
	return type(value) == "table" and getmetatable(value) == Promise
end

local function createContextMessage(traceback)
	if traceback and traceback ~= "" then
		return "Promise created at:\n\n" .. traceback
	end

	return nil
end

local function toPromiseError(err, options)
	options = options or {}

	if Promise.Error.is(err) then
		if options.context and (err.context == nil or err.context == "") then
			err = Promise.Error.extend(err, {
				context = options.context,
			})
		end

		return err
	end

	local message = err
	if typeof(message) ~= "string" then
		message = tostring(message)
	end

	local trace = options.trace or debug.traceback(tostring(message), 2)

	return Promise.Error.new({
		error = message,
		kind = options.kind or Promise.Error.Kind.ExecutionError,
		trace = trace,
		context = options.context,
	}, options.parent)
end

local function executionError(err, contextTrace)
	return toPromiseError(err, {
		kind = Promise.Error.Kind.ExecutionError,
		context = createContextMessage(contextTrace),
	})
end

function Promise.new(executor)
	assert(isCallable(executor), "Promise executor must be a function")

	local creationTrace = debug.traceback(nil, 2)

	local self = setmetatable({
		_status = Promise.Status.Started,
		_handlers = {},
		_consumers = {},
		_cancelHooks = {},
		_cancelReason = nil,
		_values = nil,
		_error = nil,
		_parent = nil,
		_sourceTraceback = creationTrace,
		_hasCatch = false,
		_unhandledScheduled = false,
	}, Promise)

	local function resolve(...)
		if self._status ~= Promise.Status.Started then
			return
		end

		self:_finalize(Promise.Status.Resolved, packArgs(...))
	end

	local function reject(err)
		if self._status ~= Promise.Status.Started then
			return
		end

		self:_finalize(Promise.Status.Rejected, toPromiseError(err, {
			context = createContextMessage(self._sourceTraceback),
		}))
	end

	local function onCancel(hook)
		if hook == nil then
			return self._status == Promise.Status.Cancelled
		end

		if self._status == Promise.Status.Cancelled then
			hook(self._cancelReason)
			return true
		end

		table.insert(self._cancelHooks, hook)
		return false
	end

	local ok, err = pcall(function()
		executor(resolve, reject, onCancel)
	end)

	if not ok then
		reject(executionError(err, creationTrace))
	end

	return self
end

function Promise.resolve(...)
	local results = table.pack(...)
	if results.n == 1 then
		local value = results[1]
		if Promise.is(value) then
			return value
		end
	end

	return Promise.new(function(resolve)
		resolve(table.unpack(results, 1, results.n))
	end)
end

function Promise.reject(err)
	return Promise.new(function(_, reject)
		reject(err)
	end)
end

function Promise.delay(seconds, value)
	return Promise.new(function(resolve, _, onCancel)
		if task == nil then
			warn("[Promise] task global is nil during Promise.delay")
		end
		local thread = task.delay(seconds, function()
			resolve(value)
		end)

		onCancel(function()
			task.cancel(thread)
		end)
	end)
end

function Promise.async(callback)
	assert(isCallable(callback), "Promise.async expects a function")

	return function(...)
		local length = select("#", ...)
		local args = { ... }
		return Promise.new(function(resolve, reject)
			local results = { pcall(callback, table.unpack(args, 1, length)) }
			local ok = table.remove(results, 1)
			if ok then
				resolve(table.unpack(results, 1, #results))
			else
				reject(results[1])
			end
		end)
	end
end

function Promise.try(callback, ...)
	assert(isCallable(callback), "Promise.try expects a function")

	local length = select("#", ...)
	local args = { ... }

	return Promise.new(function(resolve, reject)
		local results = { pcall(callback, table.unpack(args, 1, length)) }
		local ok = table.remove(results, 1)
		if ok then
			resolve(table.unpack(results, 1, #results))
		else
			reject(results[1])
		end
	end)
end

function Promise.all(...)
	local promises = coercePromiseList("Promise.all", ...)
	local total = #promises

	return Promise.new(function(resolve, reject, onCancel)
		if total == 0 then
			resolve({})
			return
		end

		local remaining = total
		local cancelled = false
		local results = table.create(total)
		local observers = table.create(total)

		local function finalize()
			if remaining ~= 0 then
				return
			end

			local values = table.create(total)
			for index = 1, total do
				local payload = results[index]
				if payload ~= nil then
					if payload.n <= 1 then
						values[index] = payload[1]
					else
						local multi = table.create(payload.n)
						for i = 1, payload.n do
							multi[i] = payload[i]
						end
						values[index] = multi
					end
				end
			end

			resolve(values)
		end

		local function cancelAll(reason)
			for _, observer in ipairs(observers) do
				if Promise.is(observer) then
					observer:cancel(reason)
				end
			end

			for _, source in ipairs(promises) do
				if Promise.is(source) then
					source:cancel(reason)
				end
			end
		end

		onCancel(function(reason)
			if cancelled then
				return
			end

			cancelled = true
			cancelAll(reason or "Promise.all cancelled")
		end)

		for index, source in ipairs(promises) do
			local promise = wrapToPromise(source)

			local observer
			observer = promise:andThen(function(...)
				if cancelled then
					return ...
				end

				results[index] = packArgs(...)
				remaining -= 1
				finalize()

				return ...
			end, function(err)
				if cancelled then
					return Promise.reject(err)
				end

				cancelled = true
				cancelAll(err)
				reject(err)

				return Promise.reject(err)
			end)

			observers[index] = observer
		end
	end)
end

function Promise.race(...)
	local promises = coercePromiseList("Promise.race", ...)

	return Promise.new(function(resolve, reject, onCancel)
		if #promises == 0 then
			resolve(nil)
			return
		end

		local settled = false
		local observers = {}

		onCancel(function(reason)
			if settled then
				return
			end

			settled = true
			for _, observer in ipairs(observers) do
				observer:cancel(reason)
			end

			for _, source in ipairs(promises) do
				if Promise.is(source) then
					source:cancel(reason)
				end
			end
		end)

		for _, source in ipairs(promises) do
			local promise = wrapToPromise(source)
			local observer
			observer = promise:andThen(function(...)
				if settled then
					return ...
				end

				settled = true
				resolve(...)
				return ...
			end, function(err)
				if settled then
					return Promise.reject(err)
				end

				settled = true
				reject(err)
				return Promise.reject(err)
			end)

			table.insert(observers, observer)
		end
	end)
end

function Promise.any(...)
	local promises = coercePromiseList("Promise.any", ...)
	local total = #promises

	return Promise.new(function(resolve, reject, onCancel)
		if total == 0 then
			reject("Promise.any requires at least one promise")
			return
		end

		local remaining = total
		local resolved = false
		local rejectionErrors = {}

		local function recordError(err)
			local promiseError = toPromiseError(err)
			table.insert(rejectionErrors, promiseError)
			return promiseError
		end

		onCancel(function(reason)
			for _, source in ipairs(promises) do
				if Promise.is(source) then
					source:cancel(reason)
				end
			end
		end)

		for _, source in ipairs(promises) do
			local promise = wrapToPromise(source)
			promise:andThen(function(...)
				if resolved then
					return ...
				end

				resolved = true
				resolve(...)
				return ...
			end, function(err)
				local errorValue = recordError(err)
				remaining -= 1
				if remaining == 0 and not resolved then
					local aggregate = rejectionErrors[1]

					if #rejectionErrors > 1 then
						for index = 2, #rejectionErrors do
							local current = rejectionErrors[index]
							aggregate = Promise.Error.new({
								error = Promise.Error.GetMessage(current),
								kind = Promise.Error.GetKind(current),
								trace = current.trace,
								context = current.context,
							}, aggregate)
						end
					end

					reject(aggregate)
					return Promise.reject(errorValue)
				end

				return Promise.reject(errorValue)
			end)
		end
	end)
end

function Promise.allSettled(...)
	local promises = coercePromiseList("Promise.allSettled", ...)
	local total = #promises

	return Promise.new(function(resolve, _, onCancel)
		if total == 0 then
			resolve({})
			return
		end

		local remaining = total
		local results = table.create(total)
		local observers = table.create(total)

		onCancel(function(reason)
			for _, observer in ipairs(observers) do
				if Promise.is(observer) then
					observer:cancel(reason)
				end
			end

			for _, source in ipairs(promises) do
				if Promise.is(source) then
					source:cancel(reason)
				end
			end
		end)

		local function finalize(index, entry)
			results[index] = entry
			remaining -= 1
			if remaining == 0 then
				resolve(results)
			end
		end

		for index, source in ipairs(promises) do
			local promise = wrapToPromise(source)
			local observer
			observer = promise:andThen(function(...)
				local count = select("#", ...)
				local value
				if count == 0 then
					value = nil
				elseif count == 1 then
					value = select(1, ...)
				else
					value = { ... }
				end

				finalize(index, {
					status = Promise.Status.Resolved,
					value = value,
				})

				return ...
			end, function(err)
				finalize(index, {
					status = Promise.Status.Rejected,
					reason = err,
				})

				return Promise.reject(err)
			end)

			observers[index] = observer
		end
	end)
end

function Promise.timeout(promise, seconds, rejectionValue)
	assert(Promise.is(promise), "Promise.Timeout expects a Promise")
	assert(type(seconds) == "number" and seconds >= 0, "Promise.Timeout expects a non-negative number of seconds")
	assert(task ~= nil, "Promise.Timeout requires the global 'task' library to be available")

	local timeoutMessage = rejectionValue or string.format("%s after %.2f seconds", DEFAULT_TIMEOUT_MESSAGE, seconds)
	local timeoutError
	if Promise.Error.is(rejectionValue) then
		timeoutError = Promise.Error.extend(rejectionValue, {
			kind = Promise.Error.Kind.TimedOut,
		})
		timeoutMessage = Promise.Error.getMessage(timeoutError)
	else
		timeoutError = Promise.Error.new({
			error = timeoutMessage,
			kind = Promise.Error.Kind.TimedOut,
		})
	end

	local racePromise = Promise.race(
		Promise.delay(seconds):andThen(function()
			promise:cancel(timeoutMessage)
			return Promise.reject(timeoutError)
		end),
		promise
	)

	racePromise._timeoutKind = Promise.Error.Kind.TimedOut
	racePromise._timeoutMessage = timeoutMessage

	return racePromise
end

function Promise.retry(callback, times, ...)
	assert(isCallable(callback), "Promise.retry expects a callback")
	if times == nil then
		times = 1
	end
	assert(type(times) == "number", "Promise.retry expects retry count to be a number")
	assert(times >= 0, "Promise.retry expects retry count to be >= 0")

	local args = { ... }
	local length = select("#", ...)

	local function invoke()
		local results = { pcall(callback, table.unpack(args, 1, length)) }
		local ok = table.remove(results, 1)
		if ok then
			return Promise.resolve(table.unpack(results, 1, #results))
		else
			return Promise.reject(results[1])
		end
	end

	local function attempt(remaining)
		return invoke():catch(function(err)
			if remaining > 0 then
				return attempt(remaining - 1)
			end

			return Promise.reject(err)
		end)
	end

	return attempt(times)
end

function Promise.retryWithDelay(callback, times, seconds, ...)
	assert(isCallable(callback), "Promise.retryWithDelay expects a callback")
	assert(type(times) == "number", "Promise.retryWithDelay expects retry count to be a number")
	assert(times >= 0, "Promise.retryWithDelay expects retry count to be >= 0")
	assert(type(seconds) == "number", "Promise.retryWithDelay expects seconds to be a number")

	local args = { ... }
	local length = select("#", ...)

	local function invoke()
		local results = { pcall(callback, table.unpack(args, 1, length)) }
		local ok = table.remove(results, 1)
		if ok then
			return Promise.resolve(table.unpack(results, 1, #results))
		else
			return Promise.reject(results[1])
		end
	end

	local function attempt(remaining)
		return invoke():catch(function(err)
			if remaining > 0 then
				local delaySuccess, delayError = Promise.delay(seconds):await()
				if not delaySuccess then
					return Promise.reject(delayError)
				end

				return attempt(remaining - 1)
			end

			return Promise.reject(err)
		end)
	end

	return attempt(times)
end

function Promise.fromEvent(event, predicate)
	if type(event) ~= "table" or type(event.Connect) ~= "function" then
		error("Promise.fromEvent expects an event with a Connect method")
	end

	predicate = predicate or function()
		return true
	end

	return Promise.new(function(resolve, _, onCancel)
		local connection
		local shouldDisconnect = false

		local function disconnect()
			if connection == nil then
				return
			end

			local connectionType = typeof(connection)
			if connectionType == "function" then
				connection()
			elseif connectionType == "table" then
				local disconnectMethod = connection.Disconnect or connection.disconnect
				if typeof(disconnectMethod) == "function" then
					disconnectMethod(connection)
				end
			end

			connection = nil
		end

		onCancel(disconnect)

		connection = event:Connect(function(...)
			local ok = predicate(...)

			if ok == true then
				resolve(...)

				if connection == nil then
					shouldDisconnect = true
				else
					disconnect()
				end
			elseif ok == false then
				return
			elseif ok ~= nil then
				error("Promise.fromEvent predicate should always return a boolean")
			end
		end)

		if shouldDisconnect and connection ~= nil then
			disconnect()
		end
	end)
end

function Promise:_finalize(status, payload)
	self._status = status

	if status == Promise.Status.Resolved then
		self._values = payload
	elseif status == Promise.Status.Rejected then
		self._error = toPromiseError(payload, {
			context = createContextMessage(self._sourceTraceback),
		})
	end

	self:_flushHandlers()

	if status == Promise.Status.Rejected then
		self:_scheduleUnhandledRejection()
	end
end

function Promise:_markHandled()
	if self._hasCatch then
		return
	end

	self._hasCatch = true
	self._unhandledScheduled = false
end

function Promise:_scheduleUnhandledRejection()
	if self._status ~= Promise.Status.Rejected or self._hasCatch or self._unhandledScheduled then
		return
	end

	self._unhandledScheduled = true

	defer(function()
		if self._hasCatch or self._status ~= Promise.Status.Rejected then
			self._unhandledScheduled = false
			return
		end

		self._unhandledScheduled = false

		for _, callback in ipairs(Promise._unhandledRejectionCallbacks) do
			local ok, err = pcall(callback, self, self._error)
			if not ok then
				warn(`[Promise] onUnhandledRejection callback failed: {err}`)
			end
		end
	end)
end

function Promise:_enqueue(onResolved, onRejected, resolve, reject)
	local handler = {
		onResolved = onResolved,
		onRejected = onRejected,
		resolve = resolve,
		reject = reject,
	}

	table.insert(self._handlers, handler)

	if self._status ~= Promise.Status.Started then
		self:_runHandler(handler)
	end
end

function Promise:_runHandler(handler)
	local status = self._status

	if status == Promise.Status.Resolved then
		if not isCallable(handler.onResolved) then
			handler.resolve(unpackArgs(self._values))
			return
		end

		local ok, result = pcall(function()
			return packArgs(handler.onResolved(unpackArgs(self._values)))
		end)

		if ok then
			self:_resolveContinuation(result, handler.resolve, handler.reject)
		else
			handler.reject(result)
		end
	elseif status == Promise.Status.Rejected or status == Promise.Status.Cancelled then
		local reason = self._error or self._cancelReason

		if not isCallable(handler.onRejected) then
			handler.reject(reason)
			return
		end

		local ok, result = pcall(function()
			return packArgs(handler.onRejected(reason))
		end)

		if ok then
			self:_resolveContinuation(result, handler.resolve, handler.reject)
		else
			handler.reject(result)
		end
	end
end

function Promise:_resolveContinuation(results, resolve, reject)
	local length = results.n or #results
	local first = results[1]

	if length == 1 and Promise.is(first) then
		first:andThen(function(...)
			resolve(...)
		end, function(err)
			reject(err)
		end)

		return
	end

	resolve(unpackArgs(results))
end

function Promise:_flushHandlers()
	for _, handler in ipairs(self._handlers) do
		self:_runHandler(handler)
	end

	table.clear(self._handlers)
end

function Promise:_runCancelHooks()
	for _, hook in ipairs(self._cancelHooks) do
		local ok, err = pcall(hook, self._cancelReason)
		if not ok then
			warnHookError(err)
		end
	end

	table.clear(self._cancelHooks)
end

function Promise:_addConsumer(consumer)
	table.insert(self._consumers, consumer)
end

function Promise:_removeConsumer(consumer)
	for index, existing in ipairs(self._consumers) do
		if existing == consumer then
			table.remove(self._consumers, index)
			break
		end
	end
end

function Promise:andThen(onResolved, onRejected)
    self:_markHandled()

    local child
	child = Promise.new(function(resolve, reject, onCancel)
		onCancel(function()
			self:_removeConsumer(child)
		end)

		self:_enqueue(onResolved, onRejected, resolve, reject)
	end)

	child._parent = self
	self:_addConsumer(child)

	return child
end

function Promise:catch(onRejected)
	return self:andThen(nil, onRejected)
end

function Promise:finally(onFinally)
    self:_markHandled()

    assert(onFinally ~= nil, "Promise.finally requires a callback")

    return self:andThen(function(...)
		if isCallable(onFinally) then
			onFinally()
		end

		return ...
	end, function(err)
		if isCallable(onFinally) then
			onFinally()
		end

		return Promise.reject(err)
	end)
end

function Promise:await()
    self:_markHandled()

    if self._status == Promise.Status.Resolved then
        return true, unpackArgs(self._values)
	elseif self._status == Promise.Status.Rejected then
		return false, self._error
	elseif self._status == Promise.Status.Cancelled then
		return false, self._error or Promise.Error.new({
			error = self._cancelReason or "Promise was cancelled",
			kind = Promise.Error.Kind.AlreadyCancelled,
		})
	end

	local thread = coroutine.running()
	self:_enqueue(function(...)
		task.spawn(thread, true, ...)
	end, function(err)
		task.spawn(thread, false, err)
	end, function() end, function() end)

	return coroutine.yield()
end

function Promise.getAwaitResult(promise)
    assert(Promise.is(promise), "Promise.getAwaitResult expects a Promise")

    promise:_markHandled()

    local results = { promise:await() }
	local success = table.remove(results, 1)

	if success then
		local value
		if #results == 0 then
			value = nil
		elseif #results == 1 then
			value = results[1]
		else
			value = results
		end

		return {
			Success = true,
			Value = value,
			Error = nil,
		}
	end

	local errorValue
	if promise._status == Promise.Status.Cancelled then
		errorValue = Promise.Error.new({
			error = promise._cancelReason or "Promise was cancelled",
			kind = Promise.Error.Kind.AlreadyCancelled,
		})
	else
		errorValue = promise._error or results[1]
		if not Promise.Error.is(errorValue) then
			errorValue = Promise.Error.new({
				error = errorValue,
				kind = Promise.Error.Kind.ExecutionError,
			})
		end
		if promise._timeoutKind ~= nil then
			errorValue = Promise.Error.extend(errorValue, {
				kind = promise._timeoutKind,
			})
		end
	end

	return {
		Success = false,
		Value = nil,
		Error = errorValue,
	}
end

function Promise:cancel(reason)
	if self._status ~= Promise.Status.Started then
		return
	end

	self._status = Promise.Status.Cancelled
	self._cancelReason = reason or "Promise was cancelled"
	local cancelError = Promise.Error.new({
		error = self._cancelReason,
		kind = Promise.Error.Kind.AlreadyCancelled,
		context = createContextMessage(self._sourceTraceback),
	})
	self._error = cancelError

	self:_runCancelHooks()

	for _, consumer in ipairs(self._consumers) do
		consumer:cancel(self._cancelReason)
	end

	self:_flushHandlers()

	if self._parent ~= nil then
		self._parent:_removeConsumer(self)
	end
end

function Promise.onUnhandledRejection(callback)
	assert(isCallable(callback), "Promise.onUnhandledRejection expects a function")

	table.insert(Promise._unhandledRejectionCallbacks, callback)

	return function()
		for index, registered in ipairs(Promise._unhandledRejectionCallbacks) do
			if registered == callback then
				table.remove(Promise._unhandledRejectionCallbacks, index)
				break
			end
		end
	end
end

Promise.Resolve = Promise.resolve
Promise.Reject = Promise.reject
Promise.Delay = Promise.delay
Promise.All = Promise.all
Promise.Race = Promise.race
Promise.Any = Promise.any
Promise.AllSettled = Promise.allSettled
Promise.Async = Promise.async
Promise.Then = Promise.andThen
Promise.Catch = Promise.catch
Promise.Finally = Promise.finally
Promise.Await = Promise.await
Promise.GetAwaitResult = Promise.getAwaitResult
Promise.Cancel = Promise.cancel
Promise.Timeout = Promise.timeout
Promise.Retry = Promise.retry
Promise.RetryWithDelay = Promise.retryWithDelay
Promise.Try = Promise.try
Promise.FromEvent = Promise.fromEvent
Promise.OnUnhandledRejection = Promise.onUnhandledRejection

return Promise
