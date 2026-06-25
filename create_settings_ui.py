import json, subprocess, time

code = r'''
var canvas = UnityEngine.Object.FindObjectOfType<Canvas>();
if (canvas == null) {
    var cGo = new GameObject("Canvas");
    canvas = cGo.AddComponent<Canvas>();
    canvas.renderMode = RenderMode.ScreenSpaceOverlay;
    cGo.AddComponent<UnityEngine.UI.CanvasScaler>();
    cGo.AddComponent<UnityEngine.UI.GraphicRaycaster>();
}

// Clean up old test objects
var toRemove = new System.Collections.Generic.List<GameObject>();
foreach (Transform t in canvas.transform) {
    if (t.name.Contains("Test") || t.name.Contains("SettingsPanel")) toRemove.Add(t.gameObject);
}
foreach (var go in toRemove) UnityEngine.Object.Destroy(go);

// Helpers
GameObject CreateUI(string name, Transform parent) {
    var go = new GameObject(name);
    go.transform.SetParent(parent, false);
    go.AddComponent<RectTransform>();
    return go;
}

TMPro.TextMeshProUGUI AddText(GameObject go, string text, float size, Color color) {
    var tmp = go.AddComponent<TMPro.TextMeshProUGUI>();
    tmp.text = text;
    tmp.fontSize = size;
    tmp.color = color;
    return tmp;
}

// --- Main Panel ---
var panel = CreateUI("SettingsPanel", canvas.transform);
var panelRT = panel.GetComponent<RectTransform>();
panelRT.anchorMin = new Vector2(0.5f, 0.5f);
panelRT.anchorMax = new Vector2(0.5f, 0.5f);
panelRT.sizeDelta = new Vector2(520, 560);
panelRT.anchoredPosition = Vector2.zero;

var panelBg = panel.AddComponent<UnityEngine.UI.Image>();
panelBg.color = new Color(0.08f, 0.08f, 0.1f, 0.97f);

var vlg = panel.AddComponent<UnityEngine.UI.VerticalLayoutGroup>();
vlg.padding = new RectOffset(24, 24, 20, 20);
vlg.spacing = 8;
vlg.childAlignment = TextAnchor.UpperCenter;
vlg.childControlWidth = true;
vlg.childControlHeight = false;
vlg.childForceExpandWidth = true;
vlg.childForceExpandHeight = false;

// --- Title ---
var titleGo = CreateUI("Title", panel.transform);
titleGo.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 40);
var titleTmp = AddText(titleGo, "SETTINGS", 26, Color.white);
titleTmp.alignment = TMPro.TextAlignmentOptions.Center;
titleTmp.fontStyle = TMPro.FontStyles.Bold;

// --- Separator ---
var sep = CreateUI("Separator", panel.transform);
sep.GetComponent<RectTransform>().sizeDelta = new Vector2(0, 2);
sep.AddComponent<UnityEngine.UI.Image>().color = new Color(0.3f, 0.3f, 0.35f, 1);

// --- Row helper ---
GameObject CreateRow(string name, string labelText, Transform parent, float labelW, float height) {
    var row = CreateUI(name, parent);
    row.GetComponent<RectTransform>().sizeDelta = new Vector2(0, height);
    var hlg = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
    hlg.spacing = 8;
    hlg.childAlignment = TextAnchor.MiddleLeft;
    hlg.childControlWidth = false;
    hlg.childControlHeight = false;
    hlg.childForceExpandWidth = false;
    hlg.childForceExpandHeight = false;

    var lbl = CreateUI("Label", row.transform);
    lbl.GetComponent<RectTransform>().sizeDelta = new Vector2(labelW, height);
    var tmp = AddText(lbl, labelText, 14, new Color(0.82f, 0.82f, 0.82f));
    tmp.alignment = TMPro.TextAlignmentOptions.MidlineLeft;
    return row;
}

// --- Game Speed (cycle button) ---
var speedRow = CreateRow("GameSpeed", "Game Speed", panel.transform, 150, 32);
var speedDD = CreateUI("CycleBtn", speedRow.transform);
speedDD.GetComponent<RectTransform>().sizeDelta = new Vector2(130, 28);
var speedTmp = AddText(speedDD, "1", 14, Color.white);
speedTmp.alignment = TMPro.TextAlignmentOptions.Center;
speedDD.AddComponent<UnityEngine.UI.Image>().color = new Color(0.22f, 0.22f, 0.28f, 1);
var speedBtn = speedDD.AddComponent<UnityEngine.UI.Button>();
float[] speeds = {0.5f, 1f, 2f, 4f};
int speedIdx = 1;
speedBtn.onClick.AddListener(() => {
    speedIdx = (speedIdx + 1) % speeds.Length;
    speedTmp.text = speeds[speedIdx].ToString();
});

// --- Play Discard Position (cycle button) ---
var posRow = CreateRow("PlayDiscardPos", "Discard Position", panel.transform, 150, 32);
var posDD = CreateUI("CycleBtn", posRow.transform);
posDD.GetComponent<RectTransform>().sizeDelta = new Vector2(130, 28);
var posTmp = AddText(posDD, "Default", 14, Color.white);
posTmp.alignment = TMPro.TextAlignmentOptions.Center;
posDD.AddComponent<UnityEngine.UI.Image>().color = new Color(0.22f, 0.22f, 0.28f, 1);
var posBtn = posDD.AddComponent<UnityEngine.UI.Button>();
string[] positions = {"Default", "Left", "Right"};
int posIdx = 0;
posBtn.onClick.AddListener(() => {
    posIdx = (posIdx + 1) % positions.Length;
    posTmp.text = positions[posIdx];
});

// --- Toggle helper ---
GameObject CreateToggleRow(string name, string labelText, Transform parent, bool initial, float labelW) {
    var row = CreateRow(name, labelText, parent, labelW, 28);
    var tgGo = CreateUI("Toggle", row.transform);
    tgGo.GetComponent<RectTransform>().sizeDelta = new Vector2(44, 22);
    var tgBg = tgGo.AddComponent<UnityEngine.UI.Image>();
    tgBg.color = initial ? new Color(0.5f, 0.42f, 0.15f, 1) : new Color(0.22f, 0.22f, 0.28f, 1);
    var tg = tgGo.AddComponent<UnityEngine.UI.Toggle>();
    tg.targetGraphic = tgBg;
    tg.isOn = initial;

    var check = CreateUI("Checkmark", tgGo.transform);
    var checkRT = check.GetComponent<RectTransform>();
    checkRT.anchorMin = new Vector2(0.15f, 0.15f);
    checkRT.anchorMax = new Vector2(0.85f, 0.85f);
    checkRT.sizeDelta = Vector2.zero;
    checkRT.anchoredPosition = Vector2.zero;
    var checkImg = check.AddComponent<UnityEngine.UI.Image>();
    checkImg.color = initial ? new Color(1, 0.85f, 0.3f, 1) : new Color(0, 0, 0, 0);
    tg.graphic = checkImg;

    tg.onValueChanged.AddListener((v) => {
        tgBg.color = v ? new Color(0.5f, 0.42f, 0.15f, 1) : new Color(0.22f, 0.22f, 0.28f, 1);
        checkImg.color = v ? new Color(1, 0.85f, 0.3f, 1) : new Color(0, 0, 0, 0);
    });
    return row;
}

// --- All toggles ---
CreateToggleRow("Rumble", "Rumble", panel.transform, true, 150);
CreateToggleRow("Stickers", "Display Stickers", panel.transform, true, 150);
CreateToggleRow("Contrast", "High Contrast Cards", panel.transform, false, 150);
CreateToggleRow("Motion", "Reduced Motion", panel.transform, false, 150);
CreateToggleRow("Crash", "Crash Reports", panel.transform, true, 150);

// --- Screenshake Slider ---
var shakeRow = CreateRow("Screenshake", "Screenshake", panel.transform, 150, 32);

var sliderGo = CreateUI("Slider", shakeRow.transform);
sliderGo.GetComponent<RectTransform>().sizeDelta = new Vector2(160, 24);
var sliderBg = sliderGo.AddComponent<UnityEngine.UI.Image>();
sliderBg.color = new Color(0.22f, 0.22f, 0.28f, 1);

var slider = sliderGo.AddComponent<UnityEngine.UI.Slider>();
slider.minValue = 0; slider.maxValue = 100; slider.value = 50;

// Fill area
var fillArea = CreateUI("FillArea", sliderGo.transform);
var faRT = fillArea.GetComponent<RectTransform>();
faRT.anchorMin = new Vector2(0.05f, 0.2f);
faRT.anchorMax = new Vector2(0.95f, 0.8f);
faRT.sizeDelta = Vector2.zero;
faRT.anchoredPosition = Vector2.zero;

var fill = CreateUI("Fill", fillArea.transform);
var fillRT = fill.GetComponent<RectTransform>();
fillRT.anchorMin = Vector2.zero;
fillRT.anchorMax = new Vector2(0.5f, 1);
fillRT.sizeDelta = Vector2.zero;
fill.AddComponent<UnityEngine.UI.Image>().color = new Color(1, 0.85f, 0.3f, 1);
slider.fillRect = fillRT;

// Value label
var valGo = CreateUI("Value", sliderGo.transform);
var valRT = valGo.GetComponent<RectTransform>();
valRT.anchorMin = Vector2.zero;
valRT.anchorMax = Vector2.one;
valRT.sizeDelta = Vector2.zero;
valRT.anchoredPosition = Vector2.zero;
var valTmp = AddText(valGo, "50", 11, new Color(0.7f, 0.7f, 0.7f));
valTmp.alignment = TMPro.TextAlignmentOptions.Center;
valTmp.raycastTarget = false;

slider.onValueChanged.AddListener((v) => {
    int pct = (int)v;
    valTmp.text = pct.ToString();
});

Debug.Log("[UI] Settings panel created with 8 settings!");
return "ok";
'''

payload = {
    "jsonrpc": "2.0",
    "id": 11,
    "method": "tools/call",
    "params": {
        "name": "execute_code",
        "arguments": {
            "action": "execute",
            "code": code
        }
    }
}

result = subprocess.run([
    "curl", "-s", "http://127.0.0.1:8080/mcp",
    "-X", "POST",
    "-H", "Content-Type: application/json",
    "-H", "Accept: application/json, text/event-stream",
    "-H", "Mcp-Session-Id: 7f28d7ca46ee4c62b7e9e9449140f612",
    "-d", json.dumps(payload)
], capture_output=True, text=True)

print("STDOUT:", result.stdout[:500])
print("STDERR:", result.stderr[:500])
