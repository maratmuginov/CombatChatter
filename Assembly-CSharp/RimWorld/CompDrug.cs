using Verse;

namespace RimWorld
{
	public class CompDrug : ThingComp
	{
		public CompProperties_Drug Props => (CompProperties_Drug)props;

		public override void PrePostIngested(Pawn ingester)
		{
			if (!Props.Addictive || !ingester.RaceProps.IsFlesh)
			{
				return;
			}
			HediffDef addictionHediffDef = Props.chemical.addictionHediff;
			Hediff_Addiction hediff_Addiction = AddictionUtility.FindAddictionHediff(ingester, Props.chemical);
			float num = AddictionUtility.FindToleranceHediff(ingester, Props.chemical)?.Severity ?? 0f;
			if (hediff_Addiction != null)
			{
				hediff_Addiction.Severity += Props.existingAddictionSeverityOffset;
			}
			else if (Rand.Value < Props.addictiveness && num >= Props.minToleranceToAddict)
			{
				ingester.health.AddHediff(addictionHediffDef);
				if (PawnUtility.ShouldSendNotificationAbout(ingester))
				{
					Find.LetterStack.ReceiveLetter("LetterLabelNewlyAddicted".Translate(Props.chemical.label).CapitalizeFirst(), "LetterNewlyAddicted".Translate(ingester.LabelShort, Props.chemical.label, ingester.Named("PAWN")).AdjustedFor(ingester).CapitalizeFirst(), LetterDefOf.NegativeEvent, ingester);
				}
				AddictionUtility.CheckDrugAddictionTeachOpportunity(ingester);
			}
			if (addictionHediffDef.causesNeed != null)
			{
				Need need = ingester.needs.AllNeeds.Find((Need x) => x.def == addictionHediffDef.causesNeed);
				if (need != null)
				{
					float effect = Props.needLevelOffset;
					AddictionUtility.ModifyChemicalEffectForToleranceAndBodySize(ingester, Props.chemical, ref effect);
					need.CurLevel += effect;
				}
			}
		}

		public override void PostIngested(Pawn ingester)
		{
			if (Props.Addictive && ingester.RaceProps.IsFlesh)
			{
				float num = ingester.health.hediffSet.GetFirstHediffOfDef(HediffDefOf.DrugOverdose)?.Severity ?? 0f;
				if (num < 0.9f && Rand.Value < Props.largeOverdoseChance)
				{
					float num2 = Rand.Range(0.85f, 0.99f);
					HealthUtility.AdjustSeverity(ingester, HediffDefOf.DrugOverdose, num2 - num);
					if (ingester.Faction == Faction.OfPlayer)
					{
						Messages.Message("MessageAccidentalOverdose".Translate(ingester.Named("INGESTER"), parent.LabelNoCount, parent.Named("DRUG")), ingester, MessageTypeDefOf.NegativeHealthEvent);
					}
				}
				else
				{
					float num3 = Props.overdoseSeverityOffset.RandomInRange / ingester.BodySize;
					if (num3 > 0f)
					{
						HealthUtility.AdjustSeverity(ingester, HediffDefOf.DrugOverdose, num3);
					}
				}
			}
			if (Props.isCombatEnhancingDrug && !ingester.Dead)
			{
				ingester.mindState.lastTakeCombatEnhancingDrugTick = Find.TickManager.TicksGame;
			}
			if (ingester.drugs != null)
			{
				ingester.drugs.Notify_DrugIngested(parent);
			}
		}
	}
}
