# Better Terrain Tools for Unity

**Better Terrain Tools** is a collection of high-precision sculpting tools for the Unity Terrain System. Unlike standard Unity tools, this suite uses a floating-point height cache to prevent the "stepping" or "staircase" artifacts, providing much smoother results and sculpting logic that gives you more control and is more intuitive to use.

## Included Tools

* **Raise and Lower** The core sculpting tool, enhanced with some special effects.
  * **Fill Mode (context aware):** Simulates how sand flows from the bottom and up. Super helpful to fill in crevasses or add height on a mountain wall without affecting the wall.
  * **Set (Relative Clamp) Mode:** Prevents the terrain from moving further than a specific distance from its starting state. Great for creating rivers or raising a portion of your map without destroying the existing structure of the landscape.
  * **Smoothing:** Smooth the results as you paint, to eliminate jagged results.
* **Flatten** Like the unity flatten tool, but with better rules
  * **Extend Mode:** Only extends cliffs outwards from where you paint
  * **Carve Mode:** Only carves into the hills
  * **Flatten:** Standard flattening
* **Smudge** Pulls terrain height along the mouse path, allowing you to "drag" slopes or smear textures organically.

## Hotkeys

* **Shift + Scroll:** Resize Brush.
* **Ctrl + Scroll:** Adjust Strength/Opacity.
* **Alt + Scroll:** Adjust Brush Tip (Roundness/Falloff).

## Installation

Install the package with unity's package manager

1. Open the Package Manager via `Window/Package Manager`.
2. Select the + from the top left of the window.
3. Select **Add package from Git URL**.
4. Enter `https://github.com/Querke/better-terrain-tools.git`
5. Select **Add**.
