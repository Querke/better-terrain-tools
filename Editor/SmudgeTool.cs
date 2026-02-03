using UnityEngine;
using UnityEditor;

namespace BetterTerrainTools
{
	using UnityEditor.TerrainTools;
	using Terrain = UnityEngine.Terrain;

	internal class SmudgeTool : BaseBetterTerrainTool<SmudgeTool>
	{
		public override int IconIndex => 2;
		public override string OnIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/Smooth_On.png";
		public override string OffIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/Smooth.png";

		public override string GetName() => "Better terrain tools - Smudge";
		public override string GetDescription() => "Click and drag to Smudge terrain.\n(Pulls height along the mouse path)";

		private Vector2 _lastUv;
		private bool _hasStartedStroke;

		public override bool OnPaint(Terrain terrain, IOnPaint editContext)
		{
			// Reset stroke if mouse just went down or we are far from last point (teleport/new click)
			if (Event.current.type == EventType.MouseDown || !_hasStartedStroke)
			{
				_lastUv = GetBrushUV();
				_hasStartedStroke = true;
				// On first frame of click, we can't smudge yet as we have no delta
				return true;
			}

			// Calculate how much the mouse moved in UV space
			Vector2 currentUv = GetBrushUV();
			Vector2 uvDelta = currentUv - _lastUv;

			// If we haven't moved enough to calculate a smudge direction, skip
			if (uvDelta.sqrMagnitude < 0.0000001f)
				return false;

			Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Terrain Smudge");

			// 1. Setup Cache and Brush
			float[,] cachedHeights = GetHeightCache(terrain);
			int heightMapRes = terrain.terrainData.heightmapResolution;

			float brushSizeWorld = _brushSize;
			float brushSizeRatio = brushSizeWorld / terrain.terrainData.size.x;
			int brushPixelsSize = Mathf.CeilToInt(heightMapRes * brushSizeRatio);
			if (brushPixelsSize % 2 != 0)
				brushPixelsSize++;

			float[,] brushMask = GenerateBrushMask(brushPixelsSize, false);

			// 2. Convert UV movement to Pixel movement
			// We negate the delta because we want to sample from "behind" the movement
			int shiftX = Mathf.RoundToInt(-uvDelta.x * heightMapRes);
			int shiftY = Mathf.RoundToInt(-uvDelta.y * heightMapRes);

			// 3. Calculate Bounds
			int centerX = Mathf.FloorToInt(currentUv.x * heightMapRes);
			int centerY = Mathf.FloorToInt(currentUv.y * heightMapRes);

			int xBase = centerX - (brushPixelsSize / 2);
			int yBase = centerY - (brushPixelsSize / 2);

			int xStart = Mathf.Max(0, xBase);
			int yStart = Mathf.Max(0, yBase);
			int xEnd = Mathf.Min(heightMapRes, xBase + brushPixelsSize);
			int yEnd = Mathf.Min(heightMapRes, yBase + brushPixelsSize);

			int width = xEnd - xStart;
			int height = yEnd - yStart;

			if (width <= 0 || height <= 0)
			{
				_lastUv = currentUv;
				return false;
			}

			float[,] patchHeights = new float[height, width];

			// 4. Perform Smudge
			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					int globalX = xStart + x;
					int globalY = yStart + y;

					int brushX = globalX - xBase;
					int brushY = globalY - yBase;

					float currentHeight = cachedHeights[globalY, globalX];

					// Default to current height (no change)
					float targetHeight = currentHeight;

					// Calculate the "Source" pixel we are dragging FROM
					int sourceX = globalX + shiftX;
					int sourceY = globalY + shiftY;

					if (brushX >= 0 && brushX < brushPixelsSize && brushY >= 0 && brushY < brushPixelsSize)
					{
						if (sourceX >= 0 && sourceX < heightMapRes && sourceY >= 0 && sourceY < heightMapRes)
						{
							float sourceHeightVal = cachedHeights[sourceY, sourceX];
							float maskValue = brushMask[brushX, brushY];

							// Apply aggressiveness via _smudgeStrength
							float weight = maskValue * _brushOpacity;
							targetHeight = Mathf.Lerp(currentHeight, sourceHeightVal, weight);
						}
					}

					cachedHeights[globalY, globalX] = targetHeight;
					patchHeights[y, x] = targetHeight;
				}
			}

			terrain.terrainData.SetHeights(xStart, yStart, patchHeights);

			// Update state for next frame
			_lastUv = currentUv;

			return true;
		}

		// Reset state when tool is deselected or mouse released
		public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
		{
			base.OnSceneGUI(terrain, editContext);
			if (Event.current.type == EventType.MouseUp)
			{
				_hasStartedStroke = false;
			}
		}
	}
}