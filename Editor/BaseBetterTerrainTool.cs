using UnityEngine;
using UnityEditor;
using UnityEngine.TerrainTools;
using UnityEditor.TerrainTools;
using System.Collections.Generic;

namespace BetterTerrainTools
{
	using Terrain = Terrain;

	public abstract class BaseBetterTerrainTool<T> : TerrainToolsPaintTool<T>, BetterTerrainToolOverlayGui where T : TerrainToolsPaintTool<T>
	{
		public override string OnIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintHeight_On.png";
		public override string OffIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintHeight.png";

		public override int IconIndex => 0;
		public override bool HasToolSettings => true;
		public override bool HasBrushAttributes => false;

		// --- Settings ---
		[SerializeField] protected float _brushSize = 50f;
		[SerializeField] protected float _brushOpacity = 0.1f;

		// CHANGED: Renamed from Roundness to Falloff for clarity
		[SerializeField, Range(0f, 1f)]
		protected float _brushFalloff = 0.5f;

		// --- State ---
		private Vector2 _customUV;

		// --- Cache ---
		private Texture2D _previewBrushTexture;
		private Texture2D _guiBrushTexture;
		private readonly Dictionary<Terrain, float[,]> _heightCache = new();

		public override void OnEnterToolMode()
		{
			base.OnEnterToolMode();
			Undo.undoRedoPerformed += OnUndoRedo;
			BetterTerrainOverlay.ActiveTool = this;
			BetterTerrainOverlay.SetDisplayed(true);
		}

		public override void OnExitToolMode()
		{
			base.OnExitToolMode();
			Undo.undoRedoPerformed -= OnUndoRedo;
			_heightCache.Clear();
			BetterTerrainOverlay.SetDisplayed(false);
		}

		public void DrawOverlayGui() => RenderGui();

		private void OnUndoRedo()
		{
			_heightCache.Clear();
			SceneView.RepaintAll();
		}

		protected float[,] GetHeightCache(Terrain terrain)
		{
			if (!_heightCache.TryGetValue(terrain, out float[,] cache))
			{
				int res = terrain.terrainData.heightmapResolution;
				cache = terrain.terrainData.GetHeights(0, 0, res, res);
				_heightCache[terrain] = cache;
			}

			return cache;
		}

		protected Vector2 GetBrushUV() => _customUV;

		private void RenderGui()
		{
			EditorGUI.BeginChangeCheck();

			GUILayout.BeginHorizontal();
			GUILayout.BeginVertical();
			OnSubToolGui();
			EditorGUILayout.Space();

			EditorGUILayout.LabelField("Brush Settings", EditorStyles.boldLabel);

			_brushSize = EditorGUILayout.Slider("Brush Size", _brushSize, 1f, 500f);
			_brushOpacity = EditorGUILayout.Slider("Strength", _brushOpacity, 0.01f, 1f);

			// CHANGED: Label is now "Falloff"
			_brushFalloff = EditorGUILayout.Slider("Falloff", _brushFalloff, 0f, 1f);

			EditorGUILayout.Space();
			GUILayout.EndVertical();

			if (EditorGUI.EndChangeCheck())
			{
				UpdatePreviewTexture();
				UpdateGuiTexture();
				SceneView.RepaintAll();
			}

			if (_guiBrushTexture == null)
				UpdateGuiTexture();

			EditorGUILayout.Space();
			GUILayout.Label(_guiBrushTexture);
			GUILayout.EndHorizontal();
			EditorGUILayout.HelpBox("Alt: Size | Ctrl: Strength | Shift: Falloff", MessageType.Info);
		}

		public override void OnInspectorGUI(UnityEngine.Terrain terrain, IOnInspectorGUI editContext) => RenderGui();

		protected virtual void OnSubToolGui()
		{
		}

		public override void OnToolSettingsGUI(Terrain terrain, IOnInspectorGUI editContext) => RenderGui();

		protected virtual void OnMouseUp(Terrain terrain)
		{
		}

		public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
		{
			Event currentEvent = Event.current;
			Ray mouseRay = HandleUtility.GUIPointToWorldRay(currentEvent.mousePosition);

			Vector3 hitWorldPos = Vector3.zero;

			// Standard CPU Raycast (Planar Projection removed as requested)
			if (RaycastTerrainCPU(mouseRay, terrain, out hitWorldPos))
			{
				_customUV = WorldToUV(terrain, hitWorldPos);
			}
			else
			{
				_customUV = new Vector2(-1, -1);
			}

			if (currentEvent.type == EventType.MouseUp)
				OnMouseUp(terrain);

			base.OnSceneGUI(terrain, editContext);
			HandleShortcuts(currentEvent);
		}

		/// <summary>
		/// Custom Raymarching to find exact terrain height without Physics/Colliders.
		/// </summary>
		private bool RaycastTerrainCPU(Ray ray, Terrain terrain, out Vector3 hitPoint)
		{
			hitPoint = Vector3.zero;
			TerrainData data = terrain.terrainData;
			Vector3 terrainPos = terrain.transform.position;
			Vector3 terrainSize = data.size;

			Bounds bounds = data.bounds;
			bounds.center += terrainPos;

			if (!bounds.IntersectRay(ray, out float distanceToBox))
				return false;

			Vector3 startPoint = ray.GetPoint(distanceToBox);
			if (distanceToBox <= 0)
				startPoint = ray.origin;

			int steps = 100;
			float maxDist = Mathf.Sqrt(terrainSize.x * terrainSize.x + terrainSize.z * terrainSize.z + terrainSize.y * terrainSize.y);
			float stepSize = maxDist / steps;

			Vector3 currentPos = startPoint;
			Vector3 direction = ray.direction * stepSize;

			for (int i = 0; i < steps; i++)
			{
				float u = (currentPos.x - terrainPos.x) / terrainSize.x;
				float v = (currentPos.z - terrainPos.z) / terrainSize.z;

				if (u >= 0 && u <= 1 && v >= 0 && v <= 1)
				{
					float terrainHeight = data.GetInterpolatedHeight(u, v) + terrainPos.y;
					if (currentPos.y <= terrainHeight)
					{
						hitPoint = BinarySearchRefine(ray, terrain, currentPos - direction, currentPos, 10);
						return true;
					}
				}

				currentPos += direction;
			}

			return false;
		}

		private Vector3 BinarySearchRefine(Ray ray, Terrain terrain, Vector3 start, Vector3 end, int iterations)
		{
			Vector3 terrainPos = terrain.transform.position;
			Vector3 size = terrain.terrainData.size;
			Vector3 mid = start;
			for (int i = 0; i < iterations; i++)
			{
				mid = (start + end) * 0.5f;
				float u = (mid.x - terrainPos.x) / size.x;
				float v = (mid.z - terrainPos.z) / size.z;
				float h = terrain.terrainData.GetInterpolatedHeight(u, v) + terrainPos.y;
				if (mid.y < h)
					end = mid;
				else
					start = mid;
			}

			return mid;
		}

		private Vector2 WorldToUV(Terrain terrain, Vector3 worldPoint)
		{
			Vector3 terrainPos = terrain.GetPosition();
			Vector3 terrainSize = terrain.terrainData.size;
			return new Vector2((worldPoint.x - terrainPos.x) / terrainSize.x, (worldPoint.z - terrainPos.z) / terrainSize.z);
		}

		private void HandleShortcuts(Event currentEvent)
		{
			if (currentEvent.type != EventType.ScrollWheel)
				return;

			float direction = currentEvent.delta.y > 0 ? -1f : 1f;

			if (currentEvent.alt)
			{
				// 0.05f = 5% growth per tick. 
				// At size 10, it adds 0.5. At size 100, it adds 5. At size 500, it adds 25.
				ModifyBrushParamExponential(ref _brushSize, direction, 0.05f, 1f, 500f);
			}
			else if (currentEvent.control) // Opacity (Linear)
			{
				ModifyBrushParamExponential(ref _brushOpacity, direction, 0.05f, 0.01f, 1f);
			}
			else if (currentEvent.shift) // Falloff (Linear)
			{
				direction = currentEvent.delta.x > 0 ? -1f : 1f;
				ModifyBrushParamExponential(ref _brushFalloff, direction, 0.05f, 0.001f, 1f);
			}

			SceneView.RepaintAll();
			currentEvent.Use();
		}

		private void ModifyBrushParamExponential(ref float param, float direction, float rate, float min, float max)
		{
			float step = param * rate;

			param += step * direction;
			param = Mathf.Clamp(param, min, max);

			UpdatePreviewTexture();
			UpdateGuiTexture();
		}

		public override void OnRenderBrushPreview(Terrain terrain, IOnSceneGUI editContext)
		{
			if (Event.current.type != EventType.Repaint)
				return;
			if (_previewBrushTexture == null)
				UpdatePreviewTexture();

			Vector2 uv = _customUV;
			if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
				return;

			BrushTransform brushXform = TerrainPaintUtility.CalculateBrushTransform(terrain, uv, _brushSize, 0);
			PaintContext paintContext = TerrainPaintUtility.BeginPaintHeightmap(terrain, brushXform.GetBrushXYBounds(), 1);
			Material previewMaterial = TerrainPaintUtilityEditor.GetDefaultBrushPreviewMaterial();

			TerrainPaintUtilityEditor.DrawBrushPreview(paintContext, TerrainBrushPreviewMode.SourceRenderTexture, _previewBrushTexture, brushXform, previewMaterial, 0);
			RenderIntoPaintContext(paintContext, _previewBrushTexture, brushXform);
			RenderTexture.active = paintContext.oldRenderTexture;
			previewMaterial.SetTexture("_HeightmapOrig", paintContext.sourceRenderTexture);
			TerrainPaintUtilityEditor.DrawBrushPreview(paintContext, TerrainBrushPreviewMode.DestinationRenderTexture, _previewBrushTexture, brushXform, previewMaterial, 1);
			TerrainPaintUtility.ReleaseContextResources(paintContext);
		}

		private void RenderIntoPaintContext(PaintContext paintContext, Texture brushTexture, BrushTransform brushXform)
		{
			Material mat = TerrainPaintUtility.GetBuiltinPaintMaterial();
			mat.SetTexture("_BrushTex", brushTexture);
			var opacity = Mathf.Lerp(0.03f, 0.1f, _brushOpacity) * (Event.current.control ? -1 : 1);
			mat.SetVector("_BrushParams", new Vector4(opacity / 100f, 0.0f, 0.0f, 0.0f));
			TerrainPaintUtility.SetupTerrainToolMaterialProperties(paintContext, brushXform, mat);
			Graphics.Blit(paintContext.sourceRenderTexture, paintContext.destinationRenderTexture, mat, 0);
		}

		private Texture2D CreateBrushTexture2D(bool isGui, int size, float gamma = 1)
		{
			Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
			texture.wrapMode = TextureWrapMode.Clamp;
			texture.hideFlags = HideFlags.HideAndDontSave;
			float[,] mask = GenerateBrushMask(size, false);
			Color32[] colors = new Color32[size * size];

			for (int y = 0; y < size; y++)
			{
				for (int x = 0; x < size; x++)
				{
					float rawValue = mask[x, y];
					rawValue = Mathf.Pow(rawValue, gamma);
					byte value = (byte) (rawValue * 255);
					colors[y * size + x] = isGui ? new Color32(255, 255, 255, value) : new Color32(value, 0, 0, 255);
				}
			}

			texture.SetPixels32(colors);
			texture.Apply();
			return texture;
		}

		private void UpdatePreviewTexture() => _previewBrushTexture = CreateBrushTexture2D(false, 128, 0.3f);
		private void UpdateGuiTexture() => _guiBrushTexture = CreateBrushTexture2D(true, 64);

		// CHANGED: New algorithm using Falloff + Radius + SmoothStep
		protected float[,] GenerateBrushMask(int size, bool useBrushOpacity)
		{
			float[,] samples = new float[size, size];
			Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
			float radius = size * 0.5f;

			// Falloff 0 = Inner Radius is 100% (Hard Cylinder)
			// Falloff 1 = Inner Radius is 0% (Cone/Bell)
			float innerRadius = radius * (1.0f - _brushFalloff);

			for (int x = 0; x < size; x++)
			{
				for (int y = 0; y < size; y++)
				{
					float dist = Vector2.Distance(new Vector2(x, y), center);

					float value = 0f;

					if (dist >= radius)
					{
						value = 0f;
					}
					else if (dist <= innerRadius)
					{
						// Inside the hard inner circle
						value = 1f;
					}
					else
					{
						// In the falloff zone
						// Calculate "t" where 0 is at InnerRadius and 1 is at OuterRadius
						float range = radius - innerRadius;
						float t = (dist - innerRadius) / range;

						// SmoothStep gives a nice S-curve falloff (Standard for Unreal/Terrain engines)
						// We flip t (1, 0) because we want 1 at inner and 0 at outer
						value = Mathf.SmoothStep(1f, 0f, t);
					}

					samples[x, y] = value * (useBrushOpacity ? _brushOpacity : 1);
				}
			}

			return samples;
		}
	}
}