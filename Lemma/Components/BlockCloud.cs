﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using ComponentBind;
using Lemma.Factories;
using Lemma.Util;
using Microsoft.Xna.Framework;

namespace Lemma.Components
{
	public class BlockCloud : Component<Main>, IUpdateableComponent
	{
		public ListProperty<Entity.Handle> Blocks = new ListProperty<Entity.Handle>();

		[XmlIgnore]
		public Property<Vector3> Position = new Property<Vector3>();

		public Property<Vector3> AveragePosition = new Property<Vector3>();

		public Property<Voxel.t> Type = new Property<Voxel.t>();

		private List<PhysicsBlock> blocks = new List<PhysicsBlock>();

		public Property<float> Scale = new Property<float> { Value = 1.0f };

		public const int TotalBlocks = 30;

		private static Random random = new Random();

		private Noise3D noise = new Noise3D();

		[XmlIgnore]
		public Command Clear = new Command();

		[XmlIgnore]
		public Command<BEPUphysics.BroadPhaseEntries.Collidable, BEPUphysics.NarrowPhaseSystems.Pairs.ContactCollection> Collided = new Command<BEPUphysics.BroadPhaseEntries.Collidable, BEPUphysics.NarrowPhaseSystems.Pairs.ContactCollection>();

		public override void Awake()
		{
			base.Awake();
			this.Clear.Action = this.clear;
			this.EnabledWhenPaused = false;
			this.EnabledInEditMode = false;
		}

		private bool active = true;
		private void clear()
		{
			for (int i = 0; i < this.Blocks.Count; i++)
			{
				Entity e = this.Blocks[i];
				if (e != null)
					e.Delete.Execute();
			}
			this.Blocks.Clear();
			this.active = false;
		}

		void Blocks_Cleared()
		{
			this.blocks.Clear();
		}

		void Blocks_ItemChanged(int index, Entity.Handle old, Entity.Handle newValue)
		{
			PhysicsBlock block = newValue.Target.Get<PhysicsBlock>();
			block.Add(new CommandBinding<BEPUphysics.BroadPhaseEntries.Collidable, BEPUphysics.NarrowPhaseSystems.Pairs.ContactCollection>(block.Collided, this.Collided));
			this.blocks[index] = block;
		}

		void Blocks_ItemAdded(int index, Entity.Handle e)
		{
			PhysicsBlock block = e.Target.Get<PhysicsBlock>();
			block.Add(new CommandBinding<BEPUphysics.BroadPhaseEntries.Collidable, BEPUphysics.NarrowPhaseSystems.Pairs.ContactCollection>(block.Collided, this.Collided));
			this.blocks.Add(block);
		}

		void Blocks_ItemRemoved(int index, Entity.Handle t)
		{
			this.blocks.RemoveAt(index);
		}

		public override void delete()
		{
			base.delete();
			this.Blocks.ItemAdded -= this.Blocks_ItemAdded;
			this.Blocks.ItemRemoved -= this.Blocks_ItemRemoved;
			this.Blocks.ItemChanged -= this.Blocks_ItemChanged;
			this.Blocks.Cleared -= this.Blocks_Cleared;
		}

		public override void Start()
		{
			if (!main.EditorEnabled)
			{
				for (int i = 0; i < this.Blocks.Length; i++)
				{
					Entity e = this.Blocks[i];
					if (e == null)
					{
						this.Blocks.RemoveAt(i);
						i--;
					}
					else
						this.Blocks_ItemAdded(i, e);
				}
				this.Blocks.ItemAdded += this.Blocks_ItemAdded;
				this.Blocks.ItemRemoved += this.Blocks_ItemRemoved;
				this.Blocks.ItemChanged += this.Blocks_ItemChanged;
				this.Blocks.Cleared += this.Blocks_Cleared;
			}
		}

		private const float forceMultiplier = 0.15f;
		public void Update(float dt)
		{
			if (this.active)
			{
				if (this.Type.Value != Voxel.t.Empty && this.Blocks.Length == 0)
				{
					SceneryBlockFactory factory = Factory.Get<SceneryBlockFactory>();
					Vector3 blockSpawnPoint = this.Position;
					for (int i = 0; i < TotalBlocks; i++)
					{
						Entity block = factory.CreateAndBind(main);
						block.Get<Transform>().Position.Value = blockSpawnPoint + new Vector3(((float)BlockCloud.random.NextDouble() - 0.5f) * 2.0f, ((float)BlockCloud.random.NextDouble() - 0.5f) * 2.0f, ((float)BlockCloud.random.NextDouble() - 0.5f) * 2.0f);
						SceneryBlock sceneryBlock = block.Get<SceneryBlock>();
						sceneryBlock.Type.Value = this.Type;
						sceneryBlock.Scale.Value = this.Scale;
						this.Blocks.Add(block);
						main.Add(block);
					}
				}

				Vector3 avg = Vector3.Zero;
				for (int i = 0; i < this.blocks.Count; i++)
				{
					PhysicsBlock block = this.blocks[i];
					if (block.Active)
					{
						if (!block.Suspended)
						{
							Vector3 toCenter = this.Position - block.Box.Position;
							if (toCenter.Length() > 20.0f)
								block.Box.Position = this.Position + Vector3.Normalize(toCenter) * -20.0f;

							float offset = i + this.main.TotalTime;
							Vector3 force = toCenter + new Vector3(this.noise.Sample(new Vector3(offset)), this.noise.Sample(new Vector3(offset + 64)), noise.Sample(new Vector3(offset + 128))) * 10.0f * this.Scale;
							force *= main.ElapsedTime * forceMultiplier / this.Scale;
							block.Box.ApplyLinearImpulse(ref force);

							avg += block.Box.Position;
						}
					}
					else
					{
						this.Blocks.RemoveAt(i);
						i--;
					}
				}
				avg /= this.blocks.Count;
				this.AveragePosition.Value = avg;
			}
		}
	}
}