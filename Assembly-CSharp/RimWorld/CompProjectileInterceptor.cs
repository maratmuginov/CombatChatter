using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace RimWorld
{
	[StaticConstructorOnStartup]
	public class CompProjectileInterceptor : ThingComp
	{
		private int lastInterceptTicks = -999999;

		private int nextChargeTick = -1;

		private bool shutDown;

		private StunHandler stunner;

		private Sustainer sustainer;

		private float lastInterceptAngle;

		private bool debugInterceptNonHostileProjectiles;

		private static readonly Material ForceFieldMat = MaterialPool.MatFrom("Other/ForceField", ShaderDatabase.MoteGlow);

		private static readonly Material ForceFieldConeMat = MaterialPool.MatFrom("Other/ForceFieldCone", ShaderDatabase.MoteGlow);

		private static readonly MaterialPropertyBlock MatPropertyBlock = new MaterialPropertyBlock();

		private const float TextureActualRingSizeFactor = 1.16015625f;

		private static readonly Color InactiveColor = new Color(0.2f, 0.2f, 0.2f);

		public CompProperties_ProjectileInterceptor Props => (CompProperties_ProjectileInterceptor)props;

		public bool Active
		{
			get
			{
				if (!OnCooldown && !stunner.Stunned && !shutDown)
				{
					return !Charging;
				}
				return false;
			}
		}

		public bool OnCooldown => Find.TickManager.TicksGame < lastInterceptTicks + Props.cooldownTicks;

		public bool Charging
		{
			get
			{
				if (nextChargeTick >= 0)
				{
					return Find.TickManager.TicksGame > nextChargeTick;
				}
				return false;
			}
		}

		public int ChargeCycleStartTick
		{
			get
			{
				if (nextChargeTick < 0)
				{
					return 0;
				}
				return nextChargeTick;
			}
		}

		public int ChargingTicksLeft
		{
			get
			{
				if (nextChargeTick < 0)
				{
					return 0;
				}
				return nextChargeTick + Props.chargeDurationTicks - Find.TickManager.TicksGame;
			}
		}

		public int CooldownTicksLeft
		{
			get
			{
				if (!OnCooldown)
				{
					return 0;
				}
				return Props.cooldownTicks - (Find.TickManager.TicksGame - lastInterceptTicks);
			}
		}

		public bool ReactivatedThisTick => Find.TickManager.TicksGame - lastInterceptTicks == Props.cooldownTicks;

		public override void PostPostMake()
		{
			base.PostPostMake();
			if (Props.chargeIntervalTicks > 0)
			{
				nextChargeTick = Find.TickManager.TicksGame + Rand.Range(0, Props.chargeIntervalTicks);
			}
			stunner = new StunHandler(parent);
		}

		public override void PostDeSpawn(Map map)
		{
			if (sustainer != null)
			{
				sustainer.End();
			}
		}

		public bool CheckIntercept(Projectile projectile, Vector3 lastExactPos, Vector3 newExactPos)
		{
			if (!ModLister.RoyaltyInstalled)
			{
				Log.ErrorOnce("Shields are a Royalty-specific game system. If you want to use this code please check ModLister.RoyaltyInstalled before calling it.", 657212);
				return false;
			}
			Vector3 vector = parent.Position.ToVector3Shifted();
			float num = Props.radius + projectile.def.projectile.SpeedTilesPerTick + 0.1f;
			if ((newExactPos.x - vector.x) * (newExactPos.x - vector.x) + (newExactPos.z - vector.z) * (newExactPos.z - vector.z) > num * num)
			{
				return false;
			}
			if (!Active)
			{
				return false;
			}
			if (!InterceptsProjectile(Props, projectile))
			{
				return false;
			}
			if ((projectile.Launcher == null || !projectile.Launcher.HostileTo(parent)) && !debugInterceptNonHostileProjectiles && !Props.interceptNonHostileProjectiles)
			{
				return false;
			}
			if (!Props.interceptOutgoingProjectiles && (new Vector2(vector.x, vector.z) - new Vector2(lastExactPos.x, lastExactPos.z)).sqrMagnitude <= Props.radius * Props.radius)
			{
				return false;
			}
			if (!GenGeo.IntersectLineCircleOutline(new Vector2(vector.x, vector.z), Props.radius, new Vector2(lastExactPos.x, lastExactPos.z), new Vector2(newExactPos.x, newExactPos.z)))
			{
				return false;
			}
			lastInterceptAngle = lastExactPos.AngleToFlat(parent.TrueCenter());
			lastInterceptTicks = Find.TickManager.TicksGame;
			if (projectile.def.projectile.damageDef == DamageDefOf.EMP && Props.disarmedByEmpForTicks > 0)
			{
				BreakShield(new DamageInfo(projectile.def.projectile.damageDef, projectile.def.projectile.damageDef.defaultDamage));
			}
			Effecter effecter = new Effecter(Props.interceptEffect ?? EffecterDefOf.Interceptor_BlockedProjectile);
			effecter.Trigger(new TargetInfo(newExactPos.ToIntVec3(), parent.Map), TargetInfo.Invalid);
			effecter.Cleanup();
			return true;
		}

		public static bool InterceptsProjectile(CompProperties_ProjectileInterceptor props, Projectile projectile)
		{
			if (props.interceptGroundProjectiles)
			{
				return !projectile.def.projectile.flyOverhead;
			}
			if (props.interceptAirProjectiles)
			{
				return projectile.def.projectile.flyOverhead;
			}
			return false;
		}

		public override void CompTick()
		{
			if (ReactivatedThisTick && Props.reactivateEffect != null)
			{
				Effecter effecter = new Effecter(Props.reactivateEffect);
				effecter.Trigger(parent, TargetInfo.Invalid);
				effecter.Cleanup();
			}
			if (Find.TickManager.TicksGame >= nextChargeTick + Props.chargeDurationTicks)
			{
				nextChargeTick += Props.chargeIntervalTicks;
			}
			stunner.StunHandlerTick();
			if (Props.activeSound.NullOrUndefined())
			{
				return;
			}
			if (Active)
			{
				if (sustainer == null || sustainer.Ended)
				{
					sustainer = Props.activeSound.TrySpawnSustainer(SoundInfo.InMap(parent));
				}
				sustainer.Maintain();
			}
			else if (sustainer != null && !sustainer.Ended)
			{
				sustainer.End();
			}
		}

		public override void Notify_LordDestroyed()
		{
			base.Notify_LordDestroyed();
			shutDown = true;
		}

		public override void PostDraw()
		{
			base.PostDraw();
			Vector3 pos = parent.Position.ToVector3Shifted();
			pos.y = AltitudeLayer.MoteOverhead.AltitudeFor();
			float currentAlpha = GetCurrentAlpha();
			if (currentAlpha > 0f)
			{
				Color value = ((!Active && Find.Selector.IsSelected(parent)) ? InactiveColor : Props.color);
				value.a *= currentAlpha;
				MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, value);
				Matrix4x4 matrix = default(Matrix4x4);
				matrix.SetTRS(pos, Quaternion.identity, new Vector3(Props.radius * 2f * 1.16015625f, 1f, Props.radius * 2f * 1.16015625f));
				Graphics.DrawMesh(MeshPool.plane10, matrix, ForceFieldMat, 0, null, 0, MatPropertyBlock);
			}
			float currentConeAlpha_RecentlyIntercepted = GetCurrentConeAlpha_RecentlyIntercepted();
			if (currentConeAlpha_RecentlyIntercepted > 0f)
			{
				Color color = Props.color;
				color.a *= currentConeAlpha_RecentlyIntercepted;
				MatPropertyBlock.SetColor(ShaderPropertyIDs.Color, color);
				Matrix4x4 matrix2 = default(Matrix4x4);
				matrix2.SetTRS(pos, Quaternion.Euler(0f, lastInterceptAngle - 90f, 0f), new Vector3(Props.radius * 2f * 1.16015625f, 1f, Props.radius * 2f * 1.16015625f));
				Graphics.DrawMesh(MeshPool.plane10, matrix2, ForceFieldConeMat, 0, null, 0, MatPropertyBlock);
			}
		}

		private float GetCurrentAlpha()
		{
			return Mathf.Max(Mathf.Max(Mathf.Max(Mathf.Max(GetCurrentAlpha_Idle(), GetCurrentAlpha_Selected()), GetCurrentAlpha_RecentlyIntercepted()), GetCurrentAlpha_RecentlyActivated()), Props.minAlpha);
		}

		private float GetCurrentAlpha_Idle()
		{
			float idlePulseSpeed = Props.idlePulseSpeed;
			float minIdleAlpha = Props.minIdleAlpha;
			if (!Active)
			{
				return 0f;
			}
			if (parent.Faction == Faction.OfPlayer && !debugInterceptNonHostileProjectiles)
			{
				return 0f;
			}
			if (Find.Selector.IsSelected(parent))
			{
				return 0f;
			}
			return Mathf.Lerp(minIdleAlpha, 0.11f, (Mathf.Sin((float)(Gen.HashCombineInt(parent.thingIDNumber, 96804938) % 100) + Time.realtimeSinceStartup * idlePulseSpeed) + 1f) / 2f);
		}

		private float GetCurrentAlpha_Selected()
		{
			float num = Mathf.Max(2f, Props.idlePulseSpeed);
			if (!Find.Selector.IsSelected(parent) || stunner.Stunned || shutDown)
			{
				return 0f;
			}
			if (!Active)
			{
				return 0.41f;
			}
			return Mathf.Lerp(0.2f, 0.62f, (Mathf.Sin((float)(Gen.HashCombineInt(parent.thingIDNumber, 35990913) % 100) + Time.realtimeSinceStartup * num) + 1f) / 2f);
		}

		private float GetCurrentAlpha_RecentlyIntercepted()
		{
			int num = Find.TickManager.TicksGame - lastInterceptTicks;
			return Mathf.Clamp01(1f - (float)num / 40f) * 0.09f;
		}

		private float GetCurrentAlpha_RecentlyActivated()
		{
			if (!Active)
			{
				return 0f;
			}
			int num = Find.TickManager.TicksGame - (lastInterceptTicks + Props.cooldownTicks);
			return Mathf.Clamp01(1f - (float)num / 50f) * 0.09f;
		}

		private float GetCurrentConeAlpha_RecentlyIntercepted()
		{
			int num = Find.TickManager.TicksGame - lastInterceptTicks;
			return Mathf.Clamp01(1f - (float)num / 40f) * 0.82f;
		}

		public override IEnumerable<Gizmo> CompGetGizmosExtra()
		{
			if (!Prefs.DevMode)
			{
				yield break;
			}
			if (OnCooldown)
			{
				Command_Action command_Action = new Command_Action();
				command_Action.defaultLabel = "Dev: Reset cooldown";
				command_Action.action = delegate
				{
					lastInterceptTicks = Find.TickManager.TicksGame - Props.cooldownTicks;
				};
				yield return command_Action;
			}
			Command_Toggle command_Toggle = new Command_Toggle();
			command_Toggle.defaultLabel = "Dev: Intercept non-hostile";
			command_Toggle.isActive = () => debugInterceptNonHostileProjectiles;
			command_Toggle.toggleAction = delegate
			{
				debugInterceptNonHostileProjectiles = !debugInterceptNonHostileProjectiles;
			};
			yield return command_Toggle;
		}

		public override string CompInspectStringExtra()
		{
			StringBuilder stringBuilder = new StringBuilder();
			if (Props.interceptGroundProjectiles || Props.interceptAirProjectiles)
			{
				string value = ((!Props.interceptGroundProjectiles) ? ((string)"InterceptsProjectiles_AerialProjectiles".Translate()) : ((string)"InterceptsProjectiles_GroundProjectiles".Translate()));
				if (Props.cooldownTicks > 0)
				{
					stringBuilder.Append("InterceptsProjectilesEvery".Translate(value, Props.cooldownTicks.ToStringTicksToPeriod()));
				}
				else
				{
					stringBuilder.Append("InterceptsProjectiles".Translate(value));
				}
			}
			if (OnCooldown)
			{
				if (stringBuilder.Length != 0)
				{
					stringBuilder.AppendLine();
				}
				stringBuilder.Append("CooldownTime".Translate() + ": " + CooldownTicksLeft.ToStringTicksToPeriod());
			}
			if (stunner.Stunned)
			{
				if (stringBuilder.Length != 0)
				{
					stringBuilder.AppendLine();
				}
				stringBuilder.Append("DisarmedTime".Translate() + ": " + stunner.StunTicksLeft.ToStringTicksToPeriod());
			}
			if (shutDown)
			{
				if (stringBuilder.Length != 0)
				{
					stringBuilder.AppendLine();
				}
				stringBuilder.Append("ShutDown".Translate());
			}
			else if (Props.chargeIntervalTicks > 0)
			{
				if (stringBuilder.Length != 0)
				{
					stringBuilder.AppendLine();
				}
				if (Charging)
				{
					stringBuilder.Append("ChargingTime".Translate() + ": " + ChargingTicksLeft.ToStringTicksToPeriod());
				}
				else
				{
					stringBuilder.Append("ChargingNext".Translate((ChargeCycleStartTick - Find.TickManager.TicksGame).ToStringTicksToPeriod(), Props.chargeDurationTicks.ToStringTicksToPeriod(), Props.chargeIntervalTicks.ToStringTicksToPeriod()));
				}
			}
			return stringBuilder.ToString();
		}

		public override void PostPreApplyDamage(DamageInfo dinfo, out bool absorbed)
		{
			base.PostPreApplyDamage(dinfo, out absorbed);
			if (dinfo.Def == DamageDefOf.EMP && Props.disarmedByEmpForTicks > 0)
			{
				BreakShield(dinfo);
			}
		}

		private void BreakShield(DamageInfo dinfo)
		{
			float fTheta;
			Vector3 center;
			if (Active)
			{
				SoundDefOf.EnergyShield_Broken.PlayOneShot(new TargetInfo(parent));
				int num = Mathf.CeilToInt(Props.radius * 2f);
				fTheta = (float)Math.PI * 2f / (float)num;
				center = parent.TrueCenter();
				for (int i = 0; i < num; i++)
				{
					MoteMaker.MakeConnectingLine(PosAtIndex(i), PosAtIndex((i + 1) % num), ThingDefOf.Mote_LineEMP, parent.Map, 1.5f);
				}
			}
			dinfo.SetAmount((float)Props.disarmedByEmpForTicks / 30f);
			stunner.Notify_DamageApplied(dinfo, affectedByEMP: true);
			Vector3 PosAtIndex(int index)
			{
				return new Vector3(Props.radius * Mathf.Cos(fTheta * (float)index) + center.x, 0f, Props.radius * Mathf.Sin(fTheta * (float)index) + center.z);
			}
		}

		public override void PostExposeData()
		{
			base.PostExposeData();
			Scribe_Values.Look(ref lastInterceptTicks, "lastInterceptTicks", -999999);
			Scribe_Values.Look(ref shutDown, "shutDown", defaultValue: false);
			Scribe_Values.Look(ref nextChargeTick, "nextChargeTick", -1);
			Scribe_Deep.Look(ref stunner, "stunner", parent);
			if (Scribe.mode == LoadSaveMode.PostLoadInit)
			{
				if (Props.chargeIntervalTicks > 0 && nextChargeTick <= 0)
				{
					nextChargeTick = Find.TickManager.TicksGame + Rand.Range(0, Props.chargeIntervalTicks);
				}
				if (stunner == null)
				{
					stunner = new StunHandler(parent);
				}
			}
		}
	}
}
