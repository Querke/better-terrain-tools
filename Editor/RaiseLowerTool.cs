namespace BetterTerrainTools
{
	using UnityEditor;
	using UnityEditor.TerrainTools;
	using UnityEngine;
	using Terrain = UnityEngine.Terrain;

	internal class RaiseLowerTool : BaseBetterTerrainTool<RaiseLowerTool>
	{
		public override int IconIndex => 0;

		public override string GetName() => "Better terrain tools - Raise and lower";

		public override string GetDescription() => "Click to Raise.\nCtrl + Click to Lower.\nFill mode prevents adding height above your cursors.";

		[SerializeField] protected int _mode;

		[SerializeField] private bool _fillMode;

		[SerializeField] private float _additiveClamp;

		[SerializeField] private bool _enableSmoothing;

		private float[,] _startHeights;

		private float _wallBlendRange = 3;
		private const float SMOOTH_STRENGTH = 4f;
		private const float STRUCTURE_PERSISTENCE = 1f; // Lower = preserves structure more

		private string[] _modes = {"Additive", "Set"};
		private string[] _fillModes = {"Off", "On"};

		protected override void OnSubToolGui()
		{
			base.OnSubToolGui();

			_mode = GUILayout.Toolbar(_mode, _modes);

			if (_mode == 1)
			{
				_additiveClamp = EditorGUILayout.Slider("Relative clamp (m)", _additiveClamp, 0.01f, 50f);
			}

			EditorGUILayout.BeginHorizontal();
			GUILayout.Label("Fill", GUILayout.Width(EditorGUIUtility.labelWidth));
			_fillMode = GUILayout.Toolbar((_fillMode ? 1 : 0), _fillModes) == 1;

			EditorGUILayout.EndHorizontal();
			if (_fillMode)
			{
				_wallBlendRange = EditorGUILayout.Slider("Wall blend range (m)", _wallBlendRange, 1f, 5f);
			}

			EditorGUILayout.BeginHorizontal();
			GUILayout.Label("Smoothing", GUILayout.Width(EditorGUIUtility.labelWidth));
			_enableSmoothing = GUILayout.Toolbar((_enableSmoothing ? 1 : 0), _fillModes) == 1;

			EditorGUILayout.EndHorizontal();
			// if (_enableSmoothing)
			// {
			// 	SMOOTH_STRENGTH = EditorGUILayout.Slider("Smooth strength", SMOOTH_STRENGTH, 0.01f, 1f);
			// 	STRUCTURE_PERSISTENCE = EditorGUILayout.Slider("Structure persistence", STRUCTURE_PERSISTENCE, 0.0001f, 10f);
			// }
		}

		protected override void OnMouseUp(Terrain terrain)
		{
			base.OnMouseUp(terrain);
			ClearLimitedHeight();
		}

		private void ClearLimitedHeight() => _startHeights = null;

		public override bool OnPaint(Terrain terrain, IOnPaint editContext)
		{
			Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Terrain Sculpt");
			float[,] cachedHeights = GetHeightCache(terrain);

			// Snapshot baseline for Relative Clamp
			if (_additiveClamp > 0.001f)
			{
				if (_startHeights == null || _startHeights.GetLength(0) != cachedHeights.GetLength(0))
					_startHeights = (float[,]) cachedHeights.Clone();
			}

			Vector2 uv = GetBrushUV();
			float terrainHeight = terrain.terrainData.size.y;
			float centerHeightNorm = terrain.terrainData.GetInterpolatedHeight(uv.x, uv.y) / terrainHeight;

			// Conversion from World Meters to 0-1 Space
			float rangeNorm = Mathf.Max(0.00001f, _wallBlendRange / terrainHeight);
			float clampNorm = _additiveClamp / terrainHeight;

			float brushSizeWorld = _brushSize;
			float brushSizeRatio = brushSizeWorld / terrain.terrainData.size.x;
			int brushPixelsSize = Mathf.CeilToInt(terrain.terrainData.heightmapResolution * brushSizeRatio);
			if (brushPixelsSize % 2 != 0)
				brushPixelsSize++;

			float[,] brushMask = GenerateBrushMask(brushPixelsSize, true);

			int centerX = Mathf.FloorToInt(uv.x * terrain.terrainData.heightmapResolution);
			int centerY = Mathf.FloorToInt(uv.y * terrain.terrainData.heightmapResolution);
			int xBase = centerX - (brushPixelsSize / 2);
			int yBase = centerY - (brushPixelsSize / 2);

			int xStart = Mathf.Max(0, xBase);
			int yStart = Mathf.Max(0, yBase);
			int xEnd = Mathf.Min(terrain.terrainData.heightmapResolution, xBase + brushPixelsSize);
			int yEnd = Mathf.Min(terrain.terrainData.heightmapResolution, yBase + brushPixelsSize);

			int width = xEnd - xStart;
			int height = yEnd - yStart;
			if (width <= 0 || height <= 0)
				return false;

			float[,] patchHeights = new float[height, width];
			bool isLowering = Event.current.control;

			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					int globalX = xStart + x;
					int globalY = yStart + y;
					float heightVal = cachedHeights[globalY, globalX];
					float maskValue = brushMask[globalX - xBase, globalY - yBase];

					if (maskValue > 0.0000001f)
					{
						float influenceFactor = 1f;

						if (_fillMode)
						{
							// Difference between Pixel and Brush Center
							// Positive = Pixel is higher (Up a wall/hill)
							// Negative = Pixel is lower (In a hole/valley)
							float diff = heightVal - centerHeightNorm;

							// Calculate ratio based on user's Blend Range setting
							float ratio = diff / rangeNorm;

							if (isLowering)
							{
								// LOWERING: Dig freely if we are above the center (walls), 
								// but fade out if we try to dig below the center (the floor).
								if (diff > 0)
									influenceFactor = 1f;
								else
									influenceFactor = Mathf.Exp(-ratio * ratio);
							}
							else
							{
								// RAISING (Sand Mode):
								// If pixel is BELOW center (Hole): Fill with 100% strength.
								// If pixel is ABOVE center (Hill/Wall): Fade out exponentially.
								// This avoids the "Plateau" because it never hits a hard 0 limit.

								if (diff < 0)
									influenceFactor = 1f;
								else
									influenceFactor = Mathf.Exp(-ratio * ratio);
							}
						}

						// 2. Apply Change
						if (_mode == 1 && _additiveClamp > 0.001f)
						{
							// Relative Clamp Logic
							float originalVal = _startHeights[globalY, globalX];
							float targetHeight = isLowering
								? Mathf.Max(0f, originalVal - clampNorm)
								: Mathf.Min(1f, originalVal + clampNorm);

							float step = maskValue * influenceFactor * _brushOpacity * 0.05f;
							heightVal = Mathf.MoveTowards(heightVal, targetHeight, step);
						}
						else
						{
							// Standard Logic
							float sample = (maskValue * influenceFactor) / (terrain.terrainData.heightmapScale.y);
							heightVal = isLowering ? Mathf.Max(0f, heightVal - sample) : Mathf.Min(1f, heightVal + sample);
						}
					}

					cachedHeights[globalY, globalX] = heightVal;
					patchHeights[y, x] = heightVal;
				}
			}

			if (_enableSmoothing)
				ApplySmoothing(terrain, xStart, yStart, width, height, xBase, yBase, cachedHeights, patchHeights, brushMask);

			terrain.terrainData.SetHeights(xStart, yStart, patchHeights);
			return true;
		}

		private void ApplySmoothing(Terrain terrain, int xStart, int yStart, int width, int height, int xBase, int yBase, float[,] cachedHeights, float[,] patchHeights,
			float[,] brushMask)
		{
			float[,] smoothedPatch = new float[height, width];
			int res = terrain.terrainData.heightmapResolution;

			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					int globalX = xStart + x;
					int globalY = yStart + y;

					float currentH = cachedHeights[globalY, globalX];
					float maskValue = brushMask[globalX - xBase, globalY - yBase];

					// Skip smoothing entirely if the brush has no influence here
					if (maskValue <= 0.0000001f)
					{
						smoothedPatch[y, x] = currentH;
						continue;
					}

					float sum = 0;
					float totalWeight = 0;

					for (int kx = -1; kx <= 1; kx++)
					{
						for (int ky = -1; ky <= 1; ky++)
						{
							int sx = Mathf.Clamp(globalX + kx, 0, res - 1);
							int sy = Mathf.Clamp(globalY + ky, 0, res - 1);

							float neighborH = cachedHeights[sy, sx];
							float diff = Mathf.Abs(currentH - neighborH);

							float weight = Mathf.Exp(-diff / STRUCTURE_PERSISTENCE);

							sum += neighborH * weight;
							totalWeight += weight;
						}
					}

					float smoothedH = sum / totalWeight;

					// Scaled strength: Smoothing strength is now multiplied by the brush falloff
					float effectiveStrength = SMOOTH_STRENGTH * maskValue;
					smoothedPatch[y, x] = Mathf.Lerp(currentH, smoothedH, effectiveStrength);
				}
			}

			for (int x = 0; x < width; x++)
			{
				for (int y = 0; y < height; y++)
				{
					cachedHeights[yStart + y, xStart + x] = smoothedPatch[y, x];
					patchHeights[y, x] = smoothedPatch[y, x];
				}
			}
		}
	}
}