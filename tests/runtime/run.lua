local fs = require("@lune/fs")
local luau = require("@lune/luau")
local process = require("@lune/process")
local stdio = require("@lune/stdio")

local cliArgs = { ... }
local getEnv = type(os) == "table" and type(os.getenv) == "function" and os.getenv or function()
    return nil
end

local function realpath(path)
    if type(fs.realpath) == "function" then
        return fs.realpath(path)
    elseif type(fs.realPath) == "function" then
        return fs.realPath(path)
    elseif type(fs.canonicalize) == "function" then
        return fs.canonicalize(path)
    elseif type(fs.normalize) == "function" then
        return fs.normalize(path)
    end

    return path
end

local function pathExists(path)
    if type(fs.exists) == "function" then
        return fs.exists(path)
    elseif type(fs.isFile) == "function" or type(fs.isDir) == "function" or type(fs.isDirectory) == "function" then
        local isFile = type(fs.isFile) == "function" and fs.isFile(path)
        local isDir = type(fs.isDir) == "function" and fs.isDir(path)
        local isDirectory = type(fs.isDirectory) == "function" and fs.isDirectory(path)
        return isFile or isDir or isDirectory
    elseif type(fs.access) == "function" then
        local ok = pcall(fs.access, path)
        return ok
    end

    local f = io.open(path, "r")
    if f then
        f:close()
        return true
    end

    return false
end

local function isDirectory(path)
    if type(fs.isDir) == "function" then
        return fs.isDir(path)
    elseif type(fs.isDirectory) == "function" then
        return fs.isDirectory(path)
    elseif type(fs.dir) == "function" then
        local ok = pcall(function()
            fs.dir(path)
        end)
        return ok
    end

    return false
end

local function listDir(path)
    if type(fs.listDir) == "function" then
        return fs.listDir(path)
    elseif type(fs.iterDir) == "function" then
        local entries = {}
        for entry in fs.iterDir(path) do
            entries[#entries + 1] = entry
        end
        return entries
    elseif type(fs.dir) == "function" then
        local entries = {}
        for entry in fs.dir(path) do
            entries[#entries + 1] = entry
        end
        return entries
    elseif type(fs.readDir) == "function" then
        local ok, entries = pcall(fs.readDir, path)
        if ok and type(entries) == "table" then
            local result = {}
            for _, entry in ipairs(entries) do
                result[#result + 1] = entry
            end
            return result
        end
    end

    local entries = {}
    if type(io) == "table" and type(io.popen) == "function" then
        local command = string.format('ls -1 "%s"', path:gsub('"', '\\"'))
        local proc = io.popen(command)
        if proc then
            for line in proc:lines() do
                if line ~= '.' and line ~= '..' then
                    entries[#entries + 1] = line
                end
            end
            proc:close()
        end
    end

    return entries
end

local function getScriptPath()
    local candidate = process and process.scriptPath or nil

    if type(candidate) == "function" then
        local ok, value = pcall(candidate)
        if ok and type(value) == "string" and value ~= "" then
            return value
        end
    elseif type(candidate) == "string" and candidate ~= "" then
        return candidate
    end

    return nil
end

local function dirname(path)
    if type(fs.dirname) == "function" then
        return fs.dirname(path)
    end

    local normalized = path:gsub("\\", "/")
    local parent = normalized:match("^(.*)/[^/]+$")
    return parent or "."
end

local function join(...)
    if type(fs.join) == "function" then
        return fs.join(...)
    end

    local parts = { ... }
    local buffer = {}

    for i = 1, #parts do
        local part = tostring(parts[i])
        if part ~= "" then
            buffer[#buffer + 1] = part
        end
    end

    local result = table.concat(buffer, "/")
    result = result:gsub("\\", "/")
    result = result:gsub("//+", "/")

    return result
end

local function normalizePath(path)
    local isAbsolute = path:sub(1, 1) == "/"
    local segments = {}

    for segment in path:gmatch("[^/]+") do
        if segment == ".." then
            if #segments > 0 then
                table.remove(segments)
            end
        elseif segment ~= "." and segment ~= "" then
            table.insert(segments, segment)
        end
    end

    local normalized = table.concat(segments, "/")
    if isAbsolute then
        return "/" .. normalized
    end

    return normalized
end

local function ensureAbsolute(path)
    if path == nil then
        return nil
    end

    if path:sub(1, 1) == "/" then
        return normalizePath(path)
    end

    local base
    local envGet = type(os) == "table" and type(os.getenv) == "function" and os.getenv or function()
        return nil
    end

    if fs and type(fs.realpath) == "function" then
        base = fs.realpath(".")
    end

    if not base and type(process) == "table" then
        if type(process.cwd) == "function" then
            local ok, value = pcall(process.cwd)
            if ok and type(value) == "string" and value ~= "" then
                base = value
            end
        elseif type(process.currentDir) == "function" then
            local ok, value = pcall(process.currentDir)
            if ok and type(value) == "string" and value ~= "" then
                base = value
            end
        end
    end

    if not base then
        base = envGet("PWD")
    end

    if not base and type(io) == "table" and type(io.popen) == "function" then
        local proc = io.popen("pwd")
        if proc then
            base = proc:read("*l")
            proc:close()
        end
    end

    base = base or "."
    local combined = normalizePath(base .. "/" .. path)
    local resolved = realpath(combined)
    if resolved ~= nil then
        return resolved
    end
    return combined
end

local function resolvePaths()
    local debugInfo
    if type(debug) == "table" and type(debug.getinfo) == "function" then
        debugInfo = debug.getinfo(1, "S")
    end

    local scriptSource = debugInfo and type(debugInfo.source) == "string" and debugInfo.source or nil
    local getEnv = type(os) == "table" and type(os.getenv) == "function" and os.getenv or function()
        return nil
    end

    local scriptPath = cliArgs[1]
        or getEnv("ROBLOX_CS_RUNTIME_HARNESS")
        or getScriptPath()
        or (scriptSource and scriptSource:match("^@(.+)$"))
        or (process and process.argv and process.argv[0])

    local cwd
    if scriptPath == nil then
        cwd = nil
        if type(process) == "table" then
            if type(process.cwd) == "function" then
                cwd = process.cwd()
            elseif type(process.currentDir) == "function" then
                cwd = process.currentDir()
            end
        end

        if cwd == nil then
            cwd = fs and type(fs.realpath) == "function" and fs.realpath(".") or nil
        end

        if cwd ~= nil then
            scriptPath = join(cwd, "roblox-cs", "tests", "runtime", "run.lua")
        else
            if type(io) == "table" and type(io.popen) == "function" then
                local proc = io.popen("pwd")
                if proc then
                    local path = proc:read("*l")
                    proc:close()
                    if path and path ~= "" then
                        scriptPath = join(path, "roblox-cs", "tests", "runtime", "run.lua")
                    end
                end
            end
        end
    end

    if scriptPath == nil then
        scriptPath = "roblox-cs/tests/runtime/run.lua"
    end

    scriptPath = ensureAbsolute(scriptPath)
    scriptPath = realpath(scriptPath)
    if scriptPath == nil then
        error("Unable to resolve runtime spec harness path.")
    end
    local scriptDir = dirname(scriptPath)
    local explicitRootArgument = getEnv("ROBLOX_CS_RUNTIME_PROJECT_ROOT")
    local explicitRoot = explicitRootArgument and ensureAbsolute(explicitRootArgument) or nil
    local projectRoot
    if explicitRoot ~= nil then
        projectRoot = explicitRoot
    else
        projectRoot = ensureAbsolute(normalizePath(scriptDir .. "/../.."))
    end

    if projectRoot == nil then
        error("Unable to resolve project root from runtime harness.")
    end

    local runtimePathCandidates = {
        join(projectRoot, "RobloxCS", "Include", "RuntimeLib.luau"),
        join(projectRoot, "..", "RobloxCS", "Include", "RuntimeLib.luau"),
        join(projectRoot, "Include", "RuntimeLib.luau"),
    }

    local runtimePath
    for _, candidate in ipairs(runtimePathCandidates) do
        if pathExists(candidate) then
            runtimePath = realpath(candidate)
            break
        end
    end

    if runtimePath == nil then
        error("Unable to locate RuntimeLib.luau when running specs.")
    end

    local outputCandidates = {
        join(projectRoot, "tests", "runtime", "out"),
        join(projectRoot, "runtime", "out"),
        join(projectRoot, "..", "roblox-cs", "tests", "runtime", "out"),
    }

    local outputRoot
    for _, candidate in ipairs(outputCandidates) do
        if pathExists(candidate) then
            outputRoot = realpath(candidate)
            break
        end
    end

    if outputRoot == nil then
        error("Transpiled runtime specs not found. Expected directory `tests/runtime/out` relative to the project root.")
    end

    return {
        projectRoot = ensureAbsolute(projectRoot),
        outputRoot = ensureAbsolute(outputRoot),
        runtimePath = ensureAbsolute(runtimePath),
    }
end

local paths = resolvePaths()

local OUTPUT_ROOT = ensureAbsolute(paths.outputRoot)
local RUNTIME_PATH = ensureAbsolute(paths.runtimePath)
local function readFile(path)
    local reader = fs.readFile or fs.readFileSync
    if type(reader) ~= "function" then
        local file = io.open(path, "r")
        if not file then
            error("Failed to open " .. path)
        end

        local contents = file:read("*a")
        file:close()
        return contents
    end

    local ok, contents = pcall(reader, path)
    if not ok then
        error("Failed to read " .. path .. ": " .. tostring(contents))
    end
    return contents
end

local function loadRuntime()
    local env = setmetatable({}, { __index = _G })
    env._G = env
    local luneTask = require("@lune/task")
    env.task = luneTask

    local moduleCache = {}

    local function resolveModule(modulePath)
        if modulePath:match("^%.%./RobloxCS%.Runtime/") then
            local remainder = modulePath:gsub("^%.%./", "")
            local directBase = normalizePath(join(paths.projectRoot, remainder))
            local directLua = directBase .. ".lua"
            local okLua, contentsLua = pcall(readFile, directLua)
            if okLua then
                return directLua, contentsLua
            end

            local directLuau = directBase .. ".luau"
            local okLuau, contentsLuau = pcall(readFile, directLuau)
            if okLuau then
                return directLuau, contentsLuau
            end
        end

        local searchBases = {}

        if modulePath:match("^%.") then
            table.insert(searchBases, join(dirname(RUNTIME_PATH), modulePath))
        else
            table.insert(searchBases, join(paths.projectRoot, modulePath))
            table.insert(searchBases, join(dirname(RUNTIME_PATH), modulePath))
        end

        for _, base in ipairs(searchBases) do
            local combinedBase = normalizePath(base)
            local candidates = {
                combinedBase,
                combinedBase .. ".luau",
                combinedBase .. ".lua",
            }

            for _, candidate in ipairs(candidates) do
                local ok, contents = pcall(readFile, candidate)
                if ok then
                    return candidate, contents
                end
            end
        end

        stdio.ewrite(string.format("[runtime] failed to resolve module '%s'\n", modulePath))
        stdio.ewrite(string.format("[runtime] search bases: %s\n", table.concat(searchBases, ", ")))
        return nil
    end

    env.require = function(modulePath)
        if moduleCache[modulePath] ~= nil then
            local cached = moduleCache[modulePath]
            return table.unpack(cached, 1, cached.n)
        end

        local resolvedPath, contents = resolveModule(modulePath)
        if not resolvedPath or not contents then
            error("Unable to resolve module '" .. modulePath .. "'")
        end

        local moduleEnv = setmetatable({
            require = env.require,
        }, { __index = env })
        if env.task ~= nil then
            moduleEnv.task = env.task
        end

        local chunk = luau.load(contents, {
            debugName = modulePath,
            environment = moduleEnv,
        })

        local results = table.pack(chunk())
        moduleCache[modulePath] = results
        return table.unpack(results, 1, results.n)
    end

    local source = readFile(RUNTIME_PATH)
    local chunk = luau.load(source, {
        debugName = "RuntimeLib",
        environment = env,
    })
    local results = table.pack(chunk())

    if type(env.CS) ~= "table" and results[1] ~= nil then
        env.CS = results[1]
    end

    if type(env.CS) ~= "table" then
        error("RuntimeLib did not define global CS table")
    end

    env.Roblox = env.Roblox or {}
    env.Roblox.Promise = env.CS.Promise
    env.Promise = env.CS.Promise

    env.Task = env.Task or {}
    env.Task.FromResult = function(value)
        local promise = env.CS.Promise.Resolve(value)
        return promise
    end

    return env
end

local function collectModuleFiles()
    if not isDirectory(OUTPUT_ROOT) then
        error("Transpiled runtime specs not found. Expected directory: " .. OUTPUT_ROOT)
    end

    local files = {}

    local function allowSpec(path)
        return path:match("/Promise/") or path:match("/Unity/")
    end

    local function scan(dir)
        for _, entry in ipairs(listDir(dir)) do
            local entryPath = string.format("%s/%s", dir, entry)
            if isDirectory(entryPath) then
                scan(entryPath)
            elseif entryPath:match("%.spec%.luau$") and allowSpec(entryPath) then
                table.insert(files, entryPath)
            end
        end
    end

    scan(OUTPUT_ROOT)
    table.sort(files)
    return files
end

local runtimeEnv = loadRuntime()
local CS = runtimeEnv.CS

local function gatherSpecFunctions(node, prefix, accumulator, visited)
    if type(node) ~= "table" then
        return
    end

    visited = visited or {}
    if visited[node] then
        return
    end
    visited[node] = true

    for name, member in pairs(node) do
        if type(name) == "string" then
            local path = prefix and (prefix .. "." .. name) or name
            if type(member) == "function" and name:match("^Should") then
                table.insert(accumulator, {
                    path = path,
                    callback = member,
                })
            elseif type(member) == "table" then
                gatherSpecFunctions(member, path, accumulator, visited)
            end
        end
    end
end

local function runSpec(className)
    local root = CS.getGlobal(className)
    if type(root) ~= "table" then
        error("Spec class " .. className .. " was not defined by the transpiled module")
    end

    local specs = {}
    gatherSpecFunctions(root, className, specs, {})

    local failures = {}
    local passed = 0

    if #specs == 0 then
        stdio.write(string.format("[runtime] no specs discovered under %s\n", className))
    end

    for _, spec in ipairs(specs) do
        local ok, err = pcall(spec.callback)
        if ok then
            passed += 1
        else
            table.insert(failures, {
                method = spec.path,
                message = tostring(err),
            })
        end
    end

    return passed, failures
end

local moduleFiles = collectModuleFiles()

local totalPassed = 0
local totalFailed = 0
local failureMessages = {}

for _, modulePath in ipairs(moduleFiles) do
    local source = readFile(modulePath)
    local definedGlobals = {}
    for name in source:gmatch('CS%.defineGlobal%("([%w_]+)"') do
        table.insert(definedGlobals, name)
    end

    local chunk = luau.load(source, {
        debugName = modulePath,
        environment = runtimeEnv,
    })
    chunk()

    if #definedGlobals == 0 then
        stdio.write(string.format("[warn] Module %s did not define any global spec classes\n", modulePath))
    end

    for _, className in ipairs(definedGlobals) do
        local passed, failures = runSpec(className)
        totalPassed += passed
        totalFailed += #failures

        for _, failure in ipairs(failures) do
            table.insert(failureMessages, string.format("%s.%s failed: %s", className, failure.method, failure.message))
        end
    end
end

if totalFailed > 0 then
    for _, message in ipairs(failureMessages) do
        stdio.ewrite(message .. "\n")
    end
    stdio.ewrite(string.format("Runtime specs failed: %d passing, %d failing\n", totalPassed, totalFailed))
    if process and type(process.exit) == "function" then
        process.exit(1)
    else
        os.exit(1)
    end
end

stdio.write(string.format("Runtime specs passed: %d\n", totalPassed))
if process and type(process.exit) == "function" then
    process.exit(0)
else
    os.exit(0)
end
