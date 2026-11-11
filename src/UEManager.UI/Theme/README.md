# Theme

Everything here is compiled into `UEModManager.exe`. Nothing ships as a loose file.

| File | Purpose |
| --- | --- |
| `Palette.xaml` | The shared look: colors, brushes and font families. |
| `Games/<game id>.xaml` | Optional. A game's own colors: the keys of `Palette.xaml` it changes. |
| `Controls.xaml` | Styles for buttons, inputs, navigation, scroll bars and text. |
| `Art/<game id>/logo.png` | Square emblem with a transparent background, no text. Shown at 60 x 60 at the top of the sidebar, above the game name (which the UI draws as text). 256 x 256 is enough. |
| `Art/<game id>/background.png` or `.jpg` | Cover art, wide (about 3:1), no text. Anchored to the top right of the content area and faded out toward the lower left. Keep the subject in the upper right and the left side dark. |
| `Art/<game id>/card.png` | The game's card in the game picker, 16:9, 480 x 270. Shown at 352 x 198. Text such as the game's logo is fine here. |
| `Art/backdrop.jpg` | Behind the game picker at a start without a chosen game, blurred. 1920 x 1080. |
| `Fonts/*.ttf`, `Fonts/*.otf` | Optional. Built in as resources; reference one from `Palette.xaml`. |

`<game id>` is the `id` of the game's profile, such as `phantom-blade-zero`.

## A game's look

The app runs for one game at a time. At the start it loads `Palette.xaml`, then the
game's `Games/<game id>.xaml` when there is one, then `Controls.xaml`
(`Services/GameTheme.cs`). Every style takes the game's colors that way. A change
of game starts the app again, so the look is never changed while it runs.

A game file holds only keys that are in `Palette.xaml` (a test checks this). A key
it leaves out keeps the shared value. A brush must name its color again: a brush
of `Palette.xaml` keeps that file's color, even when the game changes the `Color`
it was made from. `Games/mortal-shell-2.xaml` shows every key a game can change:
the ink, the title bar and sidebar, the panels, fields, buttons and lines, the
veils, the text and the accent. The colors of states (green, warning, error,
blue) and of other services stay the same for every game.

The settings editor takes its colors from the look too: `Color.Syntax.Comment`,
`Color.Syntax.Key` (also `[Section]` lines and Lua's words), `Color.Syntax.String`,
`Color.Syntax.Value` (numbers, true/false) and `Brush.Selection`. The files in
`Syntax/` hold the rules; their own colors are only the fallback. Choose colors
that stay readable on the game's `Brush.Field`: a contrast of 4.5:1 or more, 4:1 for
comments (a test checks this).

The views and their code name no color themselves (a test checks this too). A new
color goes into `Palette.xaml`, and into a game's file when the game needs another one.

## Images

Every image is optional. Without a logo the sidebar shows the app's drawn mark,
the mod stack (`BrandMark` in `Controls.xaml`); without cover art the content area
is plain ink with a faint glow in the accent color; without a card the picker
shows the mark.

Keep the files reasonably small: they are embedded in the exe. A JPEG at quality
90 or so is fine for the cover and the backdrop; the logo needs a PNG for its
transparency.

## Fonts

To use a bundled font for headings, set `Font.Heading` in `Palette.xaml` to the
font's family name, for example:

```xml
<FontFamily x:Key="Font.Heading">pack://application:,,,/Theme/Fonts/#Cinzel</FontFamily>
```

Check that a font's license allows redistribution before adding it.

The exe icon is `../app.ico`: the mod stack, three plates leaning right (crimson
`#C0182F` on top, then `#9EA3AA` and `#767B83`) on an anthracite tile (`#1A1C20`,
edge `#2C2F35`), in 16, 20, 24, 32, 40, 48, 64, 128 and 256 px, each drawn on its
own. The title bar and the drawn mark show the same plates in the game's colors.
