using System;
using UnityEditor;
using UnityEditor.Overlays;
using UnityEngine;
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

	public interface BetterTerrainToolOverlayLayout
	{
		public float OverlayWidth { get; }
		public float OverlayHeight { get; }
	}

	// Dockable SceneView overlay panel, enabled/disabled automatically by your tool.
	[Overlay(typeof(SceneView), BetterTerrainOverlayIds.OverlayId, BetterTerrainOverlayIds.OverlayTitle, false)]
	internal class BetterTerrainOverlay : Overlay
	{
		public static BetterTerrainToolOverlayGui ActiveTool;

		// Scrolling is handled inside IMGUI rather than with a UIToolkit ScrollView: an
		// IMGUIContainer nested in a scrolled ScrollView offsets the mouse coordinates IMGUI
		// receives, which made overlay clicks land low and to the right of the cursor.
		private Vector2 _scrollPos;

		public override VisualElement CreatePanelContent()
		{
			VisualElement root = new VisualElement();
			root.style.paddingLeft = 8;
			root.style.paddingRight = 8;
			root.style.paddingTop = 6;
			root.style.paddingBottom = 6;

			IMGUIContainer imgui = new IMGUIContainer();
			imgui.onGUIHandler = () => DrawImgui(root, imgui);
			root.Add(imgui);
			return root;
		}

		private void DrawImgui(VisualElement root, IMGUIContainer imgui)
		{
			BetterTerrainToolOverlayGui tool = ActiveTool;
			bool isFixedHeight = ApplyLayout(root, imgui, tool);

			if (tool == null)
			{
				EditorGUILayout.HelpBox("No active Better Terrain tool. Select a terrain and select a custom tool.", MessageType.Info);
				return;
			}

			if (isFixedHeight)
				_scrollPos = EditorGUILayout.BeginScrollView(_scrollPos);

			try
			{
				tool.DrawOverlayGui();
			}
			catch (Exception ex)
			{
				EditorGUILayout.HelpBox(ex.ToString(), MessageType.Error);
			}

			if (isFixedHeight)
				EditorGUILayout.EndScrollView();
		}

		// Returns true when the active tool requests a fixed-height overlay (and therefore needs
		// the IMGUI scroll view). Auto-height tools fit their content and don't scroll.
		private static bool ApplyLayout(VisualElement root, IMGUIContainer imgui, BetterTerrainToolOverlayGui tool)
		{
			if (tool is BetterTerrainToolOverlayLayout layout)
			{
				root.style.width = layout.OverlayWidth;
				root.style.minWidth = layout.OverlayWidth;
				root.style.maxWidth = layout.OverlayWidth;
				root.style.height = layout.OverlayHeight;
				imgui.style.flexGrow = 1;
				return true;
			}

			root.style.width = StyleKeyword.Null;
			root.style.minWidth = StyleKeyword.Null;
			root.style.maxWidth = StyleKeyword.Null;
			root.style.height = StyleKeyword.Null;
			imgui.style.flexGrow = StyleKeyword.Null;
			return false;
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
