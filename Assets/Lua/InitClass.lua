package.path = package.path .. ";Assets/Lua/Libs/?.lua"
package.path = package.path .. ";Assets/Lua/UI/?.lua"

----- 常用别名
-- 面向对象父类
require("Object")
-- 字符串拆分
require("SplitTools")
-- Json读取
Json = require("JsonUtility")
-- 官方类名别名
require("Official_NickName")
-- 自定义别名
require("Custom_NickName")
-- 判空
require("IsNull")