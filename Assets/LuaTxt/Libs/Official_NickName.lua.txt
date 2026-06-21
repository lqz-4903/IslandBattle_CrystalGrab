-- GameObject
GameObject = CS.UnityEngine.GameObject
PrimitiveType = CS.UnityEngine.PrimitiveType
Rigidbody = CS.UnityEngine.Rigidbody
Vector2 = CS.UnityEngine.Vector2
Vector3 = CS.UnityEngine.Vector3
Transform = CS.UnityEngine.Transform
Resources = CS.UnityEngine.Resources
TextAsset = CS.UnityEngine.TextAsset

-- Debug
Debug = CS.UnityEngine.Debug

-- DataType and DataContainer
Int = CS.System.Int32
Float = CS.System.Single
String = CS.System.String
Array = CS.System.Array
List = CS.System.Collections.Generic.List
Dictionary = CS.System.Collections.Generic.Dictionary

-- UI
UI = CS.UnityEngine.UI
Canvas = CS.UnityEngine.Canvas
CanvasGroup = CS.UnityEngine.CanvasGroup
UIBehaviour = CS.UnityEngine.EventSystems.UIBehaviour
RectTransform = CS.UnityEngine.RectTransform
Image = CS.UnityEngine.UI.Image
Text = CS.UnityEngine.UI.Text 
Button = CS.UnityEngine.UI.Button
Toggle = CS.UnityEngine.UI.Toggle
Slider = CS.UnityEngine.UI.Slider
ScrollRect = CS.UnityEngine.UI.ScrollRect
TmpText = CS.TMPro.TextMeshProUGUI
--- 图集对象类
SpriteAtlas = CS.UnityEngine.U2D.SpriteAtlas

-- Coroutine
WaitForSeconds = CS.UnityEngine.WaitForSeconds

-- Math
Mathf = CS.UnityEngine.Mathf

-- XLua
-- xlua提供的一个工具表 一定要通过require引用
util = require("xlua.util")

-- Canvas 对于我们这个项目来说 是找一次就可以了
-- Canvas = GameObject.Find("Canvas").transform






