--!strict
--!native
local CS = {}

type GlobalNamespace = { [string]: NamespaceMember }
type IEnum<K, V> = { [K & string]: V & number }
type Enum = IEnum<string, number>

export type Namespace = { [string]: any }
type NamespaceMember = Namespace | Class | Enum

CS.globalNamespace = {} :: GlobalNamespace

function CS.defineGlobal(name: string, value: NamespaceMember): ()
	CS.globalNamespace[name] = value
	if _G ~= nil then
		_G[name] = value
	end
end

function CS.getGlobal(name: string): NamespaceMember
	return CS.globalNamespace[name]
end

type IClass<T> = {
	__className: string; -- prob temporary
	new: T;
}

export type Class = IClass<() -> {}>

function CS.is(object: any, class: Class | string): boolean
	if typeof(class) == "table" and typeof(class.__className) == "string" then
		return typeof(object) == "table" and typeof(object.__className) == "string" and object.__className == class.__className
	end

	-- metatable check
	if typeof(object) == "table" then
		object = getmetatable(object)

		while object ~= nil do
			if object == class then
				return true
			end

			local mt = getmetatable(object) :: { __index: {} }
			object = if mt then mt.__index else nil
		end
	end

	if typeof(class) == "string" then
		return if typeof(object) == "Instance" then
			object:IsA(class)
			else
			typeof(object) == class
	end

	return false
end

local PRIMITIVE_LUAU_TYPES = {
	["nil"] = true,
	boolean = true,
	string = true,
	number = true,
	table = true,
	userdata = true,
	["function"] = true,
	thread = true,
	vector = true,
	buffer = true,
}

function CS.typeIs(value: any, typeName: string): boolean
	local inspector = if PRIMITIVE_LUAU_TYPES[typeName] then type else typeof
	return inspector(value) == typeName
end

function CS.classIs(value: any, className: string): boolean
	return value.ClassName == className
end

local function copyTable(source, usePairs)
	local result = {}
	local writeIndex = 1
	if usePairs then
		for _, value in pairs(source) do
			result[writeIndex] = value
			writeIndex += 1
		end
	else
		for _, value in ipairs(source) do
			result[writeIndex] = value
			writeIndex += 1
		end
	end
	return result
end

function CS.iter(iterable: any)
	if typeof(iterable) ~= "table" then
		return {}
	end

	if #iterable > 0 then
		return iterable
	end

	return copyTable(iterable, true)
end

function CS.array_flatten(iterables: any)
	if typeof(iterables) ~= "table" then
		return {}
	end

	local result = {}
	local writeIndex = 1

	for _, inner in ipairs(iterables) do
		if typeof(inner) == "table" then
			for _, value in ipairs(inner) do
				result[writeIndex] = value
				writeIndex += 1
			end
		end
	end

	return result
end

function CS.unpackTuple<K, T>(tuple: { [K & string]: T }): ...T
	local list = {}
	local i: number
	for k, v in tuple do
		i = tonumber(k:sub(5)) or error(`Invalid tuple index: {k}`)
		list[i] = v
	end
	return table.unpack(list)
end

-- Enumerator class
type EnumerationFunction<T> = () -> { () -> T }
export type EnumeratorClass<T> = IClass<(items: { T } | EnumerationFunction<T>) -> IEnumerator<T>>;
export type IEnumerator<T> = EnumeratorClass<T> & {
	_index: number;
	read _isAdvanced: boolean;
	Current: T;
	MoveNext: (IEnumerator<T>) -> ();
}
type Enumerator = IEnumerator<unknown>

local Enumerator
do
	local className = "Enumerator"
	Enumerator = (setmetatable({}, {
		__tostring = function(): string
			return className
		end
	}) :: any) :: Enumerator
	Enumerator.__index = Enumerator
	Enumerator.__className = className

	function Enumerator.new<T>(items: { T } | EnumerationFunction<T>): IEnumerator<T>
		local isAdvanced = typeof(items) == "function"
		return (setmetatable({
			_items = if isAdvanced then (items :: EnumerationFunction<T>)() else items,
			_isAdvanced = isAdvanced,
			_index = 0
		}, Enumerator) :: any) :: IEnumerator<T>
	end

	function Enumerator:MoveNext(): boolean
		self._index += 1
		local gotValue = self._index <= #self._items
		local item = if gotValue then self._items[self._index] else nil
		self.Current = if self._isAdvanced and typeof(item) == "function" then
			item(function()
				self._index = #self._items + 1
			end)
			else
			item
		return gotValue
	end

	function Enumerator:Reset(): ()
		self._index = 0
		self.Current = nil :: any
	end

	function Enumerator:Dispose(): ()
		self._items = nil
		setmetatable(self, nil)
	end
	
	-- internal function to collect into table
	function Enumerator:_collect()
	    local function iter()
             self:MoveNext()
             return self.Current
        end
        
        local result = {}
        for v in iter do
            table.insert(result, v)
        end
        
        return result
    end
end
CS.defineGlobal("Enumerator", Enumerator)
CS.Enumerator = Enumerator

-- Exception class
type ExceptionClass = Class & {
	__index: ExceptionClass;
	new: (message: string?) -> Exception;
}
export type Exception = ExceptionClass & {
	Message: string;
	Throw: (Exception) -> ();
}

local Exception
do
	Exception = (setmetatable({}, {
        __tostring = function(self: Exception): string
            return `{self.__className}: {self.Message}`
        end
    }) :: any) :: Exception
	Exception.__index = Exception
	Exception.__className = "Exception"

	function Exception.new(message: string?): Exception
		message = message or "An error occurred"

		return (setmetatable({ Message = message }, Exception) :: any) :: Exception
	end

	function Exception:Throw(): ()
		error(self)
	end
end
CS.defineGlobal("Exception", Exception)
CS.Exception = Exception

type TryBlock = () -> ...any
type CatchBlock = (ex: Exception) -> ...any
type FinallyBlock = () -> ()

CS.TRY_RETURN = 1
CS.TRY_BREAK = 2
CS.TRY_CONTINUE = 3

local function normalizeException(err: any): Exception
	if typeof(err) == "table" and typeof((err :: any).Throw) == "function" then
		return err :: Exception
	end

	local message = if err == nil then "An error occurred" else tostring(err)
	return Exception.new(message)
end

local function unpackPayload(payload: { any })
	return table.unpack(payload, 1, payload.n)
end

function CS.try(tryBlock: TryBlock, catchBlock: CatchBlock?, finallyBlock: FinallyBlock?): (number?, { any }?)
	assert(typeof(tryBlock) == "function", "CS.try requires a tryBlock function")

	local trySuccess, exitTypeOrError, returns = pcall(tryBlock)
	local exitType: number? = nil
	local tryError

	if trySuccess then
		exitType = exitTypeOrError
	else
		tryError = exitTypeOrError
	end

	local catchSuccess = true
	local catchError

	if not trySuccess and catchBlock ~= nil then
		catchSuccess, exitType, returns = pcall(catchBlock, normalizeException(tryError))
		if not catchSuccess then
			catchError = exitType
		end
	end

	if finallyBlock ~= nil then
		local finallyExitType, finallyReturns = finallyBlock()
		if finallyExitType ~= nil then
			exitType = finallyExitType
			returns = finallyReturns
		end
	end

	if exitType ~= CS.TRY_RETURN and exitType ~= CS.TRY_BREAK and exitType ~= CS.TRY_CONTINUE then
		if not catchSuccess and catchError ~= nil then
			error(catchError, 2)
		end

		if not trySuccess and catchBlock == nil and tryError ~= nil then
			error(tryError, 2)
		end
	end

	if returns == nil then
		returns = table.pack()
	end

	return exitType, returns
end

local Promise = require("../RobloxCS.Runtime/Promise")
CS.Promise = Promise

local Generator = require("../RobloxCS.Runtime/Generator")
CS.Generator = Generator

function CS.async(callback)
	return Promise.async(callback)
end

function CS.await(value)
	if not Promise.is(value) then
		return value
	end

	local results = { value:await() }
	local success = table.remove(results, 1)
	if success then
		return table.unpack(results, 1, #results)
	end

	error(results[1], 2)
end

CS.Async = CS.async
CS.Await = CS.await

local List = {}
List.__index = List 

function List.new()
	return setmetatable({}, List)
end

function List:Add(item)
	table.insert(self, item)
end

function List:Remove(item)
	local index = table.find(self, item)
	if index then
		table.remove(self, index)
		return true
	end
	return false
end

function List:Contains(item)
	return table.find(self, item) ~= nil
end

function List:Clear()
	table.clear(self)
end

function List.slice(source, startIndex, length)
	if typeof(source) ~= "table" then
		return {}
	end

	startIndex = math.max(startIndex, 1)
	length = math.max(length, 0)

	if length == 0 then
		return {}
	end

	local sourceLength = #source
	if sourceLength == 0 then
		return {}
	end

	local finish = math.min(startIndex + length - 1, sourceLength)
	local result = table.create(math.max(finish - startIndex + 1, 0))

	local writeIndex = 1
	for index = startIndex, finish do
		result[writeIndex] = source[index]
		writeIndex += 1
	end

	return result
end

CS.defineGlobal("List", List)
CS.List = List

local Dictionary = {}
Dictionary.__index = Dictionary

function Dictionary.new()
	return setmetatable({}, Dictionary)
end

function Dictionary:Add(key, value)
	self[key] = value
end

function Dictionary:Remove(key)
	local exists = self[key] ~= nil
	self[key] = nil
	return exists
end

function Dictionary:ContainsKey(key)
	return self[key] ~= nil
end

function Dictionary:TryGetValue(key, outCallback)
	local val = self[key]
	if val ~= nil then
		if outCallback then outCallback(val) end
		return true
	end
	if outCallback then outCallback(nil) end
	return false
end

function Dictionary:Clear()
	for k in pairs(self) do
		self[k] = nil
	end
end

CS.defineGlobal("Dictionary", Dictionary)
CS.Dictionary = Dictionary

local Struct = {}

local function deepCopy(value, visited)
	if typeof(value) ~= "table" then
		return value
	end

	if visited[value] ~= nil then
		return visited[value]
	end

	local result = {}
	visited[value] = result

	for key, inner in value do
		result[key] = deepCopy(inner, visited)
	end

	local mt = getmetatable(value)
	if mt ~= nil then
		setmetatable(result, mt)
	end

	return result
end

function Struct.copy(value)
	if typeof(value) ~= "table" then
		return value
	end

	return deepCopy(value, {})
end

function Struct.equals(left, right)
	if typeof(left) ~= "table" or typeof(right) ~= "table" then
		return left == right
	end

	for key, value in left do
		if right[key] ~= value then
			return false
		end
	end

	for key in right do
		if left[key] == nil then
			return false
		end
	end

	return true
end

CS.Struct = Struct

return CS
