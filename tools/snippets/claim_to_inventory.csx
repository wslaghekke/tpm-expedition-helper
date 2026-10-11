// Claim the first returned crate whose reward can go to inventory, like clicking "Send to inventory".
// Uses the mod's ExpeditionActions (raises RewardClaimed with the correct struct marshalling).
return ((Func<string>)Host.Exports["Expeditions.ClaimFirstToInventory"])();
