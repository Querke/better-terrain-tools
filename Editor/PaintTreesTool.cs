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

	public class PaintTreesTool : BaseBetterTerrainFilteredTool<PaintTreesTool>
	{
				public override string OnIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintTrees_On.png";
		public override string OffIcon => "Packages/com.unity.terrain-tools/Editor/Icons/TerrainOverlays/PaintTrees_On.png";
		public override int IconIndex => 5;
        
		[SerializeField]
		private bool _lockWidthToHeight = true;

		[SerializeField]
		private bool _randomRotation = true;

		[SerializeField]
		private bool _allowHeightVar = true;

		[SerializeField]
		private bool _allowWidthVar = true;

		[SerializeField]
		private float _treeColorAdjustment = .4f;

		[SerializeField]
		private float _treeHeight = 1;

		[SerializeField]
		private float _treeHeightVariation = .1f;

		[SerializeField]
		private float _treeWidth = 1;

		[SerializeField]
		private float _treeWidthVariation = .1f;

		[SerializeField]
		private List<int> _selectedTrees = new List<int>();

		[SerializeField]
		private bool _guiTreeWeightFoldout;

		[SerializeField]
		private List<TreeWeight> _treeWeights = new();

		private GUIContent[] _TreeContents;
		private const float MIN_SPACING = 1.5f;
		private const float MAX_SPACING = 10;
		public const int INVALID_TREE = -1;

		private float GetBrushSize => _brushSize * 0.75f;

		public override string GetToolName()
		{
			return "Paint Trees";
		}

		public override string GetToolDesc()
		{
			return "Paints the selected tree prototype onto the terrain\n\n" +
			       "Click to paint trees\n" +
			       "Hold shift or ctrl + click to erase trees\n" +
			       "Selected trees filter erasing";
		}

		protected override void OnToolSpecificGUI(Terrain terrain)
		{
			if (IsDrawingOverlayGui)
			{
				DrawCompactOverlayGui(terrain);
				return;
			}

			LoadTreeIcons(terrain);

			// Tree picker
			GUI.changed = false;

			GUILayout.Label(Styles.trees, EditorStyles.boldLabel);
			EditorGUILayout.HelpBox("Select multiple trees with Shift.", MessageType.Info);
			_selectedTrees = PaintToolsGuiUtility.AspectSelectionGridImageAndText(_selectedTrees, _TreeContents, 64, "GridListText", "No Tree Objects defined");

			for (int i = 0; i < _selectedTrees.Count; i++)
			{
				if (_selectedTrees[i] >= _TreeContents.Length)
					_selectedTrees[i] = INVALID_TREE;
			}

			GUILayout.BeginHorizontal();
			if (_selectedTrees.Any())
			{
				if (GUILayout.Button(Styles.massPlaceTrees))
				{
					Debug.Log("Does not work yet!");
					//TerrainMenus.MassPlaceTrees();
				}
			}

			GUILayout.FlexibleSpace();

			ShowButtonAddPrototype();
			ShowRefreshPrototypes();
			GUILayout.EndHorizontal();

			EditorGUILayout.HelpBox("Prototype objects can only be added/removed/updated in the default terrain tool   ", MessageType.Info);

			GUILayout.Space(5);

			//TREE WEIGHT (spawn chance)
			_guiTreeWeightFoldout = EditorGUILayout.Foldout(_guiTreeWeightFoldout, "Tree spawn chance");
			// expand weight list if needed
			if (_selectedTrees.Count != _treeWeights.Count)
			{
				for (int i = 0; i < _treeWeights.ToList().Count; i++)
				{
					TreeWeight treeWeight = _treeWeights.ToList()[i];
					if (treeWeight == null)
					{
						_treeWeights.RemoveAt(i);
						continue;
					}

					if (treeWeight.TreeIndex == -1 || _selectedTrees.All(x => x != treeWeight.TreeIndex))
					{
						_treeWeights.Remove(treeWeight);
					}
				}

				for (int i = 0; i < _selectedTrees.Count; i++)
				{
					if (_treeWeights.All(x => x.TreeIndex != _selectedTrees[i]))
					{
						_treeWeights.Insert(i, new TreeWeight(_selectedTrees[i], 1));
					}
				}
			}

			if (_guiTreeWeightFoldout)
			{
				EditorGUILayout.BeginVertical("GroupBox");
				if (_selectedTrees.Count > 1)
				{
					GUILayout.BeginHorizontal();
					GUILayout.Space(5);
					GUILayout.BeginVertical();
					for (int i = 0; i < _treeWeights.Count; i++)
					{
						if (i >= _selectedTrees.Count)
						{
							break;
						}

						string s = $"Missing tree prefab on selected index {_treeWeights[i].TreeIndex}";
						if (terrain.terrainData.treePrototypes.Length > i && terrain.terrainData.treePrototypes[_treeWeights[i].TreeIndex].prefab != null)
						{
							s = $"ID_{_treeWeights[i].TreeIndex} - {terrain.terrainData.treePrototypes[_treeWeights[i].TreeIndex].prefab.name}";
						}

						GUILayout.BeginHorizontal();
						float multipliedWeight = _treeWeights[i].Weight * 100;
						multipliedWeight = EditorGUILayout.Slider(s, multipliedWeight, 0, 100);
						GUILayout.Label("%", GUILayout.Width(20));
						_treeWeights[i].Weight = multipliedWeight / 100;
						GUILayout.EndHorizontal();
					}

					GUILayout.EndVertical();
					GUILayout.EndHorizontal();
				}
				else if (_selectedTrees.Count == 1)
				{
					GUILayout.Label("Select more than two trees to modify spawn chance");
				}
				else
				{
					GUILayout.Label("No trees selected");
				}

				EditorGUILayout.EndVertical();
			}

			GUILayout.Space(5);

			GUILayout.Label("Tree Settings", EditorStyles.boldLabel);

			GUILayout.BeginHorizontal();
			GUILayout.Label(Styles.treeHeight, GUILayout.Width(EditorGUIUtility.labelWidth - 6));
			GUILayout.Label(Styles.treeHeightRandomLabel, GUILayout.ExpandWidth(false));
			_allowHeightVar = GUILayout.Toggle(_allowHeightVar, Styles.treeHeightRandomToggle, GUILayout.ExpandWidth(false));
			if (_allowHeightVar)
			{
				EditorGUI.BeginChangeCheck();
				float min = _treeHeight * (1.0f - _treeHeightVariation);
				float max = _treeHeight * (1.0f + _treeHeightVariation);
				EditorGUILayout.MinMaxSlider(ref min, ref max, 0.01f, 4.0f);
				if (EditorGUI.EndChangeCheck())
				{
					_treeHeight = (min + max) * 0.5f;
					_treeHeightVariation = (max - min) / (min + max);
				}
			}
			else
			{
				_treeHeight = EditorGUILayout.Slider(_treeHeight, 0.01f, 2.0f);
				_treeHeightVariation = 0.0f;
			}

			GUILayout.EndHorizontal();

			GUILayout.Space(5);

			_lockWidthToHeight = EditorGUILayout.Toggle(Styles.lockWidthToHeight, _lockWidthToHeight);

			GUILayout.Space(5);

			using (new EditorGUI.DisabledScope(_lockWidthToHeight))
			{
				GUILayout.BeginHorizontal();
				GUILayout.Label(Styles.treeWidth, GUILayout.Width(EditorGUIUtility.labelWidth - 6));
				GUILayout.Label(Styles.treeWidthRandomLabel, GUILayout.ExpandWidth(false));
				_allowWidthVar = GUILayout.Toggle(_allowWidthVar, Styles.treeWidthRandomToggle, GUILayout.ExpandWidth(false));
				if (_allowWidthVar)
				{
					EditorGUI.BeginChangeCheck();
					float min = _treeWidth * (1.0f - _treeWidthVariation);
					float max = _treeWidth * (1.0f + _treeWidthVariation);
					EditorGUILayout.MinMaxSlider(ref min, ref max, 0.01f, 2.0f);
					if (EditorGUI.EndChangeCheck())
					{
						_treeWidth = (min + max) * 0.5f;
						_treeWidthVariation = (max - min) / (min + max);
					}
				}
				else
				{
					_treeWidth = EditorGUILayout.Slider(_treeWidth, 0.01f, 2.0f);
					_treeWidthVariation = 0.0f;
				}

				GUILayout.EndHorizontal();
			}

			if (!_selectedTrees.Any())
				return;

			GUILayout.Space(5);

			_randomRotation = EditorGUILayout.Toggle(Styles.treeRotation, _randomRotation);

			// TODO: we should check if the shaders assigned to this 'tree' support _TreeInstanceColor or not..  complicated check though
			_treeColorAdjustment = EditorGUILayout.Slider(Styles.treeColorVar, _treeColorAdjustment, 0, 1);
		}

		private void DrawCompactOverlayGui(Terrain terrain)
		{
			LoadTreeIcons(terrain);

			GUI.changed = false;
			_selectedTrees = PaintToolsGuiUtility.AspectSelectionGridImageAndText(_selectedTrees, _TreeContents, 192, "GridListText", "No Tree Objects defined");

			for (int i = 0; i < _selectedTrees.Count; i++)
			{
				if (_selectedTrees[i] >= _TreeContents.Length)
					_selectedTrees[i] = INVALID_TREE;
			}

			_treeHeight = EditorGUILayout.Slider(Styles.treeHeight, _treeHeight, 0.01f, 2.0f);
			_lockWidthToHeight = EditorGUILayout.Toggle(Styles.lockWidthToHeight, _lockWidthToHeight);

			if (!_lockWidthToHeight)
			{
				_treeWidth = EditorGUILayout.Slider(Styles.treeWidth, _treeWidth, 0.01f, 2.0f);
			}
		}

		protected override int GetDefaultTerrainTool()
		{
			return 2;
		}

		public override bool OnPaint(Terrain terrain, IOnPaint editContext)
		{
			if (!base.OnPaint(terrain, editContext))
			{
				return false;
			}

			if (!Event.current.shift && !Event.current.control)
			{
				if (_selectedTrees.Any(x => x > PaintTreesTool.INVALID_TREE))
				{
					PlaceTrees(terrain, editContext);
				}
			}
			else
			{
				RemoveTrees(terrain, editContext);
			}

			return false;
		}

		void LoadTreeIcons(Terrain terrain)
		{
			// Locate the proto types asset preview textures
			TreePrototype[] trees = terrain.terrainData.treePrototypes;

			_TreeContents = new GUIContent[trees.Length];
			for (int i = 0; i < _TreeContents.Length; i++)
			{
				_TreeContents[i] = new GUIContent();
				Texture tex = AssetPreview.GetAssetPreview(trees[i].prefab);
				_TreeContents[i].image = tex != null ? tex : null;
				_TreeContents[i].text = _TreeContents[i].tooltip = trees[i].prefab != null ? trees[i].prefab.name : "Missing";
			}
		}

		private Color GetTreeColor()
		{
			Color c = Color.white * Random.Range(1.0F, 1.0F - _treeColorAdjustment);
			c.a = 1;
			return c;
		}

		private float GetTreeHeight()
		{
			float v = _allowHeightVar ? _treeHeightVariation : 0.0f;
			return _treeHeight * Random.Range(1.0F - v, 1.0F + v);
		}

		private float GetRandomizedTreeWidth()
		{
			float v = _allowWidthVar ? _treeWidthVariation : 0.0f;
			return _treeWidth * Random.Range(1.0F - v, 1.0F + v);
		}

		private float GetTreeWidth(float height)
		{
			float width;

			if (_lockWidthToHeight)
			{
				// keep scales equal since these scales are applied to the
				// prefab scale to get the final tree instance scale
				width = height;
			}
			else
			{
				width = GetRandomizedTreeWidth();
			}

			return width;
		}

		private float GetTreeRotation()
		{
			return _randomRotation ? Random.Range(0, 2 * Mathf.PI) : 0;
		}

		private Vector3 lastPlacedTree;

		protected override float GetMaxBrushSize()
		{
			return 500;
		}

		private int GetRandomTreePrototype()
		{
			if (_selectedTrees.Count == 0)
			{
				return -1;
			}

			if (_selectedTrees.Count == 1)
			{
				return _selectedTrees[0];
			}

			float probabilityTotal = 0;

			// do for loop instead of _selectedTreeWeight.Sum(), as the weight list can be bigger than the actual selected tree list
			// also filter out invalid trees
			for (int i = 0; i < _selectedTrees.Count; i++)
			{
				if (_selectedTrees[i] <= INVALID_TREE)
				{
					continue;
				}

				probabilityTotal += _treeWeights[i].Weight;
			}

			float randomTest = Random.Range(0, probabilityTotal);
			int selected = 0;
			float accumulatedProbability = 0;
			for (int i = 0; i < _selectedTrees.Count; i++)
			{
				if (_treeWeights.Count <= i)
				{
					continue;
				}

				accumulatedProbability += _treeWeights[i].Weight;
				if (randomTest <= accumulatedProbability)
				{
					selected = _selectedTrees[i];
					break;
				}
			}

			return selected;
		}

		private void PlaceTrees(Terrain terrain, IOnPaint editContext)
		{
			if (_targetTerrain == null)
			{
				return;
			}

			Vector2 brushUV = GetBrushUV();
			if (brushUV.x < 0 || brushUV.x > 1 || brushUV.y < 0 || brushUV.y > 1)
			{
				return;
			}

			BetterTerrainPaintContext ctx = BetterTerrainPaintContext.Create(terrain, brushUV);

			Vector3 position = new Vector3(brushUV.x, 0, brushUV.y);
			float spacing = Mathf.Lerp(MAX_SPACING, MIN_SPACING, Mathf.Clamp01(_brushOpacity));

			int treePrototype = PaintTreesUtils.FindTreePrototype(terrain, _targetTerrain, GetRandomTreePrototype());

			int treesAdded = 0;
			int treesAddedDebug = 0;
			int treesToAdd = Mathf.RoundToInt((_brushSize / spacing) / 4f);
			treesToAdd = Mathf.Min(treesToAdd, 500);
			int treesToAddBefore = treesToAdd;
			float[,] brushMask = GenerateBrushMask(64, true);
			int brushMaskResolution = brushMask.GetLength(0);

			if (Event.current.type == EventType.MouseDown)
			{
				lastPlacedTree = Vector3.positiveInfinity;
				if ((_brushSize <= 10 || treesToAdd <= 0) && PaintTreesUtils.ValidateTreePrototype(terrain, treePrototype))
				{
					// When painting single tree, force the brush mask contribution while keeping filters active.
					// bool checkTreeDistance = Event.current.type == EventType.MouseDown || GetBrushSize > 1;
					float sample = GetFilteredSample(terrain.terrainData, position.x, position.z);
					if (sample > 0 &&
					    Random.value <= sample &&
					    CheckTreeDistance(terrain.terrainData, position, treePrototype, spacing * 2f))
					{
						UpdateTerrainDataUndo(terrain.terrainData, "Terrain - Place Trees");

						var instanceHeight = GetTreeHeight();
						PaintTreesUtils.PlaceTree(terrain, treePrototype, position, GetTreeColor(), instanceHeight, GetTreeWidth(instanceHeight), GetTreeRotation());
						treesAddedDebug++;
						treesAdded++;
					}
				}
			}

			float mouseDragDistance = Vector3.Distance(lastPlacedTree, position);
			if (mouseDragDistance <= (spacing * 2f) / terrain.terrainData.size.x)
			{
				return;
			}

			lastPlacedTree = position;

			for (int i = 0; i < ctx.terrains.Length; ++i)
			{
				Terrain ctxTerrain = ctx.terrains[i];
				if (ctxTerrain != null)
				{
					int attempts = 0;
					treesToAdd = treesToAddBefore - treesAdded;
					Vector2 ctxUV = ctx.uvs[i];

					while (treesToAdd > 0 && attempts < 500)
					{
						Vector2 randomOffset = 0.5f * Random.insideUnitCircle;
						randomOffset.x *= GetBrushSize / ctxTerrain.terrainData.size.x;
						randomOffset.y *= GetBrushSize / ctxTerrain.terrainData.size.z;
						position = new Vector3(ctxUV.x + randomOffset.x, 0, ctxUV.y + randomOffset.y);

						float brushU = 0.5f + randomOffset.x / (GetBrushSize / ctxTerrain.terrainData.size.x);
						float brushV = 0.5f + randomOffset.y / (GetBrushSize / ctxTerrain.terrainData.size.z);
						int brushX = Mathf.Clamp(Mathf.RoundToInt(brushU * (brushMaskResolution - 1)), 0, brushMaskResolution - 1);
						int brushY = Mathf.Clamp(Mathf.RoundToInt(brushV * (brushMaskResolution - 1)), 0, brushMaskResolution - 1);
						float sample = GetFilteredSample(ctxTerrain.terrainData, position.x, position.z) * brushMask[brushX, brushY];

						if (Random.value > sample)
						{
							treesToAdd--;
						}

						if (sample > 0 &&
						    Random.value <= sample &&
						    position.x >= 0 &&
						    position.x <= 1 &&
						    position.z >= 0 &&
						    position.z <= 1)
						{
							treePrototype = PaintTreesUtils.FindTreePrototype(terrain, _targetTerrain, GetRandomTreePrototype());
							if (PaintTreesUtils.ValidateTreePrototype(ctxTerrain, treePrototype))
							{
								if (CheckTreeDistance(ctxTerrain.terrainData, position, treePrototype, spacing * 2f))
								{
									UpdateTerrainDataUndo(ctxTerrain.terrainData, "Terrain - Place Trees");

									var instanceHeight = GetTreeHeight();
									PaintTreesUtils.PlaceTree(ctxTerrain, treePrototype, position, GetTreeColor(), instanceHeight, GetTreeWidth(instanceHeight), GetTreeRotation());
									treesAddedDebug++;
									treesToAdd--;
								}
							}
							else
							{
								Debug.LogWarning("could not validate tree prototype " + treePrototype);
							}
						}

						attempts++;
					}
				}
			}
		}

		private void RemoveTrees(Terrain terrain, IOnPaint editContext)
		{
			Vector2 brushUV = GetBrushUV();
			if (brushUV.x < 0 || brushUV.x > 1 || brushUV.y < 0 || brushUV.y > 1)
			{
				return;
			}

			BetterTerrainPaintContext ctx = BetterTerrainPaintContext.Create(terrain, brushUV);

			for (int i = 0; i < ctx.terrains.Length; ++i)
			{
				Terrain ctxTerrain = ctx.terrains[i];
				if (ctxTerrain != null)
				{
					Vector2 ctxUV = ctx.uvs[i];
					float radius = 0.5f * GetBrushSize / ctxTerrain.terrainData.size.x;

					if (_selectedTrees.Any(x => x > INVALID_TREE))
					{
						if (_selectedTrees.Any())
						{
							UpdateTerrainDataUndo(ctxTerrain.terrainData, "Terrain - Remove Trees");
							foreach (int selectedTree in _selectedTrees)
							{
								if (selectedTree <= INVALID_TREE)
								{
									continue;
								}

								int treePrototype = PaintTreesUtils.FindTreePrototype(ctxTerrain, _targetTerrain, selectedTree);
								if (treePrototype == INVALID_TREE)
								{
									continue;
								}

								RemoveTrees(ctxTerrain, ctxUV, radius, treePrototype);
							}
						}
					}
					else
					{
						UpdateTerrainDataUndo(ctxTerrain.terrainData, "Terrain - Remove Trees");

						RemoveTrees(ctxTerrain, ctxUV, radius, INVALID_TREE);
					}
				}
			}
		}

		public void MassPlaceTrees(TerrainData terrainData, int numberOfTrees, bool randomTreeColor, bool keepExistingTrees)
		{
			int nbPrototypes = terrainData.treePrototypes.Length;
			if (nbPrototypes == 0)
			{
				Debug.Log("Can't place trees because no prototypes are defined");
				return;
			}

			Undo.RegisterCompleteObjectUndo(terrainData, "Mass Place Trees");

			TreeInstance[] instances = new TreeInstance[numberOfTrees];
			int i = 0;
			while (i < instances.Length)
			{
				TreeInstance instance = new TreeInstance();
				instance.position = new Vector3(Random.value, 0, Random.value);
				if (terrainData.GetSteepness(instance.position.x, instance.position.z) < 30)
				{
					instance.color = randomTreeColor ? GetTreeColor() : Color.white;
					instance.lightmapColor = Color.white;
					instance.prototypeIndex = Random.Range(0, nbPrototypes);

					instance.heightScale = GetTreeHeight();
					instance.widthScale = GetTreeWidth(instance.heightScale);

					instance.rotation = GetTreeRotation();

					instances[i++] = instance;
				}
			}

			if (keepExistingTrees)
			{
				var existingTrees = terrainData.treeInstances;
				var allTrees = new TreeInstance[existingTrees.Length + instances.Length];
				System.Array.Copy(existingTrees, 0, allTrees, 0, existingTrees.Length);
				System.Array.Copy(instances, 0, allTrees, existingTrees.Length, instances.Length);
				instances = allTrees;
			}

			terrainData.SetTreeInstances(instances, true);
		}

		#region UNITY EDITOR INTERNAL CLASS REFLECTION

		private bool CheckTreeDistance(TerrainData terrainData, Vector3 position, int treePrototype, float space)
		{
			bool val = (bool) typeof(SerializedProperty).Assembly.GetType("UnityEditor.TerrainInspectorUtil", false, false)
				.GetMethod("CheckTreeDistance")
				.Invoke(null, new object[] {terrainData, position, treePrototype, space});

			return val;
		}

		private Vector3 GetPrototypeExtent(TerrainData terrainData, int treePrototype)
		{
			Vector3 val = (Vector3) typeof(SerializedProperty).Assembly.GetType("UnityEditor.TerrainInspectorUtil", false, false)
				.GetMethod("GetPrototypeExtent")
				.Invoke(null, new object[] {terrainData, treePrototype});
			return val;
		}

		private void RemoveTrees(Terrain ctxTerrain, Vector2 ctxUV, float radius, int treePrototype)
		{
			// DIDN'T WORK TO USE REFLECTION TO CALL THE BUILT IN UNITY METHOD! might try later
			// Type type = ctxTerrain.GetType();
			// MethodInfo methodInfo = type.GetMethod("RemoveTrees", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
			// methodInfo.Invoke(ctxTerrain, new object[] {ctxUV, radius, treePrototype});

			List<TreeInstance> treeInstances = ctxTerrain.terrainData.treeInstances.ToList();

			foreach (TreeInstance treeInstance in treeInstances.ToList())
			{
				Vector2 pos = new Vector2(treeInstance.position.x, treeInstance.position.z);

				// Erasing ignores the brush falloff (hard circle); brush strength controls
				// the chance of removing each tree inside the radius.
				if (IsInsideCircle(ctxUV.x, ctxUV.y, radius, pos.x, pos.y) &&
				    (treePrototype == INVALID_TREE || treeInstance.prototypeIndex == treePrototype) &&
				    Random.value <= _brushOpacity)
				{
					treeInstances.Remove(treeInstance);
				}
			}

			ctxTerrain.terrainData.treeInstances = treeInstances.ToArray();
		}

		private static bool IsInsideCircle(float circleX, float circleY, float radius, float pointX, float pointY)
		{
			return Mathf.Pow(pointX - circleX, 2) + Mathf.Pow(pointY - circleY, 2) < Mathf.Pow(radius, 2);
		}

		#endregion UNITY EDITOR INTERNAL CLASS REFLECTION

		static class Styles
		{
			// Trees
			public static readonly GUIContent trees = EditorGUIUtility.TrTextContent("Trees");
			public static readonly GUIContent treeHeight = EditorGUIUtility.TrTextContent("Tree Height", "The height scale of the planted trees");
			public static readonly GUIContent treeHeightRandomLabel = EditorGUIUtility.TrTextContent("Random?", "Enable random variation in tree height (variation)");
			public static readonly GUIContent treeHeightRandomToggle = EditorGUIUtility.TrTextContent("", "Enable random variation in tree height (variation)");

			public static readonly GUIContent lockWidthToHeight = EditorGUIUtility.TrTextContent(
				"Lock Width to Height",
				"Let the tree width scale be equal to the tree height scale"
			);

			public static readonly GUIContent treeWidth = EditorGUIUtility.TrTextContent("Tree Width", "The width scale of the planted trees");
			public static readonly GUIContent treeWidthRandomLabel = EditorGUIUtility.TrTextContent("Random?", "Enable random variation in tree width (variation)");
			public static readonly GUIContent treeWidthRandomToggle = EditorGUIUtility.TrTextContent("", "Enable random variation in tree width (variation)");

			public static readonly GUIContent treeColorVar = EditorGUIUtility.TrTextContent(
				"Color Variation",
				"Amount of random shading applied to trees. This only works if the shader supports _TreeInstanceColor (for example, Speedtree shaders do not use this)"
			);

			public static readonly GUIContent treeRotation = EditorGUIUtility.TrTextContent(
				"Random Tree Rotation",
				"Randomize tree rotation. This only works when the tree has an LOD group."
			);

			public static readonly GUIContent massPlaceTrees = EditorGUIUtility.TrTextContent(
				"Mass Place Trees",
				"The Mass Place Trees button is a very useful way to create an overall covering of trees without painting over the whole landscape. Following a mass placement, you can still use painting to add or remove trees to create denser or sparser areas."
			);
		}
	}

	internal class PaintTreesUtils
	{
		public static bool ValidateTreePrototype(Terrain terrain, int treePrototype)
		{
			int prototypeCount = terrain.terrainData.treePrototypes.Length;
			if (treePrototype == PaintTreesTool.INVALID_TREE || treePrototype >= prototypeCount)
				return false;

			if (!PrototypeIsRenderable(terrain.terrainData, treePrototype))
				return false;

			return true;
		}

		private static bool PrototypeIsRenderable(TerrainData terrainTerrainData, int treePrototype)
		{
			if (terrainTerrainData == null || treePrototype == -1)
			{
				return false;
			}

			if (terrainTerrainData.treePrototypes.Length <= treePrototype)
			{
				return false;
			}

			if (terrainTerrainData.treePrototypes[treePrototype].prefab == null)
			{
				return false;
			}

			return true;
		}

		public static int FindTreePrototype(Terrain terrain, Terrain sourceTerrain, int sourceTree)
		{
			if (sourceTree == PaintTreesTool.INVALID_TREE ||
			    sourceTree >= sourceTerrain.terrainData.treePrototypes.Length)
			{
				return PaintTreesTool.INVALID_TREE;
			}

			if (terrain == sourceTerrain)
			{
				return sourceTree;
			}

			TreePrototype sourceTreePrototype = sourceTerrain.terrainData.treePrototypes[sourceTree];
			for (int i = 0; i < terrain.terrainData.treePrototypes.Length; ++i)
			{
				if (sourceTreePrototype.Equals(terrain.terrainData.treePrototypes[i]))
					return i;
			}

			return PaintTreesTool.INVALID_TREE;
		}

		public static int CopyTreePrototype(Terrain terrain, Terrain sourceTerrain, int sourceTree)
		{
			TreePrototype sourceTreePrototype = sourceTerrain.terrainData.treePrototypes[sourceTree];
			TreePrototype[] newTreePrototypesArray = new TreePrototype[terrain.terrainData.treePrototypes.Length + 1];
			System.Array.Copy(terrain.terrainData.treePrototypes, newTreePrototypesArray, terrain.terrainData.treePrototypes.Length);
			newTreePrototypesArray[newTreePrototypesArray.Length - 1] = new TreePrototype(sourceTreePrototype);
			terrain.terrainData.treePrototypes = newTreePrototypesArray;
			return newTreePrototypesArray.Length - 1;
		}

		public static void PlaceTree(Terrain terrain, int treePrototype, Vector3 position, Color color, float height, float width, float rotation)
		{
			TreeInstance instance = new TreeInstance();
			instance.position = position;
			instance.color = color;
			instance.lightmapColor = Color.white;
			instance.prototypeIndex = treePrototype;
			instance.heightScale = height;
			instance.widthScale = width;
			instance.rotation = rotation;
			terrain.AddTreeInstance(instance);
		}
	}

	[Serializable]
	internal class TreeWeight
	{
		public int TreeIndex;
		public float Weight;

		public TreeWeight(int treeIndex, float weight)
		{
			TreeIndex = treeIndex;
			Weight = weight;
		}
	}
}
