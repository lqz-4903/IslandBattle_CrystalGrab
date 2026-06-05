function IsNull( obj )
	if obj == nil or obj:Equals(nil) then
		return true
	end
	return false
end