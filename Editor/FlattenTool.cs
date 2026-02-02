using UnityEngine;
using UnityEditor;

namespace BetterTerrainTools
{
	using UnityEditor.TerrainTools;
	using Terrain = UnityEngine.Terrain;

	internal class FlattenTool : BaseBetterTerrainTool<FlattenTool>
	{
		public override string OnIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/FlattenSlope_On.png";
		public override string OffIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/FlattenSlope.png";
		public override int IconIndex => 1;

		public enum FlattenMode
		{
			Extend,
			Flatten,
			Carve
		}

		[SerializeField] private int _mode;
		private float _targetHeight = 0f;

		public override string GetName() => "Better terrain tools - Flatten";

		public override string GetDescription() => "Flattens terrain to a specific height.";

		private string[] _modes = {"Extend", "Flatten", "Carve"};

		protected override void OnSubToolGui()
		{
			_mode = GUILayout.Toolbar(_mode, _modes);
		}

		public override bool OnPaint(Terrain terrain, IOnPaint editContext)
		{
			if (Event.current.type == EventType.MouseDown)
			{
				float h = (editContext.raycastHit.point.y - terrain.GetPosition().y) / terrain.terrainData.size.y;
				_targetHeight = h;
			}

			Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Terrain Flatten");

			float[,] cachedHeights = GetHeightCache(terrain);
			int heightMapRes = terrain.terrainData.heightmapResolution;
			Vector3 terrainSize = terrain.terrainData.size;

			// --- Brush Math ---
			float brushSizeWorld = _brushSize;
			float brushSizeRatio = brushSizeWorld / terrainSize.x;
			int brushPixelsSize = Mathf.CeilToInt(heightMapRes * brushSizeRatio);
			if (brushPixelsSize % 2 != 0)
				brushPixelsSize++;

			float[,] brushMask = GenerateBrushMask(brushPixelsSize, true);

			Vector2 uv = editContext.uv;
			int centerX = Mathf.FloorToInt(uv.x * heightMapRes);
			int centerY = Mathf.FloorToInt(uv.y * heightMapRes);

			int xBase = centerX - (brushPixelsSize / 2);
			int yBase = centerY - (brushPixelsSize / 2);

			int xStart = Mathf.Max(0, xBase);
			int yStart = Mathf.Max(0, yBase);
			int xEnd = Mathf.Min(heightMapRes, xBase + brushPixelsSize);
			int yEnd = Mathf.Min(heightMapRes, yBase + brushPixelsSize);

			int width = xEnd - xStart;
			int height = yEnd - yStart;

			if (width <= 0 || height <= 0)
				return false;

			float[,] patchHeights = new float[height, width];

			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					int globalX = xStart + x;
					int globalY = yStart + y;

					int brushX = globalX - xBase;
					int brushY = globalY - yBase;

					if (brushX >= 0 && brushX < brushPixelsSize && brushY >= 0 && brushY < brushPixelsSize)
					{
						float currentHeight = cachedHeights[globalY, globalX];
						float maskValue = brushMask[brushX, brushY];
						float desiredHeight = _targetHeight;

						float finalTarget = _mode switch
						{
							(int) FlattenMode.Extend => Mathf.Max(currentHeight, desiredHeight),
							(int) FlattenMode.Carve => Mathf.Min(currentHeight, desiredHeight),
							_ => desiredHeight
						};

						float newHeight = Mathf.Lerp(currentHeight, finalTarget, maskValue);

						cachedHeights[globalY, globalX] = newHeight;
						patchHeights[y, x] = newHeight;
					}
					else
					{
						patchHeights[y, x] = cachedHeights[globalY, globalX];
					}
				}
			}

			terrain.terrainData.SetHeights(xStart, yStart, patchHeights);
			return true;
		}
	}
}