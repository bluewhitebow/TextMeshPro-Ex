# TextMeshPro（多 SubMesh 描边）

基于 Unity 官方 **TextMeshPro 3.0.7**（`com.unity.textmeshpro`，对应 Unity 2020.1+）的本地包。在官方实现之上，加了一套 **UGUI 多图集 / 多 SubMesh 文字描边** 方案。

官方 TMP 本身没有改渲染管线；自定义部分集中在一次提交「多submesh描边方案」。对应 git tag：**`v3.0.7-outline`**。

## 要解决什么问题

UGUI 下 `TextMeshProUGUI` 每个材质 / 图集会拆成独立 Graphic：

- 主对象画 atlas 0
- `TMP_SubMeshUI` 画 atlas 1、2…

如果用「外描边再填色」这种两 pass 画法，按官方顺序会变成：

```
atlas0 描边 → atlas0 填充 → atlas1 描边 → atlas1 填充 → …
```

混用字体、Fallback、Sprite 时，后面图集的描边会盖住前面图集的字，描边叠层错乱。

期望顺序是：

```
全部 atlas 描边 → 全部 atlas 填充
```

这样所有描边都在字下面，不会互相压字。

## 怎么用

1. 用 git tag 把本仓库当 UPM 包引入（覆盖或替换项目里的 `com.unity.textmeshpro`）：

```json
"com.unity.textmeshpro": "https://github.com/bluewhitebow/TMP.git#v3.0.7-outline"
```

也可以 clone / 本地 `file:` 引用，检出版本同样用 tag `v3.0.7-outline`。
2. 在 `TextMeshProUGUI` 上添加 **UI → TextMeshPro - Grouped Outline (UI)**（组件 `TMP_GroupedOutlineRenderer`）。
3. 指定 **Outline Material**：外圈描边用的材质模板（SDF 描边 Shader）。
4. 勾选 **Enable Outline Pass**（默认开）。关掉后恢复官方原生 Canvas 绘制。

组件会按当前 `materialCount` 自动生成子节点：

- `TMP Pass Outline [n]`：第 n 个图集的描边
- `TMP Pass Fill [n]`：第 n 个图集的填充

子节点带 `DontSave`，不要当业务对象去改。关闭或删掉组件后，原生 `CanvasRenderer` / `TMP_SubMeshUI` 绘制会回来。

描边材质会从对应填充材质拷贝图集贴图和 SDF 参数（`_MainTex`、`_GradientScale`、宽高、Weight、Scale 等），因此同一套描边模板可以套到多个 Font Atlas 上。

## 实现要点

`TMP_GroupedOutlineRenderer` 打开后会：

1. 把 `TextMeshProUGUI.suppressNativeCanvasMesh` 设为 `true`，CPU 网格照常生成，但 **不再** 把网格上传到自身和官方 SubMesh 的 `CanvasRenderer`。
2. 监听 `OnPreRenderText` / `OnMeshUploaded`，为每个材质建一对 `TMP_TextPassUI`（轻量 `MaskableGraphic`）。
3. 把同一份网格先交给描边材质、再交给填充材质。
4. 按兄弟节点顺序排成：**先所有 Outline，再所有 Fill**。
5. 同步父文字的颜色、Mask、Cull、Layer、Pivot。

`TMP_TextPassUI` 自己不 Dirty 父文字、不跑 `OnPopulateMesh`，只接收父 TMP 推过来的 Mesh，避免 Canvas 重建死循环。

### 相对官方改动的文件

| 文件 | 作用 |
| --- | --- |
| `Scripts/Runtime/TMP_GroupedOutlineRenderer.cs` | 多 pass 调度、材质实例、子节点排序 |
| `Scripts/Runtime/TMP_TextPassUI.cs` | 单图集单 pass 的 Canvas Graphic |
| `Scripts/Runtime/TMP_Text.cs` | `suppressNativeCanvasMesh`、`OnMeshUploaded` |
| `Scripts/Runtime/TextMeshProUGUI.cs` | `ApplyNativeCanvasMesh`，上传网格时走开关 |
| `Scripts/Runtime/TMPro_UGUI_Private.cs` | 生成网格后走同一套上传 / 事件 |

其余脚本、Editor、文档仍是官方 3.0.7。

## 注意

- 只覆盖 **UGUI**（`TextMeshProUGUI`），世界空间 `TextMeshPro` 没有这套分组绘制。
- 描边模板需要带 `_MainTex`、`_GradientScale` 等 SDF 属性；Bitmap / 没有这些属性的材质会只画填充、不画描边。
- 原生 SubMesh 仍会存在，只是网格被清空；真正出画的是 Pass 子节点。
- 这是对官方包的侵入式改动。升级 TMP 版本时，上述 5 个文件需要重新合入。

## 官方能力（未改）

TMP 仍是 Unity UI Text / 旧 TextMesh 的替换方案：SDF 渲染、富文本、多字体与 Sprite、样式表、InputField / Dropdown 等。首次使用需通过 **Window → TextMeshPro → Import TMP Essential Resources** 导入资源。

更完整的官方说明见 `Documentation~/TextMeshPro.md`。
