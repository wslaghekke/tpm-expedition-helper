// Manually set description + button label on the open generic inbox view (to test what sticks).
var v = UnityEngine.Resources.FindObjectsOfTypeAll<TPS.Game.UI.InboxMenu_Message_Generic>().First(x => x.isOpen);
var desc = v._textDescription;
var loc = desc.GetComponent<I2.Loc.Localize>();
var sb = new System.Text.StringBuilder($"desc Localize={(loc != null)} ");
if (loc != null) loc.enabled = false;
desc.text = "Waiting for: Kate Chiptune to recover (Banged Up). [set by hand]";
var btn = v._buttons[0];
var label = btn.GetComponentInChildren<TMPro.TMP_Text>(true);
var bloc = label?.GetComponent<I2.Loc.Localize>();
sb.Append($"button label={(label != null)} Localize={(bloc != null)} ");
if (bloc != null) bloc.enabled = false;
if (label != null) label.text = "Cancel repeat";
return sb.ToString();
