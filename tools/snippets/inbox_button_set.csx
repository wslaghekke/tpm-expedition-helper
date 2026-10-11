// Set every non-empty text inside the open generic inbox view's button.
var v = UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.UI.InboxMenu_Message_Generic>().First(x => x.isOpen);
var sb = new System.Text.StringBuilder();
foreach (var t in v._buttons[0].GetComponentsInChildren<TMPro.TMP_Text>(true))
{
    sb.Append($"[{t.name} '{t.text}' active={t.gameObject.activeInHierarchy}] ");
    if (string.IsNullOrEmpty(t.text)) continue;
    var loc = t.GetComponent<I2.Loc.Localize>();
    if (loc != null) loc.enabled = false;
    t.text = "Cancel repeat";
}
return sb.ToString();
