﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ComponentBind;
using Lemma.Factories;
using Lemma.IO;
using Lemma.Util;

namespace Lemma.Components
{
	public class DialogueFile : Component<Main>
	{
		public Property<string> Name = new Property<string>();

		public override void Start()
		{
			if (!this.main.EditorEnabled && !string.IsNullOrEmpty(this.Name))
			{
				Phone phone = PlayerDataFactory.Instance.GetOrCreate<Phone>("Phone");
				try
				{
					DialogueForest forest = WorldFactory.Instance.Get<World>().DialogueForest;
					forest.Load(File.ReadAllText(Path.Combine(this.main.Content.RootDirectory, this.Name + ".dlz")));
#if DEBUG
					forest.Validate(this.main.Strings);
#endif
					phone.Bind(forest);
				}
				catch (IOException)
				{
					Log.d("Failed to load dialogue file: " + this.Name);
				}
			}
		}

		public void EditorProperties()
		{
			this.Entity.Add("Name", this.Name, new PropertyEntry.EditorData
			{
				Options = FileFilter.Get(this.main, Path.Combine(this.main.Content.RootDirectory), new[] { MapLoader.MapDirectory }, ".dlz"),
			});
		}
	}
}
