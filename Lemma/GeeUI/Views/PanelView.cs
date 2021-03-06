﻿using GeeUI.Managers;
using GeeUI.Structs;
using Microsoft.Xna.Framework;

namespace GeeUI.Views
{
	public class PanelView : View
	{
		public NinePatch UnselectedNinepatch = new NinePatch();
		public NinePatch SelectedNinepatch = new NinePatch();

		private const int ChildrenPadding = 1;

		/// <summary>
		/// If true, the panel can be dragged by the user.
		/// </summary>
		public bool Draggable;

		/// <summary>
		/// If true, the panel can be resized by the user.
		/// </summary>
		public bool Resizeable = true;

		private bool SelectedOffChildren;
		private bool Resizing = false;
		private Vector2 MouseSelectedOffset;

		public override Rectangle BoundBox
		{
			get
			{
				NinePatch curPatch = Selected ? SelectedNinepatch : UnselectedNinepatch;
				return new Rectangle((int)RealPosition.X, (int)RealPosition.Y, Width + ChildrenPadding + curPatch.LeftWidth + curPatch.RightWidth, Height + ChildrenPadding + curPatch.TopHeight + curPatch.BottomHeight);
			}
		}

		public override Rectangle ContentBoundBox
		{
			get
			{
				NinePatch curPatch = Selected ? SelectedNinepatch : UnselectedNinepatch;
				return new Rectangle((int)RealPosition.X + curPatch.LeftWidth + ChildrenPadding, (int)RealPosition.Y + curPatch.TopHeight + ChildrenPadding, Width, Height);
			}
		}

		public PanelView(GeeUIMain GeeUI, View rootView, Vector2 position)
			: base(GeeUI, rootView)
		{
			SelectedNinepatch = GeeUIMain.NinePatchPanelSelected;
			UnselectedNinepatch = GeeUIMain.NinePatchPanelUnselected;
			Position.Value = position;
		}

		public override void Draw(Microsoft.Xna.Framework.Graphics.SpriteBatch spriteBatch)
		{
			NinePatch patch = Selected ? SelectedNinepatch : UnselectedNinepatch;
			patch.Draw(spriteBatch, AbsolutePosition, Width, Height, 0f, EffectiveOpacity);
			base.Draw(spriteBatch);
		}

		protected internal void FollowMouse()
		{
			if (Draggable && !Resizing)
			{
				Vector2 newMousePosition = InputManager.GetMousePosV();
				if (SelectedOffChildren && Selected && InputManager.IsMousePressed(MouseButton.Left))
				{
					Position.Value = (newMousePosition - MouseSelectedOffset);
				}
			}
			else if (Resizeable && Resizing)
			{
				Vector2 newMousePosition = InputManager.GetMousePosV();
				if (SelectedOffChildren && Selected && InputManager.IsMousePressed(MouseButton.Left))
				{
					int newWidth = (int)newMousePosition.X - BoundBox.X;
					int newHeight = (int)newMousePosition.Y - BoundBox.Y;
					if (newWidth >= 10 && newHeight >= 10)
					{
						Width.Value = newWidth;
						Height.Value = newHeight;
					}
				}
			}
		}

		public override void OnMClick(Vector2 position, bool fromChild)
		{
			SelectedOffChildren = !fromChild;
			Selected.Value = true;
			MouseSelectedOffset = position - Position;
			if (this.Draggable)
				this.BringToFront();

			Vector2 corner = new Vector2(BoundBox.Right, BoundBox.Bottom);
			Vector2 click = position;

			Resizing = Vector2.Distance(corner, click) <= 20 && !fromChild && click.X <= corner.X && click.Y <= corner.Y;

			FollowMouse();
			base.OnMClick(position, fromChild);
		}

		public override void OnMClickAway()
		{
			SelectedOffChildren = false;
			Selected.Value = false;
			Resizing = false;
			base.OnMClickAway();
		}

		public override void OnMOver()
		{
			FollowMouse();
			base.OnMOver();
		}

		public override void OnMOff()
		{
			FollowMouse();
			base.OnMOff();
		}
	}
}
