using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using RimWorld;
using UnityEngine;
using Verse;

namespace FlexTool.CheatsMod;

/// <summary>All pawn editing cheat implementations.</summary>
public static class PawnCheats
{
    /// <summary>Marks all cached pawn visuals dirty so appearance edits show immediately.</summary>
    private static void RefreshGraphics(Pawn p)
    {
        try
        {
            var renderer = p.Drawer?.renderer;
            if (renderer != null)
            {
                var dirty = AccessTools.Method(renderer.GetType(), "SetAllGraphicsDirty");
                if (dirty != null)
                {
                    dirty.Invoke(renderer, null);
                }
                else
                {
                    var graphics = AccessTools.Field(renderer.GetType(), "graphics")?.GetValue(renderer);
                    var resolve = graphics is null ? null : AccessTools.Method(graphics.GetType(), "ResolveAllGraphics");
                    resolve?.Invoke(graphics, null);
                }
            }

            PortraitsCache.SetDirty(p);
            GlobalTextureAtlasManager.TryMarkPawnFrameSetDirty(p);
        }
        catch { }
    }

    // -- Identity ---------------------------------------------------------

    public static string ApplyIdentity(Pawn p, string first, string nick, string last, string bioAgeText, string chronoAgeText)
    {
        if (p is null) return "No pawn selected";

        first = first?.Trim() ?? "";
        nick = nick?.Trim() ?? "";
        last = last?.Trim() ?? "";
        if (first.Length == 0) return "First name cannot be empty";

        p.Name = new NameTriple(first, nick.Length > 0 ? nick : first, last);

        string ageNote = "";
        if (!string.IsNullOrWhiteSpace(bioAgeText))
        {
            if (int.TryParse(bioAgeText.Trim(), out int years) && years >= 0 && years <= 5000)
            {
                p.ageTracker.AgeBiologicalTicks = years * (long)GenDate.TicksPerYear;
                ageNote = $", bio age {years}";
            }
            else
            {
                ageNote = " (bio age invalid - not applied)";
            }
        }

        string chronoNote = "";
        if (!string.IsNullOrWhiteSpace(chronoAgeText))
        {
            if (int.TryParse(chronoAgeText.Trim(), out int years) && years >= 0 && years <= 100000)
            {
                long ticks = years * (long)GenDate.TicksPerYear;
                if (ticks < p.ageTracker.AgeBiologicalTicks)
                    ticks = p.ageTracker.AgeBiologicalTicks;
                p.ageTracker.AgeChronologicalTicks = ticks;
                chronoNote = $", chrono age {years}";
            }
            else
            {
                chronoNote = " (chrono age invalid - not applied)";
            }
        }

        return $"Renamed to {p.Name.ToStringFull}{ageNote}{chronoNote}";
    }

    public static string SetBackstory(Pawn p, BackstorySlot slot, BackstoryDef def)
    {
        if (p?.story is null) return "Pawn has no story tracker";
        if (def is null) return "No backstory chosen";

        try
        {
            if (slot == BackstorySlot.Childhood)
                p.story.Childhood = def;
            else
                p.story.Adulthood = def;

            try { p.Notify_DisabledWorkTypesChanged(); } catch { }
            return $"{(slot == BackstorySlot.Childhood ? "Childhood" : "Adulthood")} set to {def.title?.CapitalizeFirst() ?? def.defName}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SetBackstory failed: " + ex);
            return "Failed to set backstory";
        }
    }

    public static string ToggleGender(Pawn p)
    {
        if (p is null) return "No pawn selected";

        p.gender = p.gender == Gender.Male ? Gender.Female : Gender.Male;
        RefreshGraphics(p);
        return $"{p.LabelShortCap} is now {p.gender}";
    }

    // -- Appearance ---------------------------------------------------------

    public static string SetHair(Pawn p, HairDef def)
    {
        if (p?.story is null) return "Pawn has no story tracker";

        p.story.hairDef = def;
        RefreshGraphics(p);
        return $"Hair set to {def.label?.CapitalizeFirst() ?? def.defName}";
    }

    public static string SetBeard(Pawn p, BeardDef def)
    {
        if (p?.style is null) return "Pawn has no style tracker";

        p.style.beardDef = def;
        try { p.style.Notify_StyleItemChanged(); } catch { }
        RefreshGraphics(p);
        return $"Beard set to {def.label?.CapitalizeFirst() ?? def.defName}";
    }

    public static string SetTattoo(Pawn p, TattooDef def)
    {
        if (p?.style is null) return "Pawn has no style tracker (needs Ideology)";

        if (def.tattooType == TattooType.Face)
            p.style.FaceTattoo = def;
        else
            p.style.BodyTattoo = def;

        try { p.style.Notify_StyleItemChanged(); } catch { }
        RefreshGraphics(p);
        return $"Tattoo set to {def.label?.CapitalizeFirst() ?? def.defName}";
    }

    public static string RemoveTattoos(Pawn p)
    {
        if (p?.style is null) return "Pawn has no style tracker (needs Ideology)";

        p.style.FaceTattoo = TattooDefOf.NoTattoo_Face;
        p.style.BodyTattoo = TattooDefOf.NoTattoo_Body;
        try { p.style.Notify_StyleItemChanged(); } catch { }
        RefreshGraphics(p);
        return $"Removed tattoos from {p.LabelShortCap}";
    }

    public static string SetBodyType(Pawn p, BodyTypeDef def)
    {
        if (p?.story is null) return "Pawn has no story tracker";

        p.story.bodyType = def;
        RefreshGraphics(p);
        return $"Body type set to {def.defName}";
    }

    public static string SetHeadType(Pawn p, HeadTypeDef def)
    {
        if (p?.story is null) return "Pawn has no story tracker";

        p.story.headType = def;
        RefreshGraphics(p);
        return $"Head type set to {def.defName}";
    }

    // -- Health -------------------------------------------------------------

    public static string DownPawn(Pawn p)
    {
        if (p is null) return "No pawn selected";
        if (p.Dead) return $"{p.LabelShortCap} is dead";
        if (p.Downed) return $"{p.LabelShortCap} is already downed";

        try
        {
            HealthUtility.DamageUntilDowned(p, allowBleedingWounds: false);
            return $"Downed {p.LabelShortCap}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] DownPawn failed: " + ex);
            return $"Could not down {p.LabelShortCap}";
        }
    }

    public static string AnesthetizePawn(Pawn p)
    {
        if (p is null) return "No pawn selected";
        if (p.Dead) return $"{p.LabelShortCap} is dead";

        try
        {
            var anesthetic = DefDatabase<HediffDef>.GetNamedSilentFail("Anesthetic");
            if (anesthetic is null) return "Anesthetic hediff not found";

            var hediff = HediffMaker.MakeHediff(anesthetic, p);
            hediff.Severity = 1f;
            p.health.AddHediff(hediff);
            return $"Anesthetized {p.LabelShortCap}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] AnesthetizePawn failed: " + ex);
            return $"Could not anesthetize {p.LabelShortCap}";
        }
    }

    public static string KillPawn(Pawn p)
    {
        if (p is null) return "No pawn selected";
        if (p.Dead) return $"{p.LabelShortCap} is already dead";

        try
        {
            bool godModeWasOn = CheatsState.GodMode;
            CheatsState.GodMode = false; // let the kill through even for colonists
            p.Kill(null);
            CheatsState.GodMode = godModeWasOn;
            return $"Killed {p.LabelShortCap}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] KillPawn failed: " + ex);
            return $"Could not kill {p.LabelShortCap}";
        }
    }

    public static string RevivePawn(Pawn p)
    {
        if (p is null) return "No pawn selected";
        if (!p.Dead) return $"{p.LabelShortCap} is not dead";

        return CheatActions.Resurrect(p) ? $"Revived {p.LabelShortCap}" : $"Could not revive {p.LabelShortCap}";
    }

    // -- Skills ---------------------------------------------------------------

    public static string MaxAllSkills(Pawn p)
    {
        if (p?.skills is null) return "Pawn has no skills";

        int count = 0;
        foreach (var rec in p.skills.skills)
        {
            if (rec.TotallyDisabled) continue;
            rec.Level = 20;
            rec.xpSinceLastLevel = 0f;
            count++;
        }
        return $"Maxed {count} skill(s) for {p.LabelShortCap}";
    }

    public static string SetAllPassions(Pawn p, Passion passion)
    {
        if (p?.skills is null) return "Pawn has no skills";

        int count = 0;
        foreach (var rec in p.skills.skills)
        {
            if (rec.TotallyDisabled) continue;
            rec.passion = passion;
            count++;
        }
        return $"Set {count} skill(s) to {PassionLabel(passion)} for {p.LabelShortCap}";
    }

    public static string SetSkillLevel(SkillRecord rec, int level)
    {
        if (rec is null) return "No skill";
        if (rec.TotallyDisabled) return $"{rec.def.skillLabel.CapitalizeFirst()} is disabled for this pawn";

        rec.Level = level;
        rec.xpSinceLastLevel = 0f;
        return $"{rec.def.skillLabel.CapitalizeFirst()} set to level {level}";
    }

    public static string SetSkillPassion(SkillRecord rec, Passion passion)
    {
        if (rec is null) return "No skill";

        rec.passion = passion;
        return $"{rec.def.skillLabel.CapitalizeFirst()} passion: {PassionLabel(passion)}";
    }

    public static string PassionLabel(Passion passion)
    {
        switch (passion)
        {
            case Passion.Major: return "Major (double flame)";
            case Passion.Minor: return "Minor (one flame)";
            default: return "None";
        }
    }

    // -- Traits -----------------------------------------------------------------

    /// <summary>Adds a trait directly to the list, bypassing duplicate/conflict checks so stacking works.</summary>
    public static string AddTrait(Pawn p, TraitDef def, int degree)
    {
        if (p?.story?.traits is null) return "Pawn has no traits tracker";

        p.story.traits.allTraits.Add(new Trait(def, degree, forced: true));

        try { p.Notify_DisabledWorkTypesChanged(); } catch { }
        try { p.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty(); } catch { }

        int stack = p.story.traits.allTraits.Count(t => t.def == def && t.Degree == degree);
        var label = def.DataAtDegree(degree)?.label?.CapitalizeFirst() ?? def.defName;
        return stack > 1 ? $"Added {label} (x{stack} stacked)" : $"Added {label}";
    }

    public static string RemoveTrait(Pawn p, Trait trait)
    {
        if (p?.story?.traits is null) return "Pawn has no traits tracker";
        if (!p.story.traits.allTraits.Contains(trait)) return "Trait already removed";

        p.story.traits.allTraits.Remove(trait);

        try { p.Notify_DisabledWorkTypesChanged(); } catch { }
        try { p.needs?.mood?.thoughts?.situational?.Notify_SituationalThoughtsDirty(); } catch { }

        return $"Removed {trait.LabelCap}";
    }

    // -- Abilities & status --------------------------------------------------------

    public static string AddPsylinkLevel(Pawn p)
    {
        if (p is null) return "No pawn selected";
        if (!ModsConfig.RoyaltyActive) return "Psylinks require the Royalty DLC";

        try
        {
            int max = p.GetMaxPsylinkLevel();
            int cur = p.GetPsylinkLevel();
            if (cur >= max) return $"{p.LabelShortCap} is already at max psylink level ({max})";

            p.ChangePsylinkLevel(1, sendLetter: false);
            return $"{p.LabelShortCap} is now psylink level {p.GetPsylinkLevel()}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] AddPsylinkLevel failed: " + ex);
            return "Failed to add psylink level";
        }
    }

    public static string LearnAllPsycasts(Pawn p)
    {
        if (p?.abilities is null) return "Pawn has no abilities tracker";
        if (!ModsConfig.RoyaltyActive) return "Psycasts require the Royalty DLC";

        int level = p.GetPsylinkLevel();
        if (level <= 0) return $"{p.LabelShortCap} has no psylink - add a level first";

        int count = 0;
        foreach (var def in DefDatabase<AbilityDef>.AllDefsListForReading)
        {
            try
            {
                if (def.abilityClass is null || !typeof(Psycast).IsAssignableFrom(def.abilityClass)) continue;
                if (def.level > level) continue;
                if (p.abilities.GetAbility(def) != null) continue;

                p.abilities.GainAbility(def);
                count++;
            }
            catch { }
        }

        return count > 0 ? $"Learned {count} psycast(s) up to level {level}" : "No new psycasts to learn";
    }

    public static string SetTitle(Pawn p, Faction faction, RoyalTitleDef title)
    {
        if (p?.royalty is null) return "Pawn has no royalty tracker";
        if (faction is null) return "No faction chosen";

        try
        {
            p.royalty.SetTitle(faction, title, grantRewards: false, rewardsOnlyForNewestTitle: false, sendLetter: false);
            return title is null
                ? $"Removed {faction.Name} title from {p.LabelShortCap}"
                : $"{p.LabelShortCap} is now {title.GetLabelCapFor(p)} of {faction.Name}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SetTitle failed: " + ex);
            return "Failed to set title";
        }
    }

    public static string SetIdeology(Pawn p, Ideo ideo)
    {
        if (p?.ideo is null) return "Pawn has no ideology tracker";
        if (ideo is null) return "No ideology chosen";

        p.ideo.SetIdeo(ideo);
        return $"{p.LabelShortCap} now follows {ideo.name}";
    }

    // -- Mutants (Anomaly, via reflection so it is safe without the DLC) ------------

    public static bool IsMutant(Pawn p)
    {
        try
        {
            var getter = AccessTools.PropertyGetter(typeof(Pawn), "IsMutant");
            return getter != null && (bool)getter.Invoke(p, null);
        }
        catch { return false; }
    }

    public static List<FloatMenuOption> MutantOptions(Pawn p, Action<string> setStatus)
    {
        var options = new List<FloatMenuOption>();
        try
        {
            var mutantDefType = AccessTools.TypeByName("RimWorld.MutantDef");
            if (mutantDefType is null) return options;

            foreach (var defObj in GenDefDatabase.GetAllDefsInDatabaseForDef(mutantDefType))
            {
                var def = defObj;
                options.Add(new FloatMenuOption(def.label?.CapitalizeFirst() ?? def.defName,
                    () => setStatus(MakeMutant(p, def))));
            }
            options.SortBy(o => o.Label);
        }
        catch { }
        return options;
    }

    private static string MakeMutant(Pawn p, Def mutantDef)
    {
        try
        {
            var util = AccessTools.TypeByName("RimWorld.MutantUtility");
            var method = util?.GetMethods(AccessTools.all)
                .FirstOrDefault(m => m.Name == "SetPawnAsMutantInstantly" && m.GetParameters().Length >= 2);
            if (method is null) return "Mutant API not found (needs Anomaly)";

            var ps = method.GetParameters();
            var args = new object[ps.Length];
            args[0] = p;
            args[1] = mutantDef;
            for (int i = 2; i < ps.Length; i++)
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                    : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;

            method.Invoke(null, args);
            return $"{p.LabelShortCap} transformed into {mutantDef.label?.CapitalizeFirst() ?? mutantDef.defName}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] MakeMutant failed: " + ex);
            return "Failed to transform pawn";
        }
    }

    public static string RevertMutant(Pawn p)
    {
        try
        {
            var util = AccessTools.TypeByName("RimWorld.MutantUtility");
            var method = util?.GetMethods(AccessTools.all)
                .FirstOrDefault(m => (m.Name.Contains("Restore") || m.Name.Contains("Revert"))
                    && m.GetParameters().Length >= 1 && m.GetParameters()[0].ParameterType == typeof(Pawn));
            if (method is null) return "Mutant revert API not found (needs Anomaly)";

            var ps = method.GetParameters();
            var args = new object[ps.Length];
            args[0] = p;
            for (int i = 1; i < ps.Length; i++)
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                    : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;

            method.Invoke(null, args);
            return $"{p.LabelShortCap} changed back to normal";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] RevertMutant failed: " + ex);
            return "Failed to revert pawn";
        }
    }

    // -- Slavery & imprisonment -----------------------------------------------------

    public static string MakeSlave(Pawn p)
    {
        if (p is null) return "No pawn selected";
        if (!ModsConfig.IdeologyActive) return "Slavery requires the Ideology DLC";
        if (p.Dead) return $"{p.LabelShortCap} is dead";
        if (p.IsSlaveOfColony) return $"{p.LabelShortCap} is already a slave";

        try
        {
            p.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
            return $"{p.LabelShortCap} is now a slave of the colony";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] MakeSlave failed: " + ex);
            return "Failed to enslave pawn";
        }
    }

    public static string MakePrisoner(Pawn p)
    {
        if (p is null) return "No pawn selected";
        if (p.Dead) return $"{p.LabelShortCap} is dead";
        if (p.IsPrisonerOfColony) return $"{p.LabelShortCap} is already a prisoner";

        try
        {
            p.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            return $"{p.LabelShortCap} is now a prisoner of the colony";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] MakePrisoner failed: " + ex);
            return "Failed to imprison pawn";
        }
    }

    // -- Relationships ----------------------------------------------------------------

    public static string ForceRelation(Pawn p, Pawn other, bool married)
    {
        if (p?.relations is null || other?.relations is null) return "Pawn has no relations tracker";
        if (p == other) return "Cannot pair a pawn with themselves";

        try
        {
            // Clear existing romantic relations between the two first.
            RemoveRomanticRelations(p, other);

            if (married)
            {
                p.relations.AddDirectRelation(PawnRelationDefOf.Spouse, other);
                return $"{p.LabelShortCap} and {other.LabelShortCap} are now married";
            }

            p.relations.AddDirectRelation(PawnRelationDefOf.Lover, other);
            return $"{p.LabelShortCap} and {other.LabelShortCap} are now lovers";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] ForceRelation failed: " + ex);
            return "Failed to create relation";
        }
    }

    public static string ForceBreakup(Pawn p)
    {
        if (p?.relations is null) return "Pawn has no relations tracker";

        int count = 0;
        try
        {
            foreach (var rel in p.relations.DirectRelations.ToList())
            {
                if (rel.def == PawnRelationDefOf.Lover || rel.def == PawnRelationDefOf.Spouse || rel.def == PawnRelationDefOf.Fiance)
                {
                    p.relations.RemoveDirectRelation(rel.def, rel.otherPawn);
                    count++;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] ForceBreakup failed: " + ex);
        }

        return count > 0 ? $"Removed {count} romantic relation(s) from {p.LabelShortCap}" : $"{p.LabelShortCap} has no romantic relations";
    }

    private static void RemoveRomanticRelations(Pawn a, Pawn b)
    {
        foreach (var def in new[] { PawnRelationDefOf.Lover, PawnRelationDefOf.Spouse, PawnRelationDefOf.Fiance })
        {
            try
            {
                if (a.relations.DirectRelationExists(def, b))
                    a.relations.RemoveDirectRelation(def, b);
            }
            catch { }
        }
    }

    // -- Needs, memory & value -----------------------------------------------------------

    public static string SetNeed(Need need, float pct)
    {
        if (need is null) return "No need";

        need.CurLevelPercentage = Mathf.Clamp01(pct);
        return $"{need.LabelCap} set to {pct:P0}";
    }

    public static string RemoveMemory(Pawn p, Thought_Memory memory)
    {
        var memories = p?.needs?.mood?.thoughts?.memories;
        if (memories is null) return "Pawn has no memories tracker";

        try
        {
            memories.RemoveMemory(memory);
            return $"Removed memory: {memory.LabelCap}";
        }
        catch
        {
            return "Memory already removed";
        }
    }

    /// <summary>Raises the pawn's market value by maxing skills and passions and healing them.</summary>
    public static string BoostSellValue(Pawn p)
    {
        if (p is null) return "No pawn selected";

        MaxAllSkills(p);
        SetAllPassions(p, Passion.Major);
        CheatActions.HealPawn(p);

        float value = 0f;
        try { value = p.MarketValue; } catch { }
        return value > 0f
            ? $"Boosted {p.LabelShortCap}'s sell value - now worth {value:F0} silver"
            : $"Boosted {p.LabelShortCap}'s sell value";
    }
}
