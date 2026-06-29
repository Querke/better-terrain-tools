namespace Superleap.Editor
{
	using System;
	using System.Collections.Generic;
	using System.Linq;
	using BetterTerrainTools;
	using UnityEditor;
	using UnityEditor.TerrainTools;
	using UnityEngine;
	using Random = UnityEngine.Random;

	public class PaintDetailsTool : BaseBetterTerrainFilteredTool<PaintDetailsTool>
	{
        		public override string OnIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintDetails_On.png";
		public override string OffIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintDetails_On.png";
		public override int IconIndex => 4;
        
		[SerializeField] private List<int> _selectedDetails = new List<int>();

		[SerializeField] private float _remapMin;

		[SerializeField] private float _remapMax = 0.2f;

		private List<DetailPrototype> _lastSelectedDetailPrototypes = new List<DetailPrototype>();
		private GUIContent[] _detailContents;
		public const int INVALID_DETAIL = -1;

		protected const int DENSITY = 4;

		// private float[] _previousDetailDensity;
		// private float[] _previousDetailDistance;
		private Terrain[] _terrains;

		public override string GetToolName()
		{
			return "Paint grass details";
		}

		public override string GetToolDesc()
		{
			return "Paints the selected detail prototype onto the terrain\n\n" +
			       "Click to paint details\n" +
			       "Hold shift or ctrl + click to erase details\n" +
			       "Selected details filter erasing";
		}

		public override void OnEnterToolMode()
		{
			base.OnEnterToolMode();

			// _terrains = FindObjectsOfType<Terrain>();
			// if (_terrains != null)
			// {
			// 	foreach (Terrain ti in _terrains)
			// 	{
			// 		ti.editorRenderFlags = TerrainRenderFlags.All;
			// 	}
			// }
			// _previousDetailDensity = new float[_terrains.Length];
			// _previousDetailDistance = new float[_terrains.Length];
			//
			// for (int i = 0; i < _terrains.Length; i++)
			// {
			// 	_previousDetailDensity[i] = _terrains[i].detailObjectDensity;
			// 	_previousDetailDistance[i] = _terrains[i].detailObjectDistance;
			// 	_terrains[i].detailObjectDensity = 1;
			// 	_terrains[i].detailObjectDistance = 250;
			// }

			if (_selectedDetails == null)
			{
				_selectedDetails = new List<int>();
			}

			if (_targetTerrain != null &&
			    _targetTerrain.terrainData != null &&
			    _lastSelectedDetailPrototypes != null &&
			    _lastSelectedDetailPrototypes.Count > 0)
			{
				_selectedDetails.Clear();
				for (int p = 0; p < _lastSelectedDetailPrototypes.Count; ++p)
				{
					for (int i = 0; i < _targetTerrain.terrainData.detailPrototypes.Length; ++i)
					{
						if (_lastSelectedDetailPrototypes[p].Equals(_targetTerrain.terrainData.detailPrototypes[i]) &&
						    !_selectedDetails.Contains(i))
						{
							_selectedDetails.Add(i);
							break;
						}
					}
				}
			}

			_lastSelectedDetailPrototypes = new List<DetailPrototype>();

			if (_targetTerrain == null ||
			    _targetTerrain.terrainData == null ||
			    _selectedDetails.Any(x => x <= INVALID_DETAIL || x >= _targetTerrain.terrainData.detailPrototypes.Length))
			{
				_selectedDetails.RemoveAll(x => x <= INVALID_DETAIL ||
				                                _targetTerrain == null ||
				                                _targetTerrain.terrainData == null ||
				                                x >= _targetTerrain.terrainData.detailPrototypes.Length);
			}
		}

		public override void OnExitToolMode()
		{
			base.OnExitToolMode();

			// if (_terrains != null && _terrains.Length > 0 && _previousDetailDensity.Length == _terrains.Length && _previousDetailDistance.Length == _terrains.Length)
			// {
			// 	_previousDetailDensity = new float[_terrains.Length];
			// 	_previousDetailDistance = new float[_terrains.Length];
			//
			// 	for (int i = 0; i < _terrains.Length; i++)
			// 	{
			// 		_terrains[i].detailObjectDensity = _previousDetailDensity[i];
			// 		_terrains[i].detailObjectDistance = _previousDetailDistance[i];
			// 	}
			//
			// 	_previousDetailDensity = Array.Empty<float>();
			// 	_previousDetailDistance = Array.Empty<float>();
			// }

			if (_targetTerrain != null &&
			    _targetTerrain.terrainData != null &&
			    _selectedDetails != null)
			{
				_lastSelectedDetailPrototypes = new List<DetailPrototype>();
				for (int i = 0; i < _selectedDetails.Count; ++i)
				{
					if (_selectedDetails[i] > INVALID_DETAIL &&
					    _selectedDetails[i] < _targetTerrain.terrainData.detailPrototypes.Length)
					{
						_lastSelectedDetailPrototypes.Add(new DetailPrototype(_targetTerrain.terrainData.detailPrototypes[_selectedDetails[i]]));
					}
				}
			}
		}

		public override bool OnPaint(Terrain terrain, IOnPaint editContext)
		{
			if (!base.OnPaint(terrain, editContext))
			{
				return false;
			}

			bool isErasing = Event.current.shift || Event.current.control;
			bool hasSelectedDetails = _selectedDetails.Any(x => x > INVALID_DETAIL &&
			                                                    x < _targetTerrain.terrainData.detailPrototypes.Length);
			if (!isErasing &&
			    !hasSelectedDetails)
			{
				return false;
			}

			Vector2 brushUV = GetBrushUV();
			if (brushUV.x < 0 || brushUV.x > 1 || brushUV.y < 0 || brushUV.y > 1)
			{
				return false;
			}

			BetterTerrainPaintContext ctx = BetterTerrainPaintContext.Create(terrain, brushUV);

			//PaintContext ctx = PaintContext.CreateFromBounds(terrain, editContext.uv);

			for (int t = 0; t < ctx.terrains.Length; ++t)
			{
				Terrain ctxTerrain = ctx.terrains[t];
				if (ctxTerrain != null)
				{
					TerrainData terrainData = ctxTerrain.terrainData;

					//TerrainPaintUtilityEditor.UpdateTerrainDataUndo(terrainData, "Terrain - Detail Edit");
					UpdateTerrainDataUndo(terrainData, "Terrain - Detail Edit");

					// Brush radius in detail-map cells.
					float radiusPixels = Mathf.Max(0.5f, _brushSize * ((float) terrainData.detailResolution / terrainData.size.x) * 0.5f);
					float innerRadiusPixels = radiusPixels * (1.0f - _brushFalloff);

					Vector2 ctxUV = ctx.uvs[t];

					// True sub-pixel brush center in detail-map space. Keeping the fractional
					// part here is what keeps the painted region centered on the cursor for
					// every brush size. Snapping it to an int (FloorToInt) offsets the brush
					// by up to a full cell, which dominates small brushes and shifts direction
					// with the cursor's sub-pixel position.
					float centerX = ctxUV.x * terrainData.detailWidth;
					float centerY = ctxUV.y * terrainData.detailHeight;

					int xmin = Mathf.FloorToInt(centerX - radiusPixels);
					int ymin = Mathf.FloorToInt(centerY - radiusPixels);

					int xmax = Mathf.CeilToInt(centerX + radiusPixels);
					int ymax = Mathf.CeilToInt(centerY + radiusPixels);

					if (xmin >= terrainData.detailWidth || ymin >= terrainData.detailHeight || xmax <= 0 || ymax <= 0)
					{
						continue;
					}

					xmin = Mathf.Clamp(xmin, 0, terrainData.detailWidth - 1);
					ymin = Mathf.Clamp(ymin, 0, terrainData.detailHeight - 1);

					xmax = Mathf.Clamp(xmax, 0, terrainData.detailWidth);
					ymax = Mathf.Clamp(ymax, 0, terrainData.detailHeight);

					int width = xmax - xmin;
					int height = ymax - ymin;

					List<int> layers = new List<int>();
					if (isErasing && !hasSelectedDetails)
					{
						layers.AddRange(terrainData.GetSupportedLayers(xmin, ymin, width, height));
					}
					else
					{
						for (int i = 0; i < _selectedDetails.Count; i++)
						{
							if (_selectedDetails[i] <= INVALID_DETAIL ||
							    _selectedDetails[i] >= _targetTerrain.terrainData.detailPrototypes.Length)
							{
								continue;
							}

							int detailPrototype = PaintDetailsUtils.FindDetailPrototype(ctxTerrain, _targetTerrain, _selectedDetails[i]);
							if (detailPrototype == INVALID_DETAIL && !isErasing)
							{
								detailPrototype = PaintDetailsUtils.CopyDetailPrototype(ctxTerrain, _targetTerrain, _selectedDetails[i]);
							}

							if (detailPrototype != INVALID_DETAIL &&
							    !layers.Contains(detailPrototype))
							{
								layers.Add(detailPrototype);
							}
						}
					}

					if (layers.Count == 0)
					{
						continue;
					}

					List<int[,]> alphamaps = new List<int[,]>();
					for (int i = 0; i < layers.Count; i++)
					{
						alphamaps.Add(terrainData.GetDetailLayer(xmin, ymin, width, height, layers[i]));
					}

					if (_brushFalloffCurve == null || _brushFalloffCurve.length <= 0)
					{
						_brushFalloffCurve = new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 0f));
					}

					for (int y = 0; y < height; y++)
					{
						for (int x = 0; x < width; x++)
						{
							int detailX = xmin + x;
							int detailY = ymin + y;

							// Distance from this cell's center to the true (sub-pixel) cursor center.
							float dx = (detailX + 0.5f) - centerX;
							float dy = (detailY + 0.5f) - centerY;
							float dist = Mathf.Sqrt(dx * dx + dy * dy);

							if (dist >= radiusPixels)
							{
								continue;
							}

							// Erasing ignores the brush falloff so the whole radius is cleared
							// evenly at brush strength; painting uses the falloff curve.
							float opa = _brushOpacity;
							if (!isErasing && dist > innerRadiusPixels)
							{
								float falloffT = (dist - innerRadiusPixels) / (radiusPixels - innerRadiusPixels);
								opa *= Mathf.Clamp01(_brushFalloffCurve.Evaluate(falloffT));
							}

							float normalizedX = (float) detailX / terrainData.detailWidth;
							float normalizedY = (float) detailY / terrainData.detailHeight;

							if (!isErasing)
							{
								float sample = GetFilteredSample(terrainData, normalizedX, normalizedY);
								if (sample <= 0)
								{
									continue;
								}

								opa *= sample.Remap(0, 1, _remapMin, _remapMax);
							}

							for (int i = 0; i < alphamaps.Count; i++)
							{
								float targetValue = isErasing ? 0 : terrainData.detailPrototypes[layers[i]].targetCoverage * terrainData.maxDetailScatterPerRes;
								float paintedValue = Mathf.Lerp(alphamaps[i][y, x], targetValue, opa);
								alphamaps[i][y, x] = Mathf.Clamp(Mathf.RoundToInt(paintedValue), 0, terrainData.maxDetailScatterPerRes);
							}
						}
					}

					for (int i = 0; i < layers.Count; i++)
					{
						terrainData.SetDetailLayer(xmin, ymin, layers[i], alphamaps[i]);
					}
				}
			}

			return false;
		}

		private void ClearLayersOnThisTerrain(TerrainData data, int layer)
		{
			int[,] map = data.GetDetailLayer(0, 0, data.detailWidth, data.detailHeight, layer);

			// For each pixel in the detail map...
			for (int y = 0; y < data.detailHeight; y++)
			{
				for (int x = 0; x < data.detailWidth; x++)
				{
					map[y, x] = 0;
				}
			}

			Undo.RegisterCompleteObjectUndo(data, "Cleared grass details");
			data.SetDetailLayer(0, 0, layer, map);
		}

		#region GUI functions

		protected override float GetMaxBrushSize()
		{
			return 500;
		}

		protected override void OnToolSpecificGUI(Terrain terrain)
		{
			if (IsDrawingOverlayGui)
			{
				DrawCompactOverlayGui();
				return;
			}

			EditorGUILayout.LabelField("Grass details", EditorStyles.boldLabel);
			EditorGUILayout.BeginVertical("GroupBox");
			LoadDetailIcons();

			EditorGUILayout.HelpBox("Select multiple grass details with Shift.", MessageType.Info);
			_selectedDetails = PaintToolsGuiUtility.AspectSelectionGridImageAndText(_selectedDetails, _detailContents, 64, "GridListText", "No Detail Objects defined");

			EditorGUILayout.EndVertical();

			GUILayout.BeginHorizontal();
			EditorGUILayout.HelpBox("Prototype objects can only be added/removed/updated in the default terrain tool   ", MessageType.Info);
			GUILayout.FlexibleSpace();
			ShowButtonAddPrototype();
			ShowRefreshPrototypes();
			GUILayout.EndHorizontal();

			if (GUILayout.Button("Clear grass on this terrain"))
			{
				if (_selectedDetails.Any())
				{
					for (int i = 0; i < _selectedDetails.Count; i++)
					{
						if (_selectedDetails[i] > INVALID_DETAIL &&
						    _selectedDetails[i] < terrain.terrainData.detailPrototypes.Length)
						{
							ClearLayersOnThisTerrain(terrain.terrainData, _selectedDetails[i]);
						}
					}
				}
				else
				{
					for (int i = 0; i < terrain.terrainData.detailPrototypes.Length; i++)
					{
						ClearLayersOnThisTerrain(terrain.terrainData, i);
					}
				}
			}

			GUILayout.Label("Grass specific", EditorStyles.boldLabel);
			EditorGUILayout.BeginHorizontal();

			GUI.changed = false;
			_remapMin = EditorGUILayout.FloatField("Remap Strength", _remapMin);
			GUILayout.Space(4);
			EditorGUILayout.MinMaxSlider(ref _remapMin, ref _remapMax, 0, 1);
			GUILayout.Space(4);
			_remapMax = EditorGUILayout.FloatField(_remapMax, GUILayout.MaxWidth(EditorGUIUtility.fieldWidth));
			EditorGUILayout.EndHorizontal();
		}

		private void DrawCompactOverlayGui()
		{
			LoadDetailIcons();
			_selectedDetails = PaintToolsGuiUtility.AspectSelectionGridImageAndText(_selectedDetails, _detailContents, 192, "GridListText", "No Detail Objects defined");

			EditorGUILayout.BeginHorizontal();
			_remapMin = EditorGUILayout.FloatField("Remap Strength", _remapMin);
			GUILayout.Space(4);
			EditorGUILayout.MinMaxSlider(ref _remapMin, ref _remapMax, 0, 1);
			GUILayout.Space(4);
			_remapMax = EditorGUILayout.FloatField(_remapMax, GUILayout.MaxWidth(EditorGUIUtility.fieldWidth));
			EditorGUILayout.EndHorizontal();
		}

		protected override int GetDefaultTerrainTool()
		{
			return 3; // tool number 3 is the default grass tool in unity
		}

		void LoadDetailIcons()
		{
			// Locate the proto types asset preview textures
			DetailPrototype[] prototypes = _targetTerrain.terrainData.detailPrototypes;
			_detailContents = new GUIContent[prototypes.Length];
			for (int i = 0; i < _detailContents.Length; i++)
			{
				_detailContents[i] = new GUIContent();

				if (prototypes[i].usePrototypeMesh)
				{
					Texture tex = AssetPreview.GetAssetPreview(prototypes[i].prototype);
					if (tex != null)
						_detailContents[i].image = tex;

					if (prototypes[i].prototype != null)
						_detailContents[i].text = prototypes[i].prototype.name;
					else
						_detailContents[i].text = "Missing";
				}
				else
				{
					Texture tex = prototypes[i].prototypeTexture;
					if (tex != null)
						_detailContents[i].image = tex;
					if (tex != null)
						_detailContents[i].text = tex.name;
					else
						_detailContents[i].text = "Missing";
				}
			}
		}

		#endregion GUI functions
	}

	internal class PaintDetailsUtils
	{
		public static int FindDetailPrototype(Terrain terrain, Terrain sourceTerrain, int sourceDetail)
		{
			if (sourceDetail == -1 ||
			    sourceDetail >= sourceTerrain.terrainData.detailPrototypes.Length)
			{
				return -1;
			}

			if (terrain == sourceTerrain)
			{
				return sourceDetail;
			}

			DetailPrototype sourceDetailPrototype = sourceTerrain.terrainData.detailPrototypes[sourceDetail];
			for (int i = 0; i < terrain.terrainData.detailPrototypes.Length; ++i)
			{
				if (sourceDetailPrototype.Equals(terrain.terrainData.detailPrototypes[i]))
					return i;
			}

			return -1;
		}

		public static int CopyDetailPrototype(Terrain terrain, Terrain sourceTerrain, int sourceDetail)
		{
			DetailPrototype sourceDetailPrototype = sourceTerrain.terrainData.detailPrototypes[sourceDetail];
			DetailPrototype[] newDetailPrototypesArray = new DetailPrototype[terrain.terrainData.detailPrototypes.Length + 1];
			System.Array.Copy(terrain.terrainData.detailPrototypes, newDetailPrototypesArray, terrain.terrainData.detailPrototypes.Length);
			newDetailPrototypesArray[newDetailPrototypesArray.Length - 1] = new DetailPrototype(sourceDetailPrototype);
			terrain.terrainData.detailPrototypes = newDetailPrototypesArray;
			terrain.terrainData.RefreshPrototypes();
			return newDetailPrototypesArray.Length - 1;
		}
	}
}
