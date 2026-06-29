namespace BetterTerrainTools
{
	using System;
	using System.Collections.Generic;
	using System.IO;
	using System.Linq;
	using System.Reflection;
	using Superleap.Editor;
	using Superleap.Terrain;
	using UnityEditor;
	using UnityEditor.TerrainTools;
	using UnityEngine;
	using UnityEngine.TerrainTools;
	using Object = UnityEngine.Object;
	using Random = UnityEngine.Random;
	using Terrain = UnityEngine.Terrain;

	public abstract class BaseBetterTerrainFilteredTool<T> : BaseBetterTerrainTool<T>, BetterTerrainToolOverlayLayout where T : TerrainToolsPaintTool<T>
	{
		public virtual float OverlayWidth => 320f;
		public virtual float OverlayHeight => 260f;
		protected override bool ShowGuiBrushTexture => false;
		protected override bool ShowShortcutHelp => !IsDrawingOverlayGui;

		protected Terrain _targetTerrain;

		[SerializeField] protected float _slopeMin;

		[SerializeField] protected float _slopeMax = 90;

		[SerializeField] protected float _slopeFalloff = 15;

		[SerializeField] protected Noise _noise;

		[SerializeField] protected bool _noiseEnabled;

		[SerializeField] protected AnimationCurve _applicationCurve;

		[SerializeField] private float _visualizationOpacity = 0.35f;

		[SerializeField] private Texture2D _slopeVisualizationTexture;

		private const string NOISE_VIS_ID = "noise";
		private const string SLOPE_VIS_ID = "slope";
		private readonly Color SLOPE_COLOR = Color.red;
		private readonly Color NOISE_COLOR = new Color(1, 0.5f, 0.1f, 1);

		private double _timeSinceSlopeChanged;
		private double _timeSinceNoiseChanged;

		private bool _hasGeneratedTextureFullResSlope;
		private bool _hasGeneratedTextureLowResSlope;
		private bool _hasGeneratedTextureLowResNoise;
		private Terrain _currentTerrainVisualized;

		private bool _previewFiltersOnTerrain;

		private readonly int[] _resolutionOptions = {32, 64, 128, 256, 512, 1024, 2048, 4096};

		private static int s_CurrentOperationUndoGroup = -1;
		private static List<UnityEngine.Object> s_CurrentOperationUndoStack = new List<UnityEngine.Object>();

		public abstract string GetToolName();
		public abstract string GetToolDesc();

		#region Terrain Tool overrides

		public override string GetName()
		{
			return "Better terrain tools - " + GetToolName();
		}

		public override string GetDescription()
		{
			return GetToolDesc();
		}

		public override void OnEnterToolMode()
		{
			base.OnEnterToolMode();
			UpdateTargetTerrainFromSelection();
		}

		public override void OnExitToolMode()
		{
			base.OnExitToolMode();
			_hasGeneratedTextureFullResSlope = false;
			_hasGeneratedTextureLowResSlope = false;
			_previewFiltersOnTerrain = false;
			TerrainDebugVisualizer.StopVisualizationAll();
		}

		public override bool OnPaint(Terrain terrain, IOnPaint editContext)
		{
			if (_targetTerrain == null)
			{
				UpdateTargetTerrainFromSelection();
			}

			if (_targetTerrain == null)
			{
				return false;
			}

			EnsureFilterSettings();
			return true;
		}

		protected abstract void OnToolSpecificGUI(Terrain terrain);

		protected override void OnSubToolGui()
		{
			EnsureFilterSettings();
			Terrain terrain = GetTargetTerrainForGui();
			if (terrain == null || terrain.terrainData == null)
			{
				EditorGUILayout.HelpBox("Select a terrain to use this tool.", MessageType.Info);
				return;
			}

			OnToolSpecificGUI(terrain);
		}

		protected override void OnPostBrushSettingsGui()
		{
			EnsureFilterSettings();
			Terrain terrain = GetTargetTerrainForGui();
			if (terrain == null || terrain.terrainData == null)
			{
				return;
			}

			if (IsDrawingOverlayGui)
			{
				DrawCompactFilterSettings(terrain);
			}
			else
			{
				DrawFilterSettings(terrain);
			}
		}

		public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
		{
			if (_targetTerrain == null)
			{
				_targetTerrain = terrain;
			}

			base.OnSceneGUI(terrain, editContext);

			// We're only doing painting operations, early out if it's not a repaint
			if (Event.current.type != EventType.Repaint)
				return;

			if (_previewFiltersOnTerrain)
			{
				if (_currentTerrainVisualized != terrain)
				{
					_timeSinceSlopeChanged = EditorApplication.timeSinceStartup;
					_timeSinceNoiseChanged = EditorApplication.timeSinceStartup;
					_hasGeneratedTextureLowResSlope = false;
					_hasGeneratedTextureFullResSlope = false;
					_hasGeneratedTextureLowResNoise = false;
					_currentTerrainVisualized = terrain;
				}

				VisualizeSlopeFilter(terrain);
			}
		}

		public override void OnRenderBrushPreview(Terrain terrain, IOnSceneGUI editContext)
		{
			// We're only doing painting operations, early out if it's not a repaint
			if (Event.current.type != EventType.Repaint)
				return;

			Vector2 uv = GetBrushUV();
			if (uv.x < 0 || uv.x > 1 || uv.y < 0 || uv.y > 1)
				return;

			BrushTransform brushXform = TerrainPaintUtility.CalculateBrushTransform(terrain, uv, _brushSize, 0.0f);
			PaintContext ctx = TerrainPaintUtility.BeginPaintHeightmap(terrain, brushXform.GetBrushXYBounds(), 1);
			TerrainPaintUtilityEditor.DrawBrushPreview(
				ctx,
				TerrainBrushPreviewMode.SourceRenderTexture,
				GetPreviewBrushTexture(),
				brushXform,
				TerrainPaintUtilityEditor.GetDefaultBrushPreviewMaterial(),
				0
			);
			TerrainPaintUtility.ReleaseContextResources(ctx);
		}

		#endregion Terrain Tool overrides

		#region Tool functionality

		private void DrawFilterSettings(Terrain terrain)
		{
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Filter Settings", EditorStyles.boldLabel);

			EditorGUI.BeginChangeCheck();
			EditorGUILayout.BeginHorizontal();

			_slopeMin = EditorGUILayout.FloatField("Slope", _slopeMin);
			GUILayout.Space(4);
			EditorGUILayout.MinMaxSlider(ref _slopeMin, ref _slopeMax, 0, 90f);
			GUILayout.Space(4);
			_slopeMax = EditorGUILayout.FloatField(_slopeMax, GUILayout.MaxWidth(EditorGUIUtility.fieldWidth));
			EditorGUILayout.EndHorizontal();

			_slopeMin = Mathf.RoundToInt(_slopeMin);
			_slopeMax = Mathf.RoundToInt(_slopeMax);
			if (_slopeMax <= _slopeMin)
			{
				_slopeMax = _slopeMin + 1;
			}

			_slopeFalloff = EditorGUILayout.Slider("Falloff", _slopeFalloff, 0, 90);

			EditorGUILayout.BeginHorizontal();
			_applicationCurve = EditorGUILayout.CurveField("Application modifier", _applicationCurve);
			if (GUILayout.Button("Reset"))
			{
				_applicationCurve = null;
				EnsureFilterSettings();
			}

			EditorGUILayout.EndHorizontal();

			bool shouldUpdateSlopeVisualization = EditorGUI.EndChangeCheck();
			shouldUpdateSlopeVisualization |= DrawAdditionalFilterSettings(terrain);
			bool shouldUpdateNoiseVisualization = false;

			GUILayout.Space(10);

			EditorGUI.BeginChangeCheck();
			bool wasNoiseEnabledPrior = _noiseEnabled;
			_noiseEnabled = EditorGUILayout.Toggle("Noise enabled", _noiseEnabled);
			if (EditorGUI.EndChangeCheck())
			{
				shouldUpdateNoiseVisualization = _noiseEnabled != wasNoiseEnabledPrior;
			}

			if (_noiseEnabled)
			{
				EditorGUILayout.BeginVertical("GroupBox");
				GUILayout.BeginVertical();

				EditorGUI.BeginChangeCheck();
				_noise.Resolution = EditorGUILayout.IntPopup(
					"Resolution",
					_noise.Resolution,
					_resolutionOptions.Select(s => s.ToString()).ToArray(),
					_resolutionOptions
				);

				_noise.Inverted = EditorGUILayout.Toggle("Inverted", _noise.Inverted);
				_noise.Opacity = EditorGUILayout.Slider("Opacity", _noise.Opacity, 0.0001f, 10);
				_noise.Scale = EditorGUILayout.Slider("Scale", _noise.Scale, 2, 500);
				_noise.Octaves = EditorGUILayout.IntSlider("Octaves", _noise.Octaves, 1, 6);
				_noise.Persistence = EditorGUILayout.Slider("Persistence", _noise.Persistence, 0, 2);
				_noise.Lacunarity = EditorGUILayout.Slider("Lacunarity", _noise.Lacunarity, 0, 4);
				_noise.Offset = EditorGUILayout.Vector2Field("Offset", _noise.Offset);
				_noise.Smoothen = EditorGUILayout.Toggle("Smoothen", _noise.Smoothen);

				GUILayout.EndVertical();

				GUILayout.BeginVertical();
				float textureGuiSize = 96;
				GUILayout.BeginHorizontal();
				if (GUILayout.Button("Randomize"))
				{
					_noise.Seed = Random.Range(0, 10000);
					_noise.UpdatePreviewTexture();
					shouldUpdateNoiseVisualization = true;
				}

				_noise.Seed = EditorGUILayout.IntField(_noise.Seed, GUILayout.Width(48));
				GUILayout.EndHorizontal();
				if (EditorGUI.EndChangeCheck())
				{
					_noise.UpdatePreviewTexture();
					shouldUpdateNoiseVisualization = true;
				}

				GUILayout.Label(_noise.PreviewTexture, GUILayout.Width(textureGuiSize), GUILayout.Height(textureGuiSize));
				if (GUILayout.Button("Reset", GUILayout.Width(textureGuiSize)))
				{
					_noise = new Noise();
					_noise.UpdatePreviewTexture();
					shouldUpdateNoiseVisualization = true;
				}

				GUILayout.EndVertical();

				if (GUILayout.Button("Save noise texture to disk"))
				{
					var noiseTex = _noise.GetfullResTexture();
					if (noiseTex != null)
					{
						byte[] bytes = noiseTex.EncodeToPNG();
						string path = Application.dataPath + "/Terrain/TerrainVisualizationNoise_" + EditorApplication.timeSinceStartup + ".png";
						File.WriteAllBytes(path, bytes);
						AssetDatabase.Refresh();
						Debug.Log("Saved noise texture to: " + path);
					}
				}

				EditorGUILayout.EndVertical();
			}

			if (_previewFiltersOnTerrain)
			{
				GUI.color = Color.green;
				GUILayout.Label("VISUALIZATION ACTIVE");
				GUI.color = Color.white;
				EditorGUI.BeginChangeCheck();
				_visualizationOpacity = EditorGUILayout.Slider("Visualization Opacity", _visualizationOpacity, 0.1f, 1);
				if (EditorGUI.EndChangeCheck())
				{
					SceneView.RepaintAll();
				}
			}

			if (shouldUpdateNoiseVisualization)
			{
				_hasGeneratedTextureLowResNoise = false;
				_timeSinceNoiseChanged = EditorApplication.timeSinceStartup;
				SceneView.RepaintAll();
			}

			if (shouldUpdateSlopeVisualization)
			{
				_hasGeneratedTextureLowResSlope = false;
				_timeSinceSlopeChanged = EditorApplication.timeSinceStartup;
				SceneView.RepaintAll();
			}

			GUILayout.Space(10);

			if (GUILayout.Button(_previewFiltersOnTerrain ? "Stop Visualization" : "Visualize on terrain", GUILayout.Height(40)))
			{
				_previewFiltersOnTerrain = !_previewFiltersOnTerrain;
				if (!_previewFiltersOnTerrain)
				{
					_hasGeneratedTextureFullResSlope = false;
					_hasGeneratedTextureLowResSlope = false;
					if (_noiseEnabled)
					{
						_hasGeneratedTextureLowResNoise = true;
					}

					TerrainDebugVisualizer.StopVisualizationAll();
				}

				SceneView.RepaintAll();
			}

			if (_previewFiltersOnTerrain && GUILayout.Button("Save visualization texture to disk"))
			{
				if (_slopeVisualizationTexture != null)
				{
					byte[] bytes = _slopeVisualizationTexture.EncodeToPNG();
					string path = Application.dataPath + "/Terrain/TerrainVisualizationTexture_" + EditorApplication.timeSinceStartup + ".png";
					File.WriteAllBytes(path, bytes);
					AssetDatabase.Refresh();
					Debug.Log("Saved visualization texture to: " + path);
				}
			}
		}

		private void DrawCompactFilterSettings(Terrain terrain)
		{
			EditorGUI.BeginChangeCheck();
			EditorGUILayout.BeginHorizontal();

			_slopeMin = EditorGUILayout.FloatField("Slope", _slopeMin);
			GUILayout.Space(4);
			EditorGUILayout.MinMaxSlider(ref _slopeMin, ref _slopeMax, 0, 90f);
			GUILayout.Space(4);
			_slopeMax = EditorGUILayout.FloatField(_slopeMax, GUILayout.MaxWidth(EditorGUIUtility.fieldWidth));
			EditorGUILayout.EndHorizontal();

			_slopeMin = Mathf.RoundToInt(_slopeMin);
			_slopeMax = Mathf.RoundToInt(_slopeMax);
			if (_slopeMax <= _slopeMin)
			{
				_slopeMax = _slopeMin + 1;
			}

			if (EditorGUI.EndChangeCheck())
			{
				_hasGeneratedTextureLowResSlope = false;
				_timeSinceSlopeChanged = EditorApplication.timeSinceStartup;
				SceneView.RepaintAll();
			}

			if (DrawCompactAdditionalFilterSettings(terrain))
			{
				_hasGeneratedTextureLowResSlope = false;
				_timeSinceSlopeChanged = EditorApplication.timeSinceStartup;
				SceneView.RepaintAll();
			}
		}

		protected float GetFilteredSample(TerrainData terrainData, float normalizedX, float normalizedY)
		{
			EnsureFilterSettings();

			float steepnessMin = Mathf.Max(_slopeMin - _slopeFalloff, 0);
			float steepnessMax = Mathf.Min(_slopeMax + _slopeFalloff, 90);
			float steepness = terrainData.GetSteepness(normalizedX, normalizedY);
			float sample = 0;
			// Falloff
			if (steepness >= steepnessMin &&
			    steepness <= steepnessMax)
			{
				sample = 1;
				if (steepness < _slopeMin)
				{
					sample = Mathf.Lerp(0, 1, ((steepness + _slopeFalloff) - _slopeMin) / _slopeFalloff);
				}
				else if (steepness > _slopeMax)
				{
					sample = Mathf.Lerp(1, 0, (steepness - _slopeMax) / _slopeFalloff);
				}

				if (_noiseEnabled)
				{
					sample = _noise.SampleNoiseMap(normalizedX, normalizedY);
				}

				sample = _applicationCurve.Evaluate(sample);
				sample *= Mathf.Clamp01(GetAdditionalFilteredSample(terrainData, normalizedX, normalizedY));
			}

			return sample;
		}

		protected virtual bool DrawAdditionalFilterSettings(Terrain terrain)
		{
			return false;
		}

		protected virtual bool DrawCompactAdditionalFilterSettings(Terrain terrain)
		{
			return false;
		}

		protected virtual float GetAdditionalFilteredSample(TerrainData terrainData, float normalizedX, float normalizedY)
		{
			return 1f;
		}

		private void VisualizeSlopeFilter(Terrain terrain)
		{
			if (terrain == null || terrain.terrainData == null || !_previewFiltersOnTerrain)
			{
				if (!_previewFiltersOnTerrain && TerrainDebugVisualizer.ContainsKey(SLOPE_VIS_ID))
				{
					TerrainDebugVisualizer.StopVisualization(SLOPE_VIS_ID);
					SceneView.RepaintAll();
				}

				return;
			}

			if (!TerrainDebugVisualizer.ContainsKey(SLOPE_VIS_ID))
			{
				UpdateSlopeFilterTexture(terrain, true);
				TerrainDebugVisualizer.StartVisualization(SLOPE_VIS_ID, _slopeVisualizationTexture, terrain, 0, _visualizationOpacity);
				_timeSinceSlopeChanged = EditorApplication.timeSinceStartup - 0.5f;
				_hasGeneratedTextureFullResSlope = false;
				_hasGeneratedTextureLowResSlope = true;
			}

			if (EditorApplication.timeSinceStartup - _timeSinceSlopeChanged < 1.1f || EditorApplication.timeSinceStartup - _timeSinceNoiseChanged < 1.1f)
			{
				if (!_hasGeneratedTextureLowResSlope || !_hasGeneratedTextureLowResNoise)
				{
					UpdateSlopeFilterTexture(terrain, true);
					TerrainDebugVisualizer.UpdateVisualization(SLOPE_VIS_ID, _slopeVisualizationTexture, terrain, 0, _visualizationOpacity);
					_hasGeneratedTextureLowResSlope = true;
					_hasGeneratedTextureLowResNoise = true;
				}

				_hasGeneratedTextureFullResSlope = false;
				// _hasGeneratedTextureFullResNoise = false;
				SceneView.RepaintAll();
				return;
			}

			if (_hasGeneratedTextureFullResSlope)
			{
				return;
			}

			UpdateSlopeFilterTexture(terrain, false);
			TerrainDebugVisualizer.UpdateVisualization(SLOPE_VIS_ID, _slopeVisualizationTexture, terrain, 0, _visualizationOpacity);

			_hasGeneratedTextureFullResSlope = true;
		}

		private void UpdateSlopeFilterTexture(Terrain terrain, bool lowRes)
		{
			if (_slopeVisualizationTexture != null)
			{
				DestroyImmediate(_slopeVisualizationTexture);
			}

			// int resolution = lowRes ? 512 : 1024;
			int resolution = 512;

			_slopeVisualizationTexture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false);
			var pixels = new Color32[resolution * resolution];

			Color c = SLOPE_COLOR;
			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					float normalizedX = ((float) x / resolution);
					float normalizedY = ((float) y / resolution);

					float steepness = terrain.terrainData.GetSteepness(normalizedX, normalizedY);

					float steepnessMin = Mathf.Max(_slopeMin - _slopeFalloff, 0);
					float steepnessMax = Mathf.Min(_slopeMax + _slopeFalloff, 90);
					if (steepness >= steepnessMin && steepness <= steepnessMax)
					{
						float falloffModifier = 0;
						// Falloff
						if (steepness < _slopeMin)
						{
							falloffModifier = Mathf.Lerp(1, 0, ((steepness + _slopeFalloff) - _slopeMin) / _slopeFalloff);
						}
						else if (steepness > _slopeMax)
						{
							falloffModifier = Mathf.Lerp(0, 1, (steepness - _slopeMax) / _slopeFalloff);
						}

						c.a = falloffModifier;
						pixels[y * resolution + x] = c;
					}
					else
					{
						c.a = 1;
						pixels[y * resolution + x] = c;
					}
				}
			}

			_slopeVisualizationTexture.SetPixels32(pixels);
			_slopeVisualizationTexture.Apply();
			UpdateSlopeFilterWithNoise(lowRes);
		}

		private void UpdateSlopeFilterWithNoise(bool lowRes)
		{
			if (!_noiseEnabled)
			{
				return;
			}

			int resolution = lowRes ? Noise.PREVIEW_RESOLUTION : _noise.Resolution;
			TextureScaler.Rescale(_slopeVisualizationTexture, resolution, resolution);
			Color32[] pixels = _slopeVisualizationTexture.GetPixels32();
			Color c = NOISE_COLOR;

			Color32[] noiseTex = lowRes ? _noise.PreviewTexture.GetPixels32() : _noise.GetfullResTexture().GetPixels32();
			for (int y = 0; y < resolution; y++)
			{
				for (int x = 0; x < resolution; x++)
				{
					if (pixels[y * resolution + x].a > 254)
					{
						continue;
					}

					Color32 slopeColor32 = pixels[y * resolution + x];
					Color slopeColor = new Color(slopeColor32.r / 255f, slopeColor32.g / 255f, slopeColor32.b / 255f, slopeColor32.a / 255f);
					c = NOISE_COLOR;

					c.a = (255 - noiseTex[y * resolution + x].a) / 255f;
					c = Color.Lerp(c, slopeColor, slopeColor.a);

					pixels[y * resolution + x] = c;
				}
			}

			_slopeVisualizationTexture.SetPixels32(pixels);
			_slopeVisualizationTexture.Apply();
		}

		// Proper way of doing undo
		protected static void UpdateTerrainDataUndo(TerrainData terrainData, string undoName)
		{
			// if we are in a new undo group (new operation) then start with an empty list
			if (Undo.GetCurrentGroup() != s_CurrentOperationUndoGroup)
			{
				s_CurrentOperationUndoGroup = Undo.GetCurrentGroup();
				s_CurrentOperationUndoStack.Clear();
			}

			if (!s_CurrentOperationUndoStack.Contains(terrainData))
			{
				s_CurrentOperationUndoStack.Add(terrainData);
				Undo.RegisterCompleteObjectUndo(terrainData, undoName);
			}
		}

		#endregion Tool functionality

		#region Additional GUI functionality

		protected abstract int GetDefaultTerrainTool();

		public void Repaint()
		{
			Type unityTerrainInspectorType = Assembly.GetAssembly(typeof(Editor)).GetType("UnityEditor.TerrainInspector");
			Editor[] ed = (Editor[]) Resources.FindObjectsOfTypeAll<Editor>();
			for (int i = 0; i < ed.Length; i++)
			{
				if (ed[i].GetType() == unityTerrainInspectorType)
				{
					ed[i].Repaint();
					return;
				}
			}
		}

		public void ShowButtonAddPrototype()
		{
			if (GUILayout.Button("Add/remove/update prototype objects"))
			{
				if (Selection.activeGameObject == null)
				{
					return;
				}

				Type unityTerrainInspectorType = Assembly.GetAssembly(typeof(Editor)).GetType("UnityEditor.TerrainInspector");
				PropertyInfo unityTerrainSelectedTool = unityTerrainInspectorType.GetProperty("selectedTool", BindingFlags.NonPublic | BindingFlags.Instance);

				Object[] terrainInspectors = Resources.FindObjectsOfTypeAll(unityTerrainInspectorType);
				// Iterate through each Unity terrain inspector to find the Terrain Inspector(s) that belongs to this object
				foreach (Object inspector in terrainInspectors)
				{
					Editor inspectorAsEditor = (Editor) inspector;
					GameObject inspectorGameObject = ((Terrain) inspectorAsEditor.target).gameObject;

					if (Selection.gameObjects.Length <= 1 && Selection.activeGameObject == inspectorGameObject)
					{
						unityTerrainSelectedTool.SetValue(inspector, GetDefaultTerrainTool(), null);
					}
				}
			}
		}

		public void ShowRefreshPrototypes()
		{
			if (GUILayout.Button("Refresh"))
			{
				if (_targetTerrain != null && _targetTerrain.terrainData != null)
				{
					_targetTerrain.terrainData.RefreshPrototypes();
				}
			}
		}

		#endregion Additional GUI functionality

		private Terrain GetTargetTerrainForGui()
		{
			Terrain selectedTerrain = UpdateTargetTerrainFromSelection();
			if (selectedTerrain != null)
			{
				return selectedTerrain;
			}

			return _targetTerrain;
		}

		private Terrain UpdateTargetTerrainFromSelection()
		{
			Terrain terrain = null;
			if (Selection.activeGameObject != null)
			{
				terrain = Selection.activeGameObject.GetComponent<Terrain>();
			}

			if (terrain != null)
			{
				_targetTerrain = terrain;
			}

			return terrain;
		}

		private void EnsureFilterSettings()
		{
			if (_noise == null)
			{
				_noise = new Noise();
			}

			if (_noise.PreviewTexture == null)
			{
				_noise.UpdatePreviewTexture();
			}

			if (_applicationCurve == null || _applicationCurve.length <= 0)
			{
				_applicationCurve = new AnimationCurve(new Keyframe(0, 0f, 0, 0), new Keyframe(1, 1, 2f, 1));
			}
		}
	}

	public class BetterTerrainPaintContext
	{
		static Terrain[] s_Nbrs = new Terrain[8];
		static Vector2[] s_Uvs = new Vector2[8];
		Terrain[] m_Terrains = new Terrain[4];
		Vector2[] m_Uvs = new Vector2[4];

		public Terrain[] terrains
		{
			get => m_Terrains;
			set => m_Terrains = value;
		}

		public Vector2[] uvs
		{
			get => m_Uvs;
			set => m_Uvs = value;
		}

		private BetterTerrainPaintContext()
		{
		}

		public static BetterTerrainPaintContext Create(Terrain terrain, Vector2 uv)
		{
			s_Nbrs[0] = terrain.leftNeighbor;
			s_Nbrs[1] = terrain.leftNeighbor ? terrain.leftNeighbor.topNeighbor : (terrain.topNeighbor ? terrain.topNeighbor.leftNeighbor : null);
			s_Nbrs[2] = terrain.topNeighbor;
			s_Nbrs[3] = terrain.rightNeighbor ? terrain.rightNeighbor.topNeighbor : (terrain.topNeighbor ? terrain.topNeighbor.rightNeighbor : null);
			s_Nbrs[4] = terrain.rightNeighbor;
			s_Nbrs[5] = terrain.rightNeighbor ? terrain.rightNeighbor.bottomNeighbor : (terrain.bottomNeighbor ? terrain.bottomNeighbor.rightNeighbor : null);
			s_Nbrs[6] = terrain.bottomNeighbor;
			s_Nbrs[7] = terrain.leftNeighbor ? terrain.leftNeighbor.bottomNeighbor : (terrain.bottomNeighbor ? terrain.bottomNeighbor.leftNeighbor : null);

			s_Uvs[0] = new Vector2(uv.x + 1.0f, uv.y);
			s_Uvs[1] = new Vector2(uv.x + 1.0f, uv.y - 1.0f);
			s_Uvs[2] = new Vector2(uv.x, uv.y - 1.0f);
			s_Uvs[3] = new Vector2(uv.x - 1.0f, uv.y - 1.0f);
			s_Uvs[4] = new Vector2(uv.x - 1.0f, uv.y);
			s_Uvs[5] = new Vector2(uv.x - 1.0f, uv.y + 1.0f);
			s_Uvs[6] = new Vector2(uv.x, uv.y + 1.0f);
			s_Uvs[7] = new Vector2(uv.x + 1.0f, uv.y + 1.0f);

			BetterTerrainPaintContext ctx = new BetterTerrainPaintContext();
			ctx.terrains[0] = terrain;
			ctx.uvs[0] = uv;

			bool left = uv.x < 0.5f;
			bool right = !left;
			bool bottom = uv.y < 0.5f;
			bool top = !bottom;

			int t = 0;
			if (right && top)
				t = 2;
			else if (right && bottom)
				t = 4;
			else if (left && bottom)
				t = 6;

			for (int i = 1; i < 4; ++i, t = (t + 1) % 8)
			{
				ctx.terrains[i] = s_Nbrs[t];
				ctx.uvs[i] = s_Uvs[t];
			}

			return ctx;
		}
	}

	public class TextureScaler
	{
		/// <summary>
		/// Scales the texture data of the given texture.
		/// </summary>
		/// <param name="tex">Texure to scale</param>
		/// <param name="width">New width</param>
		/// <param name="height">New height</param>
		/// <param name="mode">Filtering mode</param>
		public static void Rescale(Texture2D tex, int width, int height, FilterMode mode = FilterMode.Trilinear)
		{
			Rect texR = new Rect(0, 0, width, height);
			_gpu_scale(tex, width, height, mode);

			// Update new texture
			tex.Reinitialize(width, height);
			tex.ReadPixels(texR, 0, 0, true);
			tex.Apply(true); //Remove this if you hate us applying textures for you :)
		}

		// Internal unility that renders the source texture into the RTT - the scaling method itself.
		static void _gpu_scale(Texture2D src, int width, int height, FilterMode fmode)
		{
			//We need the source texture in VRAM because we render with it
			src.filterMode = fmode;
			src.Apply(true);

			//Using RTT for best quality and performance. Thanks, Unity 5
			RenderTexture rtt = new RenderTexture(width, height, 32);

			//Set the RTT in order to render to it
			Graphics.SetRenderTarget(rtt);

			//Setup 2D matrix in range 0..1, so nobody needs to care about sized
			GL.LoadPixelMatrix(0, 1, 1, 0);

			//Then clear & draw the texture to fill the entire RTT.
			GL.Clear(true, true, new Color(0, 0, 0, 0));
			Graphics.DrawTexture(new Rect(0, 0, 1, 1), src);
		}
	}
}
