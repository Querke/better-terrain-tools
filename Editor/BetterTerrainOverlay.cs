using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine.UIElements;

namespace BetterTerrainTools
{
	internal static class BetterTerrainOverlayIds
	{
		public const string OverlayId = "com.superleap.terrain-tools.overlay";
		public const string OverlayTitle = "Better Terrain Tools";
	}

	public interface BetterTerrainToolOverlayGui
	{
		public void DrawOverlayGui();
	}

	// Dockable SceneView overlay panel, enabled/disabled automatically by your tool.
	[Overlay(typeof(SceneView), BetterTerrainOverlayIds.OverlayId, BetterTerrainOverlayIds.OverlayTitle, false)]
	internal class BetterTerrainOverlay : Overlay
	{
		public static BetterTerrainToolOverlayGui ActiveTool;

		public override VisualElement CreatePanelContent()
		{
			VisualElement root = new VisualElement();
			root.style.paddingLeft = 8;
			root.style.paddingRight = 8;
			root.style.paddingTop = 6;
			root.style.paddingBottom = 6;

			IMGUIContainer imgui = new IMGUIContainer(() =>
				{
					BetterTerrainToolOverlayGui tool = ActiveTool;
					if (tool == null)
					{
						EditorGUILayout.HelpBox("No active TerrainFormer tool.", MessageType.Info);
						return;
					}

					try
					{
						tool.DrawOverlayGui();
					}
					catch (Exception ex)
					{
						EditorGUILayout.HelpBox(ex.ToString(), MessageType.Error);
					}
				}
			);

			root.Add(imgui);
			return root;
		}

		public static void SetDisplayed(bool displayed)
		{
			EditorApplication.delayCall += () =>
			{
				SceneView sceneView = SceneView.lastActiveSceneView;
				if (sceneView == null)
				{
					sceneView = EditorWindow.GetWindow<SceneView>();
				}

				Overlay overlay;
				bool found = sceneView.TryGetOverlay(BetterTerrainOverlayIds.OverlayId, out overlay);

				if (!found || overlay == null)
				{
					return;
				}

				overlay.displayed = displayed;
				sceneView.Repaint();
			};
		}
	}
}