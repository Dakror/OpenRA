#region Copyright & License Information
/*
 * Copyright 2007-2015 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Graphics;
using OpenRA.Primitives;
using OpenRA.Traits;

namespace OpenRA.Mods.Common.Traits
{
	public interface IRenderActorPreviewSpritesInfo
	{
		IEnumerable<IActorPreview> RenderPreviewSprites(ActorPreviewInitializer init, RenderSpritesInfo rs, string image, int facings, PaletteReference p);
	}

	public class RenderSpritesInfo : IRenderActorPreviewInfo, ITraitInfo
	{
		[Desc("The sequence name that defines the actor sprites. Defaults to the actor name.")]
		public readonly string Image = null;

		[FieldLoader.LoadUsing("LoadRaceImages")]
		[Desc("A dictionary of race-specific image overrides.")]
		public readonly Dictionary<string, string> RaceImages = null;

		[Desc("Custom palette name")]
		public readonly string Palette = null;

		[Desc("Custom PlayerColorPalette: BaseName")]
		public readonly string PlayerPalette = "player";

		[Desc("Change the sprite image size.")]
		public readonly float Scale = 1f;

		protected static object LoadRaceImages(MiniYaml y)
		{
			MiniYaml images;

			if (!y.ToDictionary().TryGetValue("RaceImages", out images))
				return null;

			return images.Nodes.ToDictionary(kv => kv.Key, kv => kv.Value.Value);
		}

		public virtual object Create(ActorInitializer init) { return new RenderSprites(init, this); }

		public IEnumerable<IActorPreview> RenderPreview(ActorPreviewInitializer init)
		{
			var sequenceProvider = init.World.Map.SequenceProvider;
			var race = init.Contains<RaceInit>() ? init.Get<RaceInit, string>() : init.Owner.Country.Race;
			var image = GetImage(init.Actor, sequenceProvider, race);
			var palette = init.WorldRenderer.Palette(Palette ?? PlayerPalette + init.Owner.InternalName);

			var facings = 0;
			var body = init.Actor.Traits.GetOrDefault<BodyOrientationInfo>();
			if (body != null)
				facings = body.QuantizedFacings == -1 ?
					init.Actor.Traits.Get<IQuantizeBodyOrientationInfo>().QuantizedBodyFacings(init.Actor, sequenceProvider, init.Owner.Country.Race) :
					body.QuantizedFacings;

			foreach (var spi in init.Actor.Traits.WithInterface<IRenderActorPreviewSpritesInfo>())
				foreach (var preview in spi.RenderPreviewSprites(init, this, image, facings, palette))
					yield return preview;
		}

		public string GetImage(ActorInfo actor, SequenceProvider sequenceProvider, string race)
		{
			if (RaceImages != null)
			{
				string raceImage = null;
				if (RaceImages.TryGetValue(race, out raceImage) && sequenceProvider.HasSequence(raceImage))
					return raceImage;
			}

			return (Image ?? actor.Name).ToLowerInvariant();
		}
	}

	public class RenderSprites : IRender, ITick, INotifyOwnerChanged, INotifyEffectiveOwnerChanged
	{
		class AnimationWrapper
		{
			public readonly AnimationWithOffset Animation;
			public readonly string Palette;
			public readonly bool IsPlayerPalette;
			public PaletteReference PaletteReference { get; private set; }

			public AnimationWrapper(AnimationWithOffset animation, string palette, bool isPlayerPalette)
			{
				Animation = animation;
				Palette = palette;
				IsPlayerPalette = isPlayerPalette;
			}

			public void CachePalette(WorldRenderer wr, Player owner)
			{
				PaletteReference = wr.Palette(IsPlayerPalette ? Palette + owner.InternalName : Palette);
			}

			public void OwnerChanged()
			{
				// Update the palette reference next time we draw
				if (IsPlayerPalette)
					PaletteReference = null;
			}

			public bool IsVisible
			{
				get
				{
					return Animation.DisableFunc == null || !Animation.DisableFunc();
				}
			}
		}

		readonly string race;
		readonly RenderSpritesInfo info;
		readonly Dictionary<string, AnimationWrapper> anims = new Dictionary<string, AnimationWrapper>();
		string cachedImage;

		public static Func<int> MakeFacingFunc(Actor self)
		{
			var facing = self.TraitOrDefault<IFacing>();
			if (facing == null) return () => 0;
			return () => facing.Facing;
		}

		public RenderSprites(ActorInitializer init, RenderSpritesInfo info)
		{
			this.info = info;
			race = init.Contains<RaceInit>() ? init.Get<RaceInit, string>() : init.Self.Owner.Country.Race;
		}

		public string GetImage(Actor self)
		{
			if (cachedImage != null)
				return cachedImage;

			return cachedImage = info.GetImage(self.Info, self.World.Map.SequenceProvider, race);
		}

		public void UpdatePalette()
		{
			foreach (var anim in anims.Values)
				anim.OwnerChanged();
		}

		public virtual void OnOwnerChanged(Actor self, Player oldOwner, Player newOwner) { UpdatePalette(); }
		public void OnEffectiveOwnerChanged(Actor self, Player oldEffectiveOwner, Player newEffectiveOwner) { UpdatePalette(); }

		public virtual IEnumerable<IRenderable> Render(Actor self, WorldRenderer wr)
		{
			foreach (var a in anims.Values)
			{
				if (!a.IsVisible)
					continue;

				if (a.PaletteReference == null)
				{
					var owner = self.EffectiveOwner != null && self.EffectiveOwner.Disguised ? self.EffectiveOwner.Owner : self.Owner;
					a.CachePalette(wr, owner);
				}

				foreach (var r in a.Animation.Render(self, wr, a.PaletteReference, info.Scale))
					yield return r;
			}
		}

		public virtual void Tick(Actor self)
		{
			foreach (var a in anims.Values)
				a.Animation.Animation.Tick();
		}

		public void Add(string key, AnimationWithOffset anim, string palette = null, bool isPlayerPalette = false)
		{
			// Use defaults
			if (palette == null)
			{
				palette = info.Palette ?? info.PlayerPalette;
				isPlayerPalette = info.Palette == null;
			}

			anims.Add(key, new AnimationWrapper(anim, palette, isPlayerPalette));
		}

		public void Remove(string key)
		{
			anims.Remove(key);
		}

		public static string NormalizeSequence(Animation anim, DamageState state, string sequence)
		{
			var states = new Pair<DamageState, string>[]
			{
				Pair.New(DamageState.Critical, "critical-"),
				Pair.New(DamageState.Heavy, "damaged-"),
				Pair.New(DamageState.Medium, "scratched-"),
				Pair.New(DamageState.Light, "scuffed-")
			};

			// Remove existing damage prefix
			foreach (var s in states)
			{
				if (sequence.StartsWith(s.Second))
				{
					sequence = sequence.Substring(s.Second.Length);
					break;
				}
			}

			foreach (var s in states)
				if (state >= s.First && anim.HasSequence(s.Second + sequence))
					return s.Second + sequence;

			return sequence;
		}

		// Required by RenderSimple
		protected int2 AutoSelectionSize(Actor self)
		{
			return anims.Values.Where(b => b.IsVisible
				&& b.Animation.Animation.CurrentSequence != null)
					.Select(a => (a.Animation.Animation.Image.Size * info.Scale).ToInt2())
					.FirstOrDefault();
		}
	}
}
