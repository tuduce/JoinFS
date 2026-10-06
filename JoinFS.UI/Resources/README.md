# Strings

`Strings.resx` is the English text, `Strings.<lang>.resx` the translations: de es fr it ko nl pt (Brazilian) ru, the languages
the old forms had. The key is the English text itself; see `Localization/Loc.cs`.

To add a text, write it as `{l:T 'English text'}` in XAML or `Loc.T("English text")` / `Loc.F("... {0}", arg)` in C#, then add
it to `Strings.resx` (key and value the same) and to every `Strings.<lang>.resx`. `LocalizationTests` fails when a text is looked up
and not in `Strings.resx`, when a language lacks a text, and when a translation drops a `{0}` placeholder. Keys are compared
without regard to case by the resource compiler, so two texts may not differ only in capitals (`Upper=True` in XAML shows a
text in capitals, for headings).
