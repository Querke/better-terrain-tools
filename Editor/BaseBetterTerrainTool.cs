using UnityEngine;
using UnityEditor;
using UnityEngine.TerrainTools;
using UnityEditor.TerrainTools;
using System.Collections.Generic; // Required for Dictionary

namespace BetterTerrainTools
{
	using Editor = Editor;
	using Terrain = Terrain;

	public abstract class BaseBetterTerrainTool<T> : TerrainToolsPaintTool<T>, BetterTerrainToolOverlayGui where T : TerrainToolsPaintTool<T>
	{
		public override string OnIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintHeight_On.png";
		public override string OffIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintHeight.png";

		public override int IconIndex => 0;
		public override bool HasToolSettings => true;
		public override bool HasBrushAttributes { get; }

		// --- Settings ---
		[SerializeField] protected float _brushSize = 50f;
		[SerializeField] protected float _brushOpacity = 0.1f;

		// How wide the center "flat" area is
		[SerializeField, Range(0f, 1f)]
		protected float _brushRoundness = 0.5f;

		// --- Cache ---
		private Texture2D _previewBrushTexture;
		private Texture2D _guiBrushTexture;

		// --- HIGH PRECISION HEIGHT CACHE  ---
		// Stores the "True" float values of the terrain to prevent quantization errors.
		private readonly Dictionary<Terrain, float[,]> _heightCache = new();

		public override void OnEnterToolMode()
		{
			base.OnEnterToolMode();
			// Subscribe to Undo so we can clear the cache if the user hits Ctrl+Z
			Undo.undoRedoPerformed += OnUndoRedo;
			BetterTerrainOverlay.ActiveTool = this;
			BetterTerrainOverlay.SetDisplayed(true);
		}

		public override void OnExitToolMode()
		{
			base.OnExitToolMode();
			Undo.undoRedoPerformed -= OnUndoRedo;
			// Clear cache to free memory
			_heightCache.Clear();
			BetterTerrainOverlay.SetDisplayed(false);
		}

		public void DrawOverlayGui() => RenderGui();

		private void OnUndoRedo()
		{
			_heightCache.Clear();
		}

		/// <summary>
		/// Retrieves the High-Precision height map for a terrain. 
		/// Creates it if it doesn't exist.
		/// </summary>
		protected float[,] GetHeightCache(Terrain terrain)
		{
			if (!_heightCache.TryGetValue(terrain, out float[,] cache))
			{
				// Initialize cache with current terrain data
				int res = terrain.terrainData.heightmapResolution;
				cache = terrain.terrainData.GetHeights(0, 0, res, res);
				_heightCache[terrain] = cache;
			}

			return cache;
		}

		// ------------------------------------------------

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
			_brushRoundness = EditorGUILayout.Slider("Brush tip", _brushRoundness, 0, 1);

			GUILayout.EndVertical();

			if (EditorGUI.EndChangeCheck())
			{
				UpdatePreviewTexture();
				UpdateGuiTexture();
				SceneView.RepaintAll();
			}

			// --- GUI Preview ---
			if (_guiBrushTexture == null)
				UpdateGuiTexture();

			EditorGUILayout.Space();
			GUILayout.Label(_guiBrushTexture);
			GUILayout.EndHorizontal();
			EditorGUILayout.HelpBox("Use scrollwheel+[mod] to change brush settings\nShift: Size | Ctrl: Strength | Alt: Brush tip", MessageType.Info);
		}

		public override void OnInspectorGUI(UnityEngine.Terrain terrain, IOnInspectorGUI editContext)
		{
			RenderGui();
		}

		protected virtual void OnSubToolGui()
		{
		}

		public override void OnToolSettingsGUI(Terrain terrain, IOnInspectorGUI editContext)
		{
			RenderGui();
		}

		private void Repaint()
		{
			UpdatePreviewTexture();
			UpdateGuiTexture();

			var inspectorType = typeof(Editor).Assembly.GetType("UnityEditor.InspectorWindow");
			var inspectors = Resources.FindObjectsOfTypeAll(inspectorType);

			foreach (var inspector in inspectors)
			{
				// Call the "Repaint" method on the window via reflection
				// This is safer than casting because InspectorWindow is internal
				var repaintMethod = inspectorType.GetMethod("Repaint");
				if (repaintMethod != null)
				{
					repaintMethod.Invoke(inspector, null);
				}
			}
		}

		protected virtual void OnMouseUp(Terrain terrain)
		{
		}

		public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
		{
			base.OnSceneGUI(terrain, editContext);
			var currentEvent = Event.current;

			if (currentEvent.type == EventType.MouseUp)
			{
				OnMouseUp(terrain);
			}

			if (currentEvent.type != EventType.ScrollWheel)
			{
				return;
			}

			if (currentEvent.shift)
			{
				float scrollDelta = currentEvent.delta.x > 0 ? 1 : -1;
				_brushSize -= _brushSize < 10 ? scrollDelta : _brushSize < 100 ? scrollDelta * 5 : scrollDelta * 10;
				Repaint();
				currentEvent.Use();
			}
			else if (currentEvent.control)
			{
				float scrollDelta = currentEvent.delta.y > 0 ? 1 : -1;
				_brushOpacity -= _brushOpacity < 0.25f ? scrollDelta * 0.03f : _brushOpacity < 0.5f ? scrollDelta * 0.05f : scrollDelta * 0.06f;
				_brushOpacity = Mathf.Clamp(_brushOpacity, 0.01f, 1);
				Repaint();
				currentEvent.Use();
			}
			else if (currentEvent.alt)
			{
				float scrollDelta = currentEvent.delta.y > 0 ? 1 : -1;
				_brushRoundness -= _brushRoundness < 0.25f ? scrollDelta * 0.03f : _brushRoundness < 0.5f ? scrollDelta * 0.05f : scrollDelta * 0.06f;
				_brushRoundness = Mathf.Clamp(_brushRoundness, 0.01f, 1);
				Repaint();
				currentEvent.Use();
			}
		}

		public override void OnRenderBrushPreview(Terrain terrain, IOnSceneGUI editContext)
		{
			if (Event.current.type != EventType.Repaint)
				return;

			if (_previewBrushTexture == null)
				UpdatePreviewTexture();

			if (!editContext.hitValidTerrain)
				return;

			BrushTransform brushXform = TerrainPaintUtility.CalculateBrushTransform(terrain, editContext.raycastHit.textureCoord, _brushSize, 0);
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

					// --- gamma boost for brush preview
					// You can tweak 0.5f. Lower (e.g. 0.3) = wider visible outline. 1.0 = exact linear match.
					rawValue = Mathf.Pow(rawValue, gamma);

					byte value = (byte) (rawValue * 255);

					if (isGui)
					{
						colors[y * size + x] = new Color32(255, 255, 255, value);
					}
					else
					{
						colors[y * size + x] = new Color32(value, 0, 0, 255);
					}
				}
			}

			texture.SetPixels32(colors);
			texture.Apply();

			return texture;
		}

		private void UpdatePreviewTexture()
		{
			_previewBrushTexture = CreateBrushTexture2D(false, 128, 0.3f);
		}

		private void UpdateGuiTexture()
		{
			_guiBrushTexture = CreateBrushTexture2D(true, 64);
		}

		protected float[,] GenerateBrushMask(int size, bool useBrushOpacity)
		{
			float[,] samples = new float[size, size];

			Vector2 center = new Vector2(size * 0.5f, size * 0.5f);
			float radius = size * 0.5f;

			AnimationCurve falloffCurve = new AnimationCurve();

			float tangentStrength = 3.0f;

			float startTangent = (1.0f - _brushRoundness) * tangentStrength;
			float endTangent = _brushRoundness * tangentStrength;

			Keyframe key0 = new Keyframe(0, 0);
			key0.outTangent = startTangent;

			Keyframe key1 = new Keyframe(1, 1);
			key1.inTangent = endTangent;

			falloffCurve.AddKey(key0);
			falloffCurve.AddKey(key1);

			for (int x = 0; x < size; x++)
			{
				for (int y = 0; y < size; y++)
				{
					float dist = Vector2.Distance(new Vector2(x, y), center) / radius;

					if (dist >= 1.0f)
					{
						samples[x, y] = 0f;
						continue;
					}

					// Invert distance: 1.0 at center, 0.0 at edge
					float curveTime = 1.0f - dist;

					// Evaluate
					float sample = falloffCurve.Evaluate(curveTime);

					// Apply Opacity
					samples[x, y] = sample * (useBrushOpacity ? _brushOpacity : 1);
				}
			}

			return samples;
		}
	}
}