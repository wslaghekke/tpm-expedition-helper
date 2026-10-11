// What a POI's drop table really contains (per reward entry), to explain rewards missing from GetExhibitRewards.
IEnumerable<T> Iter<T>(Il2CppSystem.Collections.Generic.IEnumerable<T> src)
{
    var en = src.GetEnumerator();
    var ne = en.Cast<Il2CppSystem.Collections.IEnumerator>();
    while (ne.MoveNext()) yield return en.Current;
}
var sb = new System.Text.StringBuilder();
foreach (var poi in Iter(TPS.Game.WorldMap.Instance.AllPOIs))
{
    var d = poi.Definition;
    if (!d.name.Contains("SmallPrehistoryInventions")) continue;
    var table = d.DropTable;
    sb.AppendLine($"== {d.name} table={table?.name} surveyLevel={poi.SurveyXP.Level}");
    void Dump(string label, Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppReferenceArray<TPS.Game.ExpeditionReward> rewards)
    {
        if (rewards == null) { sb.AppendLine($" {label}: null"); return; }
        foreach (var r in rewards)
        {
            if (r == null) continue;
            var ex = r.TryCast<TPS.Game.ExpeditionReward_Exhibit>();
            string what = r.GetIl2CppType().Name;
            if (ex != null)
            {
                var def = ex.ExhibitDefinition == TPS.Game.ExhibitDefinitionID.Null ? null : TPS.Game.LevelDatabaseUtils.Get(ex.ExhibitDefinition);
                var sub = TPS.Game.LevelDatabaseUtils.Get(ex.ExhibitSubcategory);
                what += $" def={def?.name ?? "-"} subcategory={sub?.name ?? "-"}";
            }
            sb.AppendLine($" {label}: {what} weight={r.Weight} unlockLevel={r.UnlockLevel}");
        }
    }
    Dump("reward", table.Rewards);
    Dump("alternate", table.AlternateRewards);
    Dump("forced", table.ForcedRewards);
}
return sb.ToString();
