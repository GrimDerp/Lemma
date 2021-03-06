﻿using ComponentBind;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using GeeUI.Managers;

namespace GeeUI.Views
{
	public class TextView : View
	{
		public Property<string> Text = new Property<string>() { Value = "" };

		public Color TextColor;

		public TextJustification TextJustification = TextJustification.Left;

		public Property<float> TextScale = new Property<float>() { Value = 1f };

		public float TextWidth
		{
			get
			{
				return (GeeUIMain.Font.MeasureString(Text).X * TextScale.Value);
			}
		}

		private Vector2 TextOrigin
		{
			get
			{
				var width = (int)(GeeUIMain.Font.MeasureString(Text).X * TextScale.Value);
				var height = (int)(GeeUIMain.Font.MeasureString(Text).Y * TextScale.Value);
				switch (TextJustification)
				{
					default:
						return new Vector2(0, 0);

					case TextJustification.Center:
						return new Vector2((Width.Value/2) - (width/2), (Height.Value/2) - (height/2))*new Vector2(-1, -1);

					case TextJustification.Right:
						return new Vector2(Width.Value - width, 0) * -1;
				}
			}
		}

		public Property<bool> AutoSize = new Property<bool>() { Value = true };

		public TextView(GeeUIMain GeeUI, View rootView, string text, Vector2 position)
			: base(GeeUI, rootView)
		{
			Text.Value = text;
			Position.Value = position;
			TextColor = GeeUI.TextColorDefault;

			Text.AddBinding(new NotifyBinding(HandleResize, () => AutoSize.Value, Text));
			if(AutoSize.Value) HandleResize();
		}

		private void HandleResize()
		{
			var width = (int)(GeeUIMain.Font.MeasureString(Text).X * TextScale.Value);
			var height = (int)(GeeUIMain.Font.MeasureString(Text).Y * TextScale.Value);
			this.Width.Value = width;
			this.Height.Value = height;
		}

		internal static string TruncateString(string input, int widthAllowed, string ellipsis = "...")
		{
			string cur = "";
			foreach (char t in input)
			{
				float width = GeeUIMain.Font.MeasureString(cur + t + ellipsis).X;
				if (width > widthAllowed)
					break;
				cur += t;
			}
			return cur + (cur.Length != input.Length ? ellipsis : "");
		}

		public override void Draw(SpriteBatch spriteBatch)
		{
			spriteBatch.DrawString(GeeUIMain.Font, Text, AbsolutePosition, TextColor * EffectiveOpacity, 0f, TextOrigin, TextScale.Value, SpriteEffects.None, 0f);
			base.Draw(spriteBatch);
		}

	}
	public enum TextJustification
	{
		Left,
		Center,
		Right
	}
}
