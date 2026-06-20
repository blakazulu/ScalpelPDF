# Adding or Improving a Scalpel Translation

## File format

Each language is a single XAML `ResourceDictionary` file in this folder. The filename must be the BCP 47 language tag for the locale:

- `en-US.xaml` - English (US)
- `es.xaml` - Spanish
- `zh-TW.xaml` - Traditional Chinese

## How to contribute

### Editing an existing translation

1. Open the file for your language in GitHub (e.g. `Strings/zh-TW.xaml`)
2. Click the pencil icon to edit
3. Translate the text between the tags - **do not change the `x:Key` values**
4. Click "Commit changes" and open a pull request

### Adding a new language

1. Copy `en-US.xaml` and rename it to the BCP 47 tag for your language
2. Translate every value - leave the `x:Key` attributes untouched
3. Open a pull request with the new file

## Rules

- **Never change `x:Key` values.** The app looks these up by key at runtime.
- **Keep format placeholders intact.** Some strings contain `{0}`, `{1}`, etc. - these are filled in by the app at runtime and must stay in the translation, in the same order.
- **Keep XML entities.** `&amp;` means `&`, `&#xE711;` is a glyph code - leave them as-is.
- The file must be valid XML. You can check by pasting it into [xmllint.com](https://www.xmllint.com) or any XML validator.

## Format string example

```xml
<!-- English -->
<sys:String x:Key="Str_Opened">Opened {0} - {1} page(s)</sys:String>

<!-- Spanish -->
<sys:String x:Key="Str_Opened">Abierto {0} - {1} página(s)</sys:String>
```

`{0}` will be replaced with the filename and `{1}` with the page count. The placeholders must stay in the translation.

## Testing your translation

If you want to see your strings in the app before submitting, build from source and change the language in Settings. Otherwise, submit the PR and the maintainer will test it.

## Questions

Open a GitHub issue or leave a comment on your pull request.
