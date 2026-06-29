namespace Superleap.Editor
{
	using System;
	using System.Collections.Generic;
	using BetterTerrainTools;
	using UnityEditor;
	using UnityEditor.TerrainTools;
	using UnityEngine;
	using Terrain = UnityEngine.Terrain;

	public class PaintTextureTool : BaseBetterTerrainFilteredTool<PaintTextureTool>
	{
		public override string OnIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintMaterials_On.png";
		public override string OffIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintMaterials_On.png";
		public override int IconIndex => 3;

		[SerializeField] private int _selectedLayer = INVALID_LAYER;
		[SerializeField] private TerrainLayer _pickedLayer;
		[SerializeField] private bool _heightFilterEnabled;
		[SerializeField] private float _heightMin;
		[SerializeField] private float _heightMax = 100f;
		[SerializeField] private bool _heightPickMin;
		[SerializeField] private bool _heightPickMax;
		[SerializeField] private bool _cavityFilterEnabled;
		[SerializeField] private CavityMode _cavityMode;
		[SerializeField] private float _cavityFeatureSize = 1f;
		[SerializeField] private float _cavityStrength = 1f;
		[SerializeField] private AnimationCurve _cavityRemapCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));

		private const int INVALID_LAYER = -1;
		private const float MIN_ALPHA = 0.00001f;
		private const int LAYER_ICON_SIZE = 128;
		private GUIContent[] _layerContents = Array.Empty<GUIContent>();
		private int _layerPickerWindowID = -1;
		private readonly Dictionary<Texture, Texture2D> _opaqueLayerIconCache = new();
		private readonly Dictionary<TerrainData, Dictionary<int, float[]>> _strokeAlphaCache = new();
		private readonly HashSet<TerrainData> _strokeUndoTerrains = new();
		private bool _hasActiveStroke;
		private int _activeStrokeUndoGroup = -1;

		private enum CavityMode
		{
			Recessed = 0,
			Exposed = 1
		}

		public override float OverlayHeight => 340f;

		public override string GetToolName()
		{
			return "Paint Texture";
		}

		public override string GetToolDesc()
		{
			return "Paints the selected texture layer onto the terrain with procedural brush, slope, and noise filters.\n\n" +
			       "Click to paint texture\n" +
			       "Hold shift or ctrl + click to erase selected texture";
		}

		public override void OnExitToolMode()
		{
			base.OnExitToolMode();
			foreach (Texture2D icon in _opaqueLayerIconCache.Values)
			{
				if (icon != null)
				{
					UnityEngine.Object.DestroyImmediate(icon);
				}
			}

			_opaqueLayerIconCache.Clear();
		}

		public override bool OnPaint(Terrain terrain, IOnPaint editContext)
		{
			if (_heightPickMin || _heightPickMax)
			{
				return false;
			}

			if (!base.OnPaint(terrain, editContext))
			{
				return false;
			}

			ValidateSelectedLayer(_targetTerrain);
			TerrainLayer selectedTerrainLayer = GetSelectedTerrainLayer(_targetTerrain);
			if (selectedTerrainLayer == null)
			{
				return false;
			}

			Vector2 brushUV = GetBrushUV();
			if (brushUV.x < 0 || brushUV.x > 1 || brushUV.y < 0 || brushUV.y > 1)
			{
				return false;
			}

			BetterTerrainPaintContext ctx = BetterTerrainPaintContext.Create(terrain, brushUV);
			bool isErasing = Event.current.shift || Event.current.control;
			if (Event.current.type == EventType.MouseDown || !_hasActiveStroke)
			{
				_hasActiveStroke = true;
				_strokeAlphaCache.Clear();
				_strokeUndoTerrains.Clear();
				Undo.IncrementCurrentGroup();
				_activeStrokeUndoGroup = Undo.GetCurrentGroup();
				Undo.SetCurrentGroupName("Terrain - Paint Texture");
			}

			for (int t = 0; t < ctx.terrains.Length; ++t)
			{
				Terrain ctxTerrain = ctx.terrains[t];
				if (ctxTerrain == null || ctxTerrain.terrainData == null)
				{
					continue;
				}

				TerrainData terrainData = ctxTerrain.terrainData;
				int layerIndex = FindOrAddTerrainLayer(terrainData, selectedTerrainLayer);
				if (layerIndex == INVALID_LAYER)
				{
					continue;
				}

				int layerCount = terrainData.alphamapLayers;
				if (layerIndex >= layerCount)
				{
					continue;
				}

				int alphamapWidth = terrainData.alphamapWidth;
				int alphamapHeight = terrainData.alphamapHeight;
				int brushPixelsSize = Mathf.CeilToInt(Mathf.Max(
					_brushSize * ((float) alphamapWidth / terrainData.size.x),
					_brushSize * ((float) alphamapHeight / terrainData.size.z)
				));
				brushPixelsSize = Mathf.Max(1, brushPixelsSize);
				if (brushPixelsSize % 2 != 0)
				{
					brushPixelsSize++;
				}

				float[,] brushMask = GenerateBrushMask(brushPixelsSize, true);
				Vector2 ctxUV = ctx.uvs[t];

				int xCenter = Mathf.FloorToInt(ctxUV.x * alphamapWidth);
				int yCenter = Mathf.FloorToInt(ctxUV.y * alphamapHeight);

				int xBase = xCenter - (brushPixelsSize / 2);
				int yBase = yCenter - (brushPixelsSize / 2);

				int xStart = Mathf.Max(0, xBase);
				int yStart = Mathf.Max(0, yBase);
				int xEnd = Mathf.Min(alphamapWidth, xBase + brushPixelsSize);
				int yEnd = Mathf.Min(alphamapHeight, yBase + brushPixelsSize);

				int width = xEnd - xStart;
				int height = yEnd - yStart;
				if (width <= 0 || height <= 0)
				{
					continue;
				}

				RegisterTerrainTextureUndo(terrainData);
				float[,,] alphamaps = terrainData.GetAlphamaps(xStart, yStart, width, height);
				if (!_strokeAlphaCache.TryGetValue(terrainData, out Dictionary<int, float[]> strokeAlphas))
				{
					strokeAlphas = new Dictionary<int, float[]>();
					_strokeAlphaCache[terrainData] = strokeAlphas;
				}

				for (int y = 0; y < height; y++)
				{
					for (int x = 0; x < width; x++)
					{
						int globalX = xStart + x;
						int globalY = yStart + y;
						int brushX = globalX - xBase;
						int brushY = globalY - yBase;
						float brushSample = brushMask[brushX, brushY];
						if (brushSample <= MIN_ALPHA)
						{
							continue;
						}

						float normalizedX = (globalX + 0.5f) / alphamapWidth;
						float normalizedY = (globalY + 0.5f) / alphamapHeight;
						float filterSample = GetFilteredSample(terrainData, normalizedX, normalizedY);
						float strength = Mathf.Clamp01(brushSample * filterSample);
						if (strength <= MIN_ALPHA)
						{
							continue;
						}

						int pixelKey = globalY * alphamapWidth + globalX;
						if (!strokeAlphas.TryGetValue(pixelKey, out float[] strokeAlpha) || strokeAlpha.Length != layerCount)
						{
							strokeAlpha = new float[layerCount];
							for (int layer = 0; layer < layerCount; layer++)
							{
								strokeAlpha[layer] = alphamaps[y, x, layer];
							}

							strokeAlphas[pixelKey] = strokeAlpha;
						}

						float currentAlpha = alphamaps[y, x, layerIndex];
						float strokeStartAlpha = strokeAlpha[layerIndex];
						if (isErasing)
						{
							if (layerCount <= 1 || strokeStartAlpha <= MIN_ALPHA)
							{
								continue;
							}

							float newSelectedAlpha = Mathf.Lerp(strokeStartAlpha, 0f, strength);
							if (currentAlpha <= newSelectedAlpha + MIN_ALPHA)
							{
								continue;
							}

							float alphaToRedistribute = currentAlpha - newSelectedAlpha;
							float otherAlphaTotal = 0f;
							int fallbackLayerIndex = INVALID_LAYER;
							float fallbackLayerAlpha = -1f;
							for (int layer = 0; layer < layerCount; layer++)
							{
								if (layer == layerIndex)
								{
									continue;
								}

								float layerAlpha = alphamaps[y, x, layer];
								otherAlphaTotal += layerAlpha;
								if (layerAlpha > fallbackLayerAlpha)
								{
									fallbackLayerAlpha = layerAlpha;
									fallbackLayerIndex = layer;
								}
							}

							alphamaps[y, x, layerIndex] = newSelectedAlpha;
							if (otherAlphaTotal > MIN_ALPHA)
							{
								for (int layer = 0; layer < layerCount; layer++)
								{
									if (layer != layerIndex)
									{
										alphamaps[y, x, layer] += alphaToRedistribute * (alphamaps[y, x, layer] / otherAlphaTotal);
									}
								}
							}
							else if (fallbackLayerIndex != INVALID_LAYER)
							{
								alphamaps[y, x, fallbackLayerIndex] += alphaToRedistribute;
							}
						}
						else
						{
							float newSelectedAlpha = Mathf.Lerp(strokeStartAlpha, 1f, strength);
							if (currentAlpha + MIN_ALPHA >= newSelectedAlpha)
							{
								continue;
							}

							float alphaToRemove = newSelectedAlpha - currentAlpha;
							float otherAlphaTotal = 1f - currentAlpha;
							alphamaps[y, x, layerIndex] = newSelectedAlpha;
							for (int layer = 0; layer < layerCount; layer++)
							{
								if (layer == layerIndex)
								{
									continue;
								}

								if (otherAlphaTotal > MIN_ALPHA)
								{
									alphamaps[y, x, layer] = Mathf.Max(0f, alphamaps[y, x, layer] - alphaToRemove * (alphamaps[y, x, layer] / otherAlphaTotal));
								}
								else
								{
									alphamaps[y, x, layer] = 0f;
								}
							}
						}

						float alphaTotal = 0f;
						for (int layer = 0; layer < layerCount; layer++)
						{
							alphaTotal += alphamaps[y, x, layer];
						}

						if (alphaTotal > MIN_ALPHA)
						{
							for (int layer = 0; layer < layerCount; layer++)
							{
								alphamaps[y, x, layer] /= alphaTotal;
							}
						}
						else
						{
							alphamaps[y, x, layerIndex] = 1f;
						}
					}
				}

				terrainData.SetAlphamaps(xStart, yStart, alphamaps);
				EditorUtility.SetDirty(terrainData);
				ctxTerrain.Flush();
			}

			return true;
		}

		public override void OnSceneGUI(Terrain terrain, IOnSceneGUI editContext)
		{
			bool isPickingHeight = _heightPickMin || _heightPickMax;
			if (isPickingHeight && Event.current.type == EventType.Layout)
			{
				HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
			}

			base.OnSceneGUI(terrain, editContext);

			if (!isPickingHeight)
			{
				return;
			}

			Event currentEvent = Event.current;
			if (currentEvent.type == EventType.MouseDown && currentEvent.button == 0)
			{
				Vector2 uv = GetBrushUV();
				if (terrain != null &&
				    terrain.terrainData != null &&
				    uv.x >= 0f &&
				    uv.x <= 1f &&
				    uv.y >= 0f &&
				    uv.y <= 1f)
				{
					float pickedHeight = terrain.terrainData.GetInterpolatedHeight(uv.x, uv.y);
					float maxTerrainHeight = terrain.terrainData.size.y;
					if (_heightPickMin)
					{
						_heightMin = Mathf.Clamp(pickedHeight, 0f, maxTerrainHeight);
						if (_heightMax < _heightMin)
						{
							_heightMax = _heightMin;
						}
					}
					else
					{
						_heightMax = Mathf.Clamp(pickedHeight, 0f, maxTerrainHeight);
						if (_heightMin > _heightMax)
						{
							_heightMin = _heightMax;
						}
					}

					_heightPickMin = false;
					_heightPickMax = false;
					Repaint();
					SceneView.RepaintAll();
				}

				currentEvent.Use();
			}
			else if (currentEvent.type == EventType.MouseDrag || currentEvent.type == EventType.MouseUp)
			{
				currentEvent.Use();
			}
		}

		protected override void OnMouseUp(Terrain terrain)
		{
			base.OnMouseUp(terrain);
			if (_activeStrokeUndoGroup >= 0)
			{
				Undo.CollapseUndoOperations(_activeStrokeUndoGroup);
			}

			_hasActiveStroke = false;
			_activeStrokeUndoGroup = -1;
			_strokeAlphaCache.Clear();
			_strokeUndoTerrains.Clear();
		}

		protected override void OnToolSpecificGUI(Terrain terrain)
		{
			ValidateSelectedLayer(terrain);
			LoadLayerIcons(terrain);

			EditorGUILayout.LabelField("Texture Layers", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("GroupBox");

			_selectedLayer = PaintToolsGuiUtility.AspectSelectionGridImageAndText(
				_selectedLayer,
				_layerContents,
				IsDrawingOverlayGui ? 192 : 64,
				"GridListText",
				"No Terrain Layers defined"
			);
			ValidateSelectedLayer(terrain);

			DrawLayerControls(terrain);
			EditorGUILayout.EndVertical();
		}

		protected override int GetDefaultTerrainTool()
		{
			return 1;
		}

		private void LoadLayerIcons(Terrain terrain)
		{
			if (terrain == null || terrain.terrainData == null)
			{
				_layerContents = Array.Empty<GUIContent>();
				return;
			}

			TerrainLayer[] layers = terrain.terrainData.terrainLayers;
			_layerContents = new GUIContent[layers.Length];
			for (int i = 0; i < layers.Length; i++)
			{
				TerrainLayer layer = layers[i];
				GUIContent content = new GUIContent();
				if (layer == null)
				{
					content.text = "Missing";
					content.tooltip = "Missing Terrain Layer";
					_layerContents[i] = content;
					continue;
				}

				if (layer.diffuseTexture != null)
				{
					if (!_opaqueLayerIconCache.TryGetValue(layer.diffuseTexture, out Texture2D opaquePreview) || opaquePreview == null)
					{
						RenderTexture previousActive = RenderTexture.active;
						RenderTexture previewRenderTexture = RenderTexture.GetTemporary(LAYER_ICON_SIZE, LAYER_ICON_SIZE, 0, RenderTextureFormat.ARGB32);
						try
						{
							Graphics.Blit(layer.diffuseTexture, previewRenderTexture);
							RenderTexture.active = previewRenderTexture;
							opaquePreview = new Texture2D(LAYER_ICON_SIZE, LAYER_ICON_SIZE, TextureFormat.RGBA32, false);
							opaquePreview.hideFlags = HideFlags.HideAndDontSave;
							opaquePreview.name = $"{layer.diffuseTexture.name} Opaque Preview";
							opaquePreview.ReadPixels(new Rect(0, 0, LAYER_ICON_SIZE, LAYER_ICON_SIZE), 0, 0);

							Color32[] pixels = opaquePreview.GetPixels32();
							for (int pixelIndex = 0; pixelIndex < pixels.Length; pixelIndex++)
							{
								pixels[pixelIndex].a = 255;
							}

							opaquePreview.SetPixels32(pixels);
							opaquePreview.Apply(false, true);
						}
						finally
						{
							RenderTexture.active = previousActive;
							RenderTexture.ReleaseTemporary(previewRenderTexture);
						}

						_opaqueLayerIconCache[layer.diffuseTexture] = opaquePreview;
					}

					content.image = opaquePreview;
				}
				else
				{
					content.image = AssetPreview.GetAssetPreview(layer);
				}

				content.text = layer.name;
				content.tooltip = layer.name;
				_layerContents[i] = content;
			}
		}

		private void DrawLayerControls(Terrain terrain)
		{
			if (Event.current.commandName == "ObjectSelectorClosed" &&
			    EditorGUIUtility.GetObjectPickerControlID() == _layerPickerWindowID)
			{
				_pickedLayer = EditorGUIUtility.GetObjectPickerObject() as TerrainLayer;
			}

			if (_pickedLayer != null && Event.current.type == EventType.Repaint)
			{
				TerrainLayer pickedLayer = _pickedLayer;
				_pickedLayer = null;
				AddLayerToTerrain(terrain, pickedLayer);
				Repaint();
			}

			EditorGUILayout.BeginHorizontal();
			using (new EditorGUI.DisabledScope(terrain == null || terrain.terrainData == null))
			{
				if (GUILayout.Button("Add Layer"))
				{
					_layerPickerWindowID = EditorGUIUtility.GetControlID(FocusType.Passive) + 100;
					EditorGUIUtility.ShowObjectPicker<TerrainLayer>(null, false, "", _layerPickerWindowID);
				}
			}

			using (new EditorGUI.DisabledScope(terrain == null || terrain.terrainData == null || _selectedLayer == INVALID_LAYER))
			{
				if (GUILayout.Button("Remove Layer") &&
				    EditorUtility.DisplayDialog("Warning", "Splatmap data changed by this layer will be lost.", "OK", "Cancel"))
				{
					RemoveSelectedLayerFromTerrain(terrain);
					GUIUtility.ExitGUI();
				}
			}

			EditorGUILayout.EndHorizontal();
		}

		private void AddLayerToTerrain(Terrain terrain, TerrainLayer layer)
		{
			if (terrain == null || terrain.terrainData == null || layer == null)
			{
				return;
			}

			TerrainLayer[] layers = terrain.terrainData.terrainLayers;
			for (int i = 0; i < layers.Length; i++)
			{
				if (layers[i] == layer)
				{
					_selectedLayer = i;
					return;
				}
			}

			Undo.RegisterCompleteObjectUndo(terrain.terrainData, "Terrain - Add Texture Layer");
			TerrainLayer[] newLayers = new TerrainLayer[layers.Length + 1];
			Array.Copy(layers, newLayers, layers.Length);
			newLayers[newLayers.Length - 1] = layer;
			terrain.terrainData.terrainLayers = newLayers;
			_selectedLayer = newLayers.Length - 1;
			EditorUtility.SetDirty(terrain.terrainData);
		}

		private void RemoveSelectedLayerFromTerrain(Terrain terrain)
		{
			if (terrain == null || terrain.terrainData == null)
			{
				return;
			}

			TerrainData terrainData = terrain.terrainData;
			TerrainLayer[] layers = terrainData.terrainLayers;
			if (_selectedLayer < 0 || _selectedLayer >= layers.Length)
			{
				return;
			}

			int removedLayerIndex = _selectedLayer;
			RegisterTerrainTextureUndoObjects(terrainData, "Terrain - Remove Texture Layer");

			TerrainLayer[] newLayers = new TerrainLayer[layers.Length - 1];
			int newLayerIndex = 0;
			for (int oldLayerIndex = 0; oldLayerIndex < layers.Length; oldLayerIndex++)
			{
				if (oldLayerIndex == removedLayerIndex)
				{
					continue;
				}

				newLayers[newLayerIndex] = layers[oldLayerIndex];
				newLayerIndex++;
			}

			if (newLayers.Length == 0)
			{
				terrainData.terrainLayers = newLayers;
				_selectedLayer = INVALID_LAYER;
				EditorUtility.SetDirty(terrainData);
				return;
			}

			int alphamapWidth = terrainData.alphamapWidth;
			int alphamapHeight = terrainData.alphamapHeight;
			float[,,] oldAlphamaps = terrainData.GetAlphamaps(0, 0, alphamapWidth, alphamapHeight);
			int oldAlphamapLayerCount = oldAlphamaps.GetLength(2);
			float[,,] newAlphamaps = new float[alphamapHeight, alphamapWidth, newLayers.Length];

			for (int y = 0; y < alphamapHeight; y++)
			{
				for (int x = 0; x < alphamapWidth; x++)
				{
					newLayerIndex = 0;
					float alphaTotal = 0f;
					for (int oldLayerIndex = 0; oldLayerIndex < layers.Length; oldLayerIndex++)
					{
						if (oldLayerIndex == removedLayerIndex)
						{
							continue;
						}

						float alpha = oldLayerIndex < oldAlphamapLayerCount ? oldAlphamaps[y, x, oldLayerIndex] : 0f;
						newAlphamaps[y, x, newLayerIndex] = alpha;
						alphaTotal += alpha;
						newLayerIndex++;
					}

					if (alphaTotal > MIN_ALPHA)
					{
						for (int layer = 0; layer < newLayers.Length; layer++)
						{
							newAlphamaps[y, x, layer] /= alphaTotal;
						}
					}
					else
					{
						newAlphamaps[y, x, Mathf.Clamp(removedLayerIndex, 0, newLayers.Length - 1)] = 1f;
					}
				}
			}

			terrainData.terrainLayers = newLayers;
			terrainData.SetAlphamaps(0, 0, newAlphamaps);
			_selectedLayer = Mathf.Clamp(removedLayerIndex, 0, newLayers.Length - 1);
			EditorUtility.SetDirty(terrainData);
		}

		private int FindOrAddTerrainLayer(TerrainData terrainData, TerrainLayer layer)
		{
			if (terrainData == null || layer == null)
			{
				return INVALID_LAYER;
			}

			TerrainLayer[] layers = terrainData.terrainLayers;
			for (int i = 0; i < layers.Length; i++)
			{
				if (layers[i] == layer)
				{
					return i;
				}
			}

			Undo.RegisterCompleteObjectUndo(terrainData, "Terrain - Paint Texture");
			TerrainLayer[] newLayers = new TerrainLayer[layers.Length + 1];
			Array.Copy(layers, newLayers, layers.Length);
			newLayers[newLayers.Length - 1] = layer;
			terrainData.terrainLayers = newLayers;
			EditorUtility.SetDirty(terrainData);
			return newLayers.Length - 1;
		}

		private void RegisterTerrainTextureUndo(TerrainData terrainData)
		{
			if (_strokeUndoTerrains.Contains(terrainData))
			{
				return;
			}

			RegisterTerrainTextureUndoObjects(terrainData, "Terrain - Paint Texture");
			_strokeUndoTerrains.Add(terrainData);
		}

		private void RegisterTerrainTextureUndoObjects(TerrainData terrainData, string undoName)
		{
			List<UnityEngine.Object> undoObjects = new List<UnityEngine.Object> {terrainData};
			undoObjects.AddRange(terrainData.alphamapTextures);
			Undo.RegisterCompleteObjectUndo(undoObjects.ToArray(), undoName);
		}

		private TerrainLayer GetSelectedTerrainLayer(Terrain terrain)
		{
			if (terrain == null || terrain.terrainData == null)
			{
				return null;
			}

			TerrainLayer[] layers = terrain.terrainData.terrainLayers;
			if (_selectedLayer < 0 || _selectedLayer >= layers.Length)
			{
				return null;
			}

			return layers[_selectedLayer];
		}

		private void ValidateSelectedLayer(Terrain terrain)
		{
			if (terrain == null || terrain.terrainData == null || terrain.terrainData.terrainLayers.Length == 0)
			{
				_selectedLayer = INVALID_LAYER;
				return;
			}

			if (_selectedLayer < 0 || _selectedLayer >= terrain.terrainData.terrainLayers.Length)
			{
				_selectedLayer = 0;
			}
		}

		protected override bool DrawAdditionalFilterSettings(Terrain terrain)
		{
			EditorGUILayout.Space();
			EditorGUILayout.LabelField("Texture Filters", EditorStyles.boldLabel);

			bool changed = false;
			EditorGUI.BeginChangeCheck();
			float maxTerrainHeight = terrain != null && terrain.terrainData != null ? terrain.terrainData.size.y : Mathf.Max(_heightMax, 1f);
			bool wasHeightFilterEnabled = _heightFilterEnabled;
			_heightFilterEnabled = EditorGUILayout.Toggle("Height", _heightFilterEnabled);
			if (_heightFilterEnabled && !wasHeightFilterEnabled)
			{
				_heightMin = 0f;
				_heightMax = maxTerrainHeight;
			}

			if (_heightFilterEnabled)
			{
				EditorGUILayout.BeginHorizontal();
				_heightMin = EditorGUILayout.FloatField("Height", _heightMin);
				GUILayout.Space(4);
				EditorGUILayout.MinMaxSlider(ref _heightMin, ref _heightMax, 0f, maxTerrainHeight);
				GUILayout.Space(4);
				_heightMax = EditorGUILayout.FloatField(_heightMax, GUILayout.MaxWidth(EditorGUIUtility.fieldWidth));
				EditorGUILayout.EndHorizontal();
				_heightMin = Mathf.Clamp(_heightMin, 0f, maxTerrainHeight);
				_heightMax = Mathf.Clamp(_heightMax, 0f, maxTerrainHeight);
				if (_heightMax < _heightMin)
				{
					_heightMax = _heightMin;
				}

				EditorGUILayout.BeginHorizontal();
				GUI.color = _heightPickMin ? Color.green : Color.white;
				if (GUILayout.Button("Set Min"))
				{
					_heightPickMin = true;
					_heightPickMax = false;
					SceneView.RepaintAll();
				}

				GUI.color = _heightPickMax ? Color.green : Color.white;
				if (GUILayout.Button("Set Max"))
				{
					_heightPickMin = false;
					_heightPickMax = true;
					SceneView.RepaintAll();
				}

				GUI.color = Color.white;
				EditorGUILayout.EndHorizontal();
			}
			else
			{
				_heightPickMin = false;
				_heightPickMax = false;
			}

			_cavityFilterEnabled = EditorGUILayout.Toggle("Cavity", _cavityFilterEnabled);
			if (_cavityFilterEnabled)
			{
				EditorGUILayout.BeginVertical("GroupBox");
				_cavityMode = (CavityMode) EditorGUILayout.EnumPopup("Mode", _cavityMode);
				_cavityStrength = EditorGUILayout.Slider("Strength", _cavityStrength, 0f, 1f);
				_cavityFeatureSize = EditorGUILayout.Slider("Feature Size", _cavityFeatureSize, 1f, 20f);
				_cavityRemapCurve = EditorGUILayout.CurveField("Remap Curve", _cavityRemapCurve);
				EditorGUILayout.EndVertical();
			}

			if (EditorGUI.EndChangeCheck())
			{
				changed = true;
			}

			return changed;
		}

		protected override bool DrawCompactAdditionalFilterSettings(Terrain terrain)
		{
			return DrawAdditionalFilterSettings(terrain);
		}

		protected override float GetAdditionalFilteredSample(TerrainData terrainData, float normalizedX, float normalizedY)
		{
			float sample = 1f;
			if (_heightFilterEnabled)
			{
				float height = terrainData.GetInterpolatedHeight(normalizedX, normalizedY);
				if (height < _heightMin || height > _heightMax)
				{
					sample = 0f;
				}
			}

			if (_cavityFilterEnabled)
			{
				int heightmapResolution = terrainData.heightmapResolution;
				float sampleOffset = Mathf.Max(1f, _cavityFeatureSize) / Mathf.Max(1f, heightmapResolution - 1f);
				float centerHeight = terrainData.GetInterpolatedHeight(normalizedX, normalizedY);
				float neighborAverage =
					terrainData.GetInterpolatedHeight(Mathf.Clamp01(normalizedX - sampleOffset), normalizedY) +
					terrainData.GetInterpolatedHeight(Mathf.Clamp01(normalizedX + sampleOffset), normalizedY) +
					terrainData.GetInterpolatedHeight(normalizedX, Mathf.Clamp01(normalizedY - sampleOffset)) +
					terrainData.GetInterpolatedHeight(normalizedX, Mathf.Clamp01(normalizedY + sampleOffset));
				neighborAverage *= 0.25f;

				float cavity = Mathf.Clamp01(Mathf.Abs(neighborAverage - centerHeight) / Mathf.Max(0.0001f, terrainData.size.y / heightmapResolution));
				if (_cavityMode == CavityMode.Recessed)
				{
					cavity = neighborAverage > centerHeight ? cavity : 0f;
				}
				else
				{
					cavity = centerHeight > neighborAverage ? cavity : 0f;
				}

				if (_cavityRemapCurve == null || _cavityRemapCurve.length <= 0)
				{
					_cavityRemapCurve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
				}

				sample *= Mathf.Lerp(1f, Mathf.Clamp01(_cavityRemapCurve.Evaluate(cavity)), _cavityStrength);
			}

			return sample;
		}
	}
}
