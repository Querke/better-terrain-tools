namespace Superleap.Editor
{
	using System.Collections.Generic;
	using System.Linq;
	using UnityEditor;
	using UnityEngine;

	public static class PaintToolsGuiUtility
	{
		public static float PowerSlider(string label, float value, float minVal, float maxVal, float power, GUILayoutOption[] options = null)
		{
			value = Mathf.Clamp(value, minVal, maxVal);
			EditorGUI.BeginChangeCheck();
			//float newValue = EditorGUILayout.PowerSlider(content, value, minVal, maxVal, power, options);
			float newValue = EditorGUILayout.Slider(label, value, minVal, maxVal, options);
			if (EditorGUI.EndChangeCheck())
			{
				return newValue;
			}

			return value;
		}

		public static List<int> AspectSelectionGridImageAndText(List<int> selected, GUIContent[] textures, int approxSize, GUIStyle style, string emptyString)
		{
			EditorGUILayout.BeginVertical(GUILayout.MinHeight(10));
			List<int> retval = new List<int>();

			if (textures.Length != 0)
			{
				Rect rect = GetBrushAspectRect(textures.Length, approxSize, 12, out int xCount);
				retval = DoButtonGrid(rect, selected, textures, xCount, style);
			}
			else
			{
				GUILayout.Label(emptyString);
			}

			GUILayout.EndVertical();
			return retval;
		}

		public static int AspectSelectionGridImageAndText(int selected, GUIContent[] textures, int approxSize, GUIStyle style, string emptyString)
		{
			EditorGUILayout.BeginVertical(GUILayout.MinHeight(10));
			int retval = selected;

			if (textures.Length != 0)
			{
				int xCount = 0;
				Rect rect = GetBrushAspectRect(textures.Length, approxSize, 12, out xCount);

				Event evt = Event.current;

				retval = DoButtonGrid(rect, selected, textures, xCount, style);
			}
			else
			{
				GUILayout.Label(emptyString);
			}

			GUILayout.EndVertical();
			return retval;
		}

		private static Rect GetBrushAspectRect(int elementCount, int approxSize, int extraLineHeight, out int xCount)
		{
			xCount = (int) Mathf.Ceil((EditorGUIUtility.currentViewWidth - 20) / approxSize);
			if (xCount <= 0)
			{
				xCount = 1;
			}

			int yCount = elementCount / xCount;
			if (elementCount % xCount != 0)
				yCount++;
			Rect r1 = GUILayoutUtility.GetAspectRect(xCount / (float) yCount);
			Rect r2 = GUILayoutUtility.GetRect(10, extraLineHeight * yCount);
			r1.height += r2.height;
			return r1;
		}

		internal static int CalcTotalHorizSpacing(int xCount, GUIStyle style, GUIStyle firstStyle, GUIStyle midStyle, GUIStyle lastStyle)
		{
			if (xCount < 2)
				return 0;
			if (xCount == 2)
				return Mathf.Max(firstStyle.margin.right, lastStyle.margin.left);

			int internalSpace = Mathf.Max(midStyle.margin.left, midStyle.margin.right);
			return Mathf.Max(firstStyle.margin.right, midStyle.margin.left) + Mathf.Max(midStyle.margin.right, lastStyle.margin.left) + internalSpace * (xCount - 3);
		}

		private static Rect[] CalcGridRects(Rect position, GUIContent[] contents, int xCount, float elemWidth, float elemHeight, GUIStyle style, GUIStyle firstStyle,
			GUIStyle midStyle, GUIStyle lastStyle, GUI.ToolbarButtonSize buttonSize)
		{
			int count = contents.Length;
			int x = 0;
			float xPos = position.xMin, yPos = position.yMin;
			GUIStyle currentStyle = style;
			Rect[] retval = new Rect[count];
			if (count > 1)
				currentStyle = firstStyle;
			for (int i = 0; i < count; i++)
			{
				float w = 0;
				switch (buttonSize)
				{
					case GUI.ToolbarButtonSize.Fixed:
						w = elemWidth;
						break;
					case GUI.ToolbarButtonSize.FitToContents:
						w = currentStyle.CalcSize(contents[i]).x;
						break;
				}

				retval[i] = new Rect(xPos, yPos, w, elemHeight);

				//we round the values to the dpi-aware pixel grid
				retval[i] = GUIUtility.AlignRectToDevice(retval[i]);

				GUIStyle nextStyle = midStyle;
				if (i == count - 2 || i == xCount - 2)
					nextStyle = lastStyle;

				xPos = retval[i].xMax + Mathf.Max(currentStyle.margin.right, nextStyle.margin.left);

				x++;
				if (x >= xCount)
				{
					x = 0;
					yPos += elemHeight + Mathf.Max(style.margin.top, style.margin.bottom);
					xPos = position.xMin;
					nextStyle = firstStyle;
				}

				currentStyle = nextStyle;
			}

			return retval;
		}

		private static readonly int s_ButtonGridHash = "ButtonGrid".GetHashCode();

		public static bool HitTest(Rect rect, Vector2 point)
		{
			return (point.x >= rect.xMin) && (point.x < rect.xMax) && (point.y >= rect.yMin) && (point.y < rect.yMax);
		}

		public static int DoButtonGrid(Rect position, int selected, GUIContent[] contents, int itemsPerRow, GUIStyle style)
		{
			GUIStyle firstStyle = style;
			GUIStyle midStyle = style;
			GUIStyle lastStyle = style;
			bool[] contentsEnabled = null;

			var buttonSize = GUI.ToolbarButtonSize.Fixed;
			int itemCount = contents.Length;
			if (itemCount == 0)
				return selected;
			if (itemsPerRow <= 0)
			{
				Debug.LogWarning(
					"You are trying to create a SelectionGrid with zero or less elements to be displayed in the horizontal direction. Set itemsPerRow to a positive value."
				);
				return selected;
			}

			bool enabled = false;
			// Figure out how large each element should be
			int rows = (itemCount + itemsPerRow - 1) / itemsPerRow;
			float elemWidth = style.fixedWidth != 0
				? style.fixedWidth
				: (position.width - CalcTotalHorizSpacing(itemsPerRow, style, firstStyle, midStyle, lastStyle)) / itemsPerRow;
			float elemHeight = style.fixedHeight != 0 ? style.fixedHeight : (position.height - Mathf.Max(style.margin.top, style.margin.bottom) * (rows - 1)) / rows;

			Rect[] buttonRects = CalcGridRects(position, contents, itemsPerRow, elemWidth, elemHeight, style, firstStyle, midStyle, lastStyle, buttonSize);
			GUIStyle selectedButtonStyle = null;
			int selectedButtonControlID = 0;
			for (int buttonIndex = 0; buttonIndex < itemCount; ++buttonIndex)
			{
				bool wasEnabled = enabled;
				enabled &= (contentsEnabled == null || contentsEnabled[buttonIndex]);
				var buttonRect = buttonRects[buttonIndex];
				var content = contents[buttonIndex];

				var id = GUIUtility.GetControlID(s_ButtonGridHash, FocusType.Passive, buttonRect);
				if (buttonIndex == selected)
					selectedButtonControlID = id;

				switch (Event.current.GetTypeForControl(id))
				{
					case EventType.MouseDown:
						if (HitTest(buttonRect, Event.current.mousePosition))
						{
							GUIUtility.hotControl = id;
							Event.current.Use();
						}

						break;
					case EventType.MouseDrag:
						if (GUIUtility.hotControl == id)
							Event.current.Use();
						break;
					case EventType.MouseUp:
						if (GUIUtility.hotControl == id)
						{
							GUIUtility.hotControl = 0;
							Event.current.Use();

							GUI.changed = true;
							return buttonIndex;
						}

						break;
					case EventType.Repaint:
						var buttonStyle = itemCount == 1 ? style : (buttonIndex == 0 ? firstStyle : (buttonIndex == itemCount - 1 ? lastStyle : midStyle));
						var isMouseOver = buttonRect.Contains(Event.current.mousePosition);
						var isHotControl = GUIUtility.hotControl == id;
						var isSelected = selected == buttonIndex;

						if (!isSelected)
							buttonStyle.Draw(buttonRect, content, enabled && isMouseOver && (isHotControl || GUIUtility.hotControl == 0), enabled && isHotControl, false, false);
						else
							selectedButtonStyle = buttonStyle;

						if (isMouseOver)
						{
							// GUIUtility.mouseUsed = true;
							// if (!string.IsNullOrEmpty(content.tooltip))
							// 	GUIStyle.SetMouseTooltip(content.tooltip, buttonRect);
						}

						break;
				}

				enabled = wasEnabled;
			}

			// draw selected button at the end so it overflows nicer
			if (selectedButtonStyle != null)
			{
				var buttonRect = buttonRects[selected];
				var content = contents[selected];
				var isMouseOver = buttonRect.Contains(Event.current.mousePosition);
				var isHotControl = GUIUtility.hotControl == selectedButtonControlID;
				var wasEnabled = enabled;
				enabled &= (contentsEnabled == null || contentsEnabled[selected]);
				selectedButtonStyle.Draw(buttonRect, content, enabled && isMouseOver && (isHotControl || GUIUtility.hotControl == 0), enabled && isHotControl, true, false);
				enabled = wasEnabled;
			}

			return selected;
		}

		public static List<int> DoButtonGrid(Rect position, List<int> selectedButtons, GUIContent[] contents, int itemsPerRow, GUIStyle style)
		{
			GUIStyle firstStyle = style;
			GUIStyle midStyle = style;
			GUIStyle lastStyle = style;
			bool[] contentsEnabled = null;

			var buttonSize = GUI.ToolbarButtonSize.Fixed;
			int itemCount = contents.Length;
			if (itemCount == 0)
			{
				return selectedButtons;
			}

			if (itemsPerRow <= 0)
			{
				Debug.LogWarning(
					"You are trying to create a SelectionGrid with zero or less elements to be displayed in the horizontal direction. Set itemsPerRow to a positive value."
				);
				return selectedButtons;
			}

			bool enabled = false;
			// Figure out how large each element should be
			int rows = (itemCount + itemsPerRow - 1) / itemsPerRow;
			float elemWidth = style.fixedWidth != 0
				? style.fixedWidth
				: (position.width - CalcTotalHorizSpacing(itemsPerRow, style, firstStyle, midStyle, lastStyle)) / itemsPerRow;
			float elemHeight = style.fixedHeight != 0 ? style.fixedHeight : (position.height - Mathf.Max(style.margin.top, style.margin.bottom) * (rows - 1)) / rows;

			Rect[] buttonRects = CalcGridRects(position, contents, itemsPerRow, elemWidth, elemHeight, style, firstStyle, midStyle, lastStyle, buttonSize);
			GUIStyle selectedButtonStyle = null;
			List<int> selectedButtonControlIDs = new List<int>();
			// clean up selected
			foreach (int selectedButton in selectedButtons.ToList())
			{
				if (selectedButton >= itemCount || selectedButton <= -1)
				{
					selectedButtons.Remove(selectedButton);
				}
			}
			
			for (int buttonIndex = 0; buttonIndex < itemCount; ++buttonIndex)
			{
				bool wasEnabled = enabled;
				enabled &= (contentsEnabled == null || contentsEnabled[buttonIndex]);
				var buttonRect = buttonRects[buttonIndex];
				var content = contents[buttonIndex];

				var id = GUIUtility.GetControlID(s_ButtonGridHash, FocusType.Passive, buttonRect);
				foreach (int selected in selectedButtons)
				{
					if (buttonIndex == selected)
					{
						selectedButtonControlIDs.Add(id);
					}
				}

				switch (Event.current.GetTypeForControl(id))
				{
					case EventType.MouseDown:
						if (HitTest(buttonRect, Event.current.mousePosition))
						{
							GUIUtility.hotControl = id;
							Event.current.Use();
						}

						break;
					case EventType.MouseDrag:
						if (GUIUtility.hotControl == id)
							Event.current.Use();
						break;
					case EventType.MouseUp:
						if (GUIUtility.hotControl == id)
						{
							GUIUtility.hotControl = 0;
							Event.current.Use();

							GUI.changed = true;
							if (Event.current.shift && selectedButtons.Any())
							{
								int lowestIndexSelected = selectedButtons.Min(x => x);
								int highestIndexSelected = selectedButtons.Max(x => x);
								int fromIndex = lowestIndexSelected;

								if (buttonIndex < highestIndexSelected || buttonIndex <= lowestIndexSelected)
								{
									fromIndex = highestIndexSelected;
								}

								int from = buttonIndex < fromIndex ? buttonIndex : fromIndex;
								int to = buttonIndex < fromIndex ? fromIndex : buttonIndex;
								bool shouldRemove = selectedButtons.Contains(buttonIndex);
								for (int i = 0; i <= itemCount; i++)
								{
									if (i >= from && i <= to)
									{
										if (selectedButtons.Contains(i))
										{
											if (shouldRemove)
											{
												selectedButtons.Remove(i);
											}

											continue;
										}

										selectedButtons.Add(i);
									}
								}
							}
							else
							{
								if (selectedButtons.Contains(buttonIndex))
								{
									selectedButtons.Remove(buttonIndex);
								}
								else
								{
									selectedButtons.Add(buttonIndex);
								}
							}

							return selectedButtons.OrderBy(x => x).ToList();
						}

						break;
					case EventType.Repaint:
						var buttonStyle = itemCount == 1 ? style : (buttonIndex == 0 ? firstStyle : (buttonIndex == itemCount - 1 ? lastStyle : midStyle));
						var isMouseOver = buttonRect.Contains(Event.current.mousePosition);
						var isHotControl = GUIUtility.hotControl == id;

						bool isSelected = selectedButtons.Any(x => x == buttonIndex);
						if (!isSelected)
							buttonStyle.Draw(
								buttonRect,
								content,
								enabled && isMouseOver && (isHotControl || GUIUtility.hotControl == 0),
								enabled && isHotControl,
								false,
								false
							);
						else
							selectedButtonStyle = buttonStyle;

						if (isMouseOver)
						{
							// GUIUtility.mouseUsed = true;
							// if (!string.IsNullOrEmpty(content.tooltip))
							// 	GUIStyle.SetMouseTooltip(content.tooltip, buttonRect);
						}

						break;
				}

				enabled = wasEnabled;
			}

			// draw selected button at the end so it overflows nicer
			if (selectedButtonStyle != null)
			{
				foreach (int selectedButton in selectedButtons)
				{
					var buttonRect = buttonRects[selectedButton];
					var content = contents[selectedButton];
					var isMouseOver = buttonRect.Contains(Event.current.mousePosition);
					var isHotControl = selectedButtonControlIDs.Any(x => x == GUIUtility.hotControl);
					var wasEnabled = enabled;
					enabled &= (contentsEnabled == null || contentsEnabled[selectedButton]);
					selectedButtonStyle.Draw(buttonRect, content, enabled && isMouseOver && (isHotControl || GUIUtility.hotControl == 0), enabled && isHotControl, true, false);
					enabled = wasEnabled;
				}
			}

			return selectedButtons;
		}
	}
}