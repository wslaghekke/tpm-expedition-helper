// Debug InboxNotice.Send step by step, without sending.
var sb = new System.Text.StringBuilder();
var tmpl = TPS.Game.GameApp.Instance?.MessageQueue?.Config?.InboxMIA;
sb.AppendLine($"template InboxMIA: {(tmpl == null ? "null" : tmpl.GetIl2CppType().Name)}");
var type = UnityEngine.AddressableAssets.Addressables.LoadAssetAsync<TPS.Game.InboxMessageType>("4a33c6568d26e604585b403c47351f28").WaitForCompletion();
sb.AppendLine($"generic type: {(type == null ? "null" : type.name)}");
return sb.ToString();
