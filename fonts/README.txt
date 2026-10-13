Fonts for the weekday banner.

The widget loads fonts from this folder BY PATH, not from the Windows font list,
so nothing here is installed system-wide and nothing needs admin rights. See
BannerFont.Resolve() in ..\src\DayBanner.cs.


Michroma-Regular.ttf - bundled, the default
-------------------------------------------
Copyright 2011 The Michroma Project Authors
(https://github.com/googlefonts/Michroma-font)
Licence:   SIL Open Font License 1.1 - full text in Michroma-OFL.txt
Source:    https://github.com/google/fonts/tree/main/ofl/michroma

The OFL allows the font to be bundled and redistributed unmodified, which is why
it can ship with the app and the banner looks the same on every machine.


Anurati - optional, NOT bundled
-------------------------------
Typeface:  Anurati, designed by Emmeran Richard
Licence:   free for PERSONAL use only
Commercial use requires the paid release:
           https://www.emmeranrichard.fr/foundry/anurati-pro/

The banner was originally designed around Anurati. Its licence does not allow
redistribution, so it is not in this repository and .gitignore keeps it out. If
you have your own copy, drop the .otf into this folder and the banner will use
it in preference to Michroma - no setting to change.

The font's internal family name must be exactly "Anurati" for the banner to pick
it up. If it does not match, the banner simply carries on with Michroma. Run
..\check-font.ps1 to see what family names WPF actually finds here and which one
will be used.

Anurati is an uppercase-only display face. That is fine here because the banner
uppercases the weekday name before rendering.

Tracking note: with Anurati the banner spaces letters by 0.4714 em, derived by
measuring the reference screenshot rather than by eye. Anurati's cap height is
exactly 0.8 em, which is the constant that conversion depends on - see the
comment in BannerFont before changing it. Michroma is wider and heavier, so it
is set at 85% of the size with 0.30 em tracking to fill about the same line.
