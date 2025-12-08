local Struct = CS.Struct

CS.defineGlobal("RuntimeSpecs.StructHelpersSpec", {
	ShouldCopyStructTable = function()
		local original = { X = 1, Y = 2 }
		local copy = Struct.copy(original)

		if copy == original then
			error("Struct.copy should return a new table")
		end

		if copy.X ~= 1 or copy.Y ~= 2 then
			error(string.format("Struct.copy produced unexpected values: X=%s Y=%s", tostring(copy.X), tostring(copy.Y)))
		end

		copy.X = 10
		if original.X ~= 1 then
			error("Struct.copy should not mutate the original table")
		end
	end,

	ShouldDeepCopyNestedTables = function()
		local original = {
			Position = { X = 3, Y = 4 },
			Values = { 1, 2, 3 },
		}

		local copy = Struct.copy(original)

		if copy.Position == original.Position then
			error("Struct.copy should deep copy nested tables")
		end

		copy.Position.X = 10
		if original.Position.X ~= 3 then
			error("Struct.copy should not mutate nested tables on the original value")
		end
	end,

	ShouldCompareStructsByValue = function()
		local a = { X = 1, Y = 2 }
		local b = Struct.copy(a)

		if not Struct.equals(a, b) then
			error("Struct.equals should return true for identical fields")
		end

		b.X = 5
		if Struct.equals(a, b) then
			error("Struct.equals should return false after mutation")
		end
	end
})
