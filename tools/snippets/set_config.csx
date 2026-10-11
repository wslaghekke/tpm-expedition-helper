// Set config values in memory (BepInEx saves them to the .cfg immediately).
void Set<T>(string section, string key, T value) =>
    ((BepInEx.Configuration.ConfigEntry<T>)Host.Config[new BepInEx.Configuration.ConfigDefinition(section, key)]).Value = value;
Set("Repeat", "MaxRepeats", 1);
Set("Repeat", "RepeatExpeditions", true);
return string.Join(", ", new[] { "RepeatExpeditions", "MaxRepeats", "UntilPristine", "UntilMaxKnowledge", "OnlyWhenProfitable", "KeepMonthlyCostsCovered", "MinCashReserve", "Notifications" }
    .Select(k => $"{k}={Host.Config[new BepInEx.Configuration.ConfigDefinition("Repeat", k)].BoxedValue}"));
