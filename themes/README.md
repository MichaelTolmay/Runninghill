# Runninghill themes

The paired JSON files supply the same colours to the website and native apps.
After changing a palette, run `python3 scripts/check-themes.py --generate` to
refresh the website's startup colours.

Both `Runninghill_light_icon.webp` and `Runninghill_dark_icon.webp` preserve the
supplied logo and its transparent background. The website uses the image's alpha
channel as a CSS mask, painting the lettering with the palette's `Text` colour.
MAUI applies `TintColor="#1F2937"` to the light image during its normal image
packaging step; the dark image keeps the original white lettering. This preserves
the logo's exact shape without adding image processing at app startup. If the
light lettering colour changes, update the MAUI project's tint as well.

The dark page background is `#9D9EA6`. The logo has no background rectangle.
It stays within a 256 × 128 logical-pixel box, aligned with the left edge of the
content on desktop and tablet websites. At widths of 560 pixels or less it moves
to the centre. Android and iOS apps always centre it; native desktop windows
follow the website breakpoint.
