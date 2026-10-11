// Read-only: the open generic inbox view(s) and their text fields.
var sb = new System.Text.StringBuilder();
foreach (var v in UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.UI.InboxMenu_Message_Generic>())
{
    sb.AppendLine($"view '{v.name}' active={v.gameObject.activeInHierarchy} isOpen={v.isOpen} desc='{v._textDescription?.text}' title='{v._textTitle?.text}' header='{v._textHeader?.text}' buttons={v._buttons?.Length}");
    foreach (var t in v.GetComponentsInChildren<TMPro.TMP_Text>(true))
        sb.AppendLine($"   TMP {t.name}: '{(t.text ?? "").Replace("\n", " | ").Substring(0, Math.Min(70, (t.text ?? "").Length))}'");
}
return sb.Length == 0 ? "no generic views" : sb.ToString();
