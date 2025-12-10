local ReplicatedStorage = game:GetService("ReplicatedStorage")
local EntryPoint = require(ReplicatedStorage:WaitForChild("EntryPoint"))

print("Starting Roll-a-Ball...")
EntryPoint.Main()
