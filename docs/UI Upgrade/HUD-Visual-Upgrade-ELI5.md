# HUD Visual Upgrade — the plain-English one-pager

No graphics jargon. This is what the fancy words mean and what we can actually do.

## The one big idea
There are **two kinds** of effects, and the difference decides how hard each one is:

1. **"Self-decorating" effects** — the panel dresses *itself* up (glows, sweeps, colored edges,
   sharper lines). **Cheap, easy, and they update instantly when we hot-reload (F6).** Most of your
   wishlist is here.
2. **"See-through glass" effects** — the panel has to actually **look at the game world behind it**
   (blur it, tint it, bend it). **More expensive and trickier**, because our HUD is drawn *on top of*
   everything, so it can't just "see" what's behind it. To do it, we take a **snapshot of the game
   world the instant before the HUD draws**, blur that snapshot **once**, and let **every** glass panel
   share the same blurred snapshot. That "blur once, share with all" trick is what keeps it cheap no
   matter how many glass panels you have.

## Your questions, answered simply
- **"Why do thin lines vanish instead of fading?"** Right now we make a line thinner by *shrinking*
  it, and past a certain point it's too small for the screen to draw, so it just blinks out. The fix:
  keep the line at least 1 pixel wide but make it **more transparent** the thinner it "should" be — so
  it **fades away gently** like a dimmer, instead of shrinking to nothing. This lets us go as thin as
  you want. ✅ Easy.
- **"Can the border be lighter in some spots, darker in others?"** Yes — we **pretend a light is
  shining from one direction**, so the edge is bright where it faces the light and dim where it faces
  away. The box borders can already do this; the drawn lines just need it added. ✅ Easy.

## What each fancy effect actually is
- **Frosted glass** = like a shower door: you see blurry shapes of the world behind, darkened and
  tinted blue. *(See-through — the expensive kind, but doable.)*
- **Shine sweep** = a glint of light sliding along an edge, like sun catching chrome trim. ✅ Easy-ish.
- **Breathing glow** = the glow slowly brightening and dimming, like calm breathing. ✅ Easy.
- **Boot-up / power-on reveal** = panels flickering to life like an old sci-fi screen. ✅ Medium.
- **Iridescence** = the oil-slick / soap-bubble rainbow sheen on glass edges. ✅ Medium (keep it subtle).
- **Color fringing** = a faint rainbow edge like a cheap camera lens. ✅ Easy-ish.
- **Razor-crisp shapes** = a smarter way to draw boxes and icons so lines stay perfectly sharp at any
  size or zoom. ✅ Big job, biggest payoff for crispness.

## What we simply **can't** do (and why)
- We **can't** make the border shimmer based on your head moving — a flat panel facing you head-on
  barely changes with view angle, so we **fake** it with a pretended light. Looks right; just isn't
  "real physics."
- We **can't** use the fancy modern-Unity render features — the game runs an older rendering mode.
  Everything here is built to work *within* that, which is why we lean on tried-and-true tricks.
- The "grab what's behind me" shortcut other tutorials use **doesn't work** for our HUD (it's an
  overlay), so we use the snapshot method instead. Slightly more setup, same result.

## One catch worth knowing
Simple effects live in **code** — they hot-reload with F6, so we iterate fast. The fancier effects
need a **"paint recipe" (a shader)** baked into the mod package, which **can't** hot-reload — changing
one means rebuilding the package. So we'll keep the *controls* in code (sliders, timing) and only the
recipe itself in the package.

## Recommended order (easy wins first)
1. **Fix the thin lines + gradient borders + a gentle breathing glow + readability.** Cheap, instant,
   solves what you asked about today.
2. **The "self-decorating" flair pack:** shine sweeps, boot-up reveals, edge iridescence.
3. **The real frosted glass** (blur + tint the world behind panels) — the showpiece, more effort.
4. **(Optional) the razor-crisp rebuild** for perfectly sharp shapes at any size.

*Full technical details, exact costs, and links to the research papers are in
`HUD-Visual-Upgrade-Plan.md` and the two research docs.*
