function IsNull(obj)
	-- 纯 Lua 的 nil
	if obj == nil then
		return true
	end
	-- Unity 对象被销毁后引用仍非 nil，Equals(nil) 返回 true
	-- 用 pcall 保护非 Unity 对象（如 table、number、string），它们没有 Equals 方法
	local ok, result = pcall(function() return obj:Equals(nil) end)
	return ok and result
end