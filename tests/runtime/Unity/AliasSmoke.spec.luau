local UnityAliases = require("RobloxCS.Runtime.UnityAliases")

local function ensure(condition, message)
	if not condition then
		error(message, 2)
	end
end

CS.defineGlobal("RuntimeSpecs.UnityAliasSmokeSpec", {
	ShouldExposeCreateGameObject = function()
		ensure(type(UnityAliases.CreateGameObject) == "function", "UnityAliases.CreateGameObject should be a function")
	end,

	ShouldInstantiateModelWithNameAndParent = function()
		local container = Instance.new("Folder")
		local model = UnityAliases.CreateGameObject("Demo", container)

		ensure(model.ClassName == "Model", "CreateGameObject should instantiate a Model")
		ensure(model.Name == "Demo", "Model name should match argument")
		ensure(model.Parent == container, "Model parent should match the supplied container")
	end,

	ShouldExposeLoggingHelpers = function()
		ensure(type(UnityAliases.Log) == "function", "UnityAliases.Log should be a function")
		ensure(type(UnityAliases.LogWarning) == "function", "UnityAliases.LogWarning should be a function")
		ensure(type(UnityAliases.LogError) == "function", "UnityAliases.LogError should be a function")
	end,

	ShouldRespectNestedParenting = function()
		local root = Instance.new("Folder")
		local container = Instance.new("Folder")
		container.Name = "Container"
		container.Parent = root

		local model = UnityAliases.CreateGameObject("Nested", container)
		ensure(model.Parent == container, "Nested create should parent to provided container")
		ensure(container.Parent == root, "Container hierarchy should remain intact")
	end,
})
