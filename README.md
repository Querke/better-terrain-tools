# Better Terrain Tools for Unity

https://github.com/user-attachments/assets/049b7dbd-48b2-4796-aa80-75062af5f6dc



https://github.com/user-attachments/assets/d2950dca-392f-46ea-bd2e-caf101dd98f8



https://github.com/user-attachments/assets/ef9f3b86-da25-4f4b-a039-134ac40d8b60




**Better Terrain Tools** is a collection of high-precision sculpting tools for the Unity Terrain System. 

It has a procedural brush, which makes it easy to have fine control over the brush tip falloff (hard/soft) without having to deal with the hassle of creating and importing brush textures. 

Unlike standard Unity tools, this suite uses a floating-point height cache to prevent the "stepping" or "staircase" artifacts, providing much smoother results and sculpting logic that gives you more control and is more intuitive to use.

It also raycasts directly onto unity's heightmap, because Unity has a bug where it doesn't update the terrain collider correctly.

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

## How to use
<img width="265" height="303" alt="image" src="https://github.com/user-attachments/assets/6aa639f5-308a-4f8b-9d1e-e8f46b982ea7" />

Click the custom brushes mode in Unity's terrain scene toolbar. Or select the tool from the tool dropdown in the inspector.

## Hotkeys

* **Alt + Scroll:** Resize Brush.
* **Ctrl + Scroll:** Adjust Strength/Opacity.
* **Shift + Scroll:** Adjust Brush falloff (soft/hard).

## Installation

Install the package with unity's package manager

1. Open the Package Manager via `Window/Package Manager`.
2. Select the + from the top left of the window.
3. Select **Add package from Git URL**.
4. Enter `https://github.com/Querke/better-terrain-tools.git`
5. Select **Add**.

## Thanks to

http://terrainformer.com/ for inspiring me to create better terrain tools and the idea for height cache to preserve detail and extend and carve with the flatten tool.
