// Manually repeat the last finished expedition whose location contains the given text.
// Usage: edit the filter below, then: tools/tpm eval -f tools/snippets/repeat_now.csx
return ((Func<string, string>)Host.Exports["Repeat.StartNow"])("Botchley");
