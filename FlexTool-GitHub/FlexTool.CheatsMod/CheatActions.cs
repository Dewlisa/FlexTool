using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using RimWorld;
using RimWorld.Planet;
using Verse;
using Verse.AI;

namespace FlexTool.CheatsMod;

/// <summary>Map, colony, world and resource cheat implementations.</summary>
public static class CheatActions
{
    public static string ForEachColonist(Action<Pawn> action, string verb)
    {
        var map = Find.CurrentMap;
        var pawns = map?.mapPawns?.FreeColonists?.ToList();
        if (pawns is null || pawns.Count == 0) return "No colonists on current map";

        foreach (var p in pawns) action(p);
        return $"{verb} {pawns.Count} colonist(s)";
    }

    /// <summary>Removes injuries, diseases, and missing parts; keeps beneficial hediffs.</summary>
    public static void HealPawn(Pawn p)
    {
        if (p?.health?.hediffSet is null) return;

        var bad = p.health.hediffSet.hediffs
            .Where(h => h is Hediff_Injury || h is Hediff_MissingPart || h.def.isBad)
            .ToList();
        foreach (var h in bad)
            p.health.RemoveHediff(h);
    }

    public static void FillNeeds(Pawn p)
    {
        if (p?.needs is null) return;
        foreach (var n in p.needs.AllNeeds)
            n.CurLevel = n.MaxLevel;
    }

    /// <summary>Removes every negative memory affecting mood; keeps the positive ones.</summary>
    public static void RemoveBadMoodEffects(Pawn p)
    {
        var memories = p?.needs?.mood?.thoughts?.memories;
        if (memories is null) return;

        foreach (var m in memories.Memories.Where(t => t.MoodOffset() < 0f).ToList())
            memories.RemoveMemory(m);
    }

    public static string RemoveAllMentalStates()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var p in map.mapPawns.AllPawns.ToList())
        {
            if (p.MentalState != null)
            {
                p.MentalState.RecoverFromState();
                count++;
            }
        }
        return $"Cleared mental states on {count} pawn(s)";
    }

    public static string ReviveSelected()
    {
        var selected = Find.Selector?.SelectedObjects;
        if (selected is null || selected.Count == 0) return "Nothing selected - select a corpse or dead pawn";

        int revived = 0;
        foreach (var obj in selected.ToList())
        {
            Pawn target = null;
            if (obj is Corpse corpse) target = corpse.InnerPawn;
            else if (obj is Pawn pawn && pawn.Dead) target = pawn;

            if (target is null) continue;
            if (Resurrect(target)) revived++;
        }

        return revived > 0 ? $"Revived {revived} pawn(s)" : "No dead pawn in selection";
    }

    /// <summary>Version-safe resurrection: RW 1.4+ uses TryResurrect, older versions use ResurrectPawn.</summary>
    internal static bool Resurrect(Pawn p)
    {
        try
        {
            var method = AccessTools.Method(typeof(ResurrectionUtility), "TryResurrect")
                      ?? AccessTools.Method(typeof(ResurrectionUtility), "ResurrectPawn");
            if (method is null) return false;

            var args = new object[method.GetParameters().Length];
            args[0] = p;
            method.Invoke(null, args);
            return true;
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] Resurrect failed: " + ex);
            return false;
        }
    }

    /// <summary>Kills every spawned hostile pawn and removes hostile raid groups.</summary>
    public static string ClearAllThreats()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
        {
            if (pawn.Faction is null || pawn.Faction.IsPlayer || !pawn.Faction.HostileTo(Faction.OfPlayer)) continue;
            if (pawn.IsPrisonerOfColony) continue;

            try
            {
                pawn.Destroy(DestroyMode.KillFinalize);
                count++;
            }
            catch { }
        }

        foreach (var lord in map.lordManager.lords.ToList())
        {
            if (lord.faction != null && lord.faction.HostileTo(Faction.OfPlayer))
            {
                try { map.lordManager.RemoveLord(lord); } catch { }
            }
        }

        return count > 0 ? $"Cleared {count} threat(s)" : "No threats on this map";
    }

    /// <summary>Removes all pollution from the map (Biotech).</summary>
    public static string ClearAllPollution()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        var grid = map.pollutionGrid;
        if (grid is null) return "Pollution requires the Biotech DLC";

        int count = 0;
        foreach (var cell in map.AllCells)
        {
            if (grid.IsPolluted(cell))
            {
                grid.SetPolluted(cell, false);
                count++;
            }
        }

        return count > 0 ? $"Cleared pollution from {count} tile(s)" : "No pollution on this map";
    }

    /// <summary>Ends all active map conditions and clears queued storyteller incidents.</summary>
    public static string ClearAllEvents()
    {
        var conditions = EndAllConditions();

        int queued = 0;
        try
        {
            var queue = Find.Storyteller?.incidentQueue;
            if (queue != null)
            {
                queued = queue.Count;
                queue.Clear();
            }
        }
        catch { }

        return queued > 0 ? $"{conditions}; cleared {queued} queued incident(s)" : conditions;
    }

    // -- Colony cheats ---------------------------------------------------

    private static MethodInfo _finishProject;

    /// <summary>Version-safe research completion via ResearchManager.FinishProject.</summary>
    private static bool FinishProjectSafe(ResearchProjectDef proj)
    {
        try
        {
            _finishProject ??= AccessTools.Method(typeof(ResearchManager), "FinishProject");
            if (_finishProject is null) return false;

            var ps = _finishProject.GetParameters();
            var args = new object[ps.Length];
            args[0] = proj;
            for (int i = 1; i < ps.Length; i++)
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                    : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;

            _finishProject.Invoke(Find.ResearchManager, args);
            return true;
        }
        catch { return false; }
    }

    public static string FinishAllResearch()
    {
        if (Current.Game is null) return "No game loaded";

        int count = 0;
        foreach (var proj in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
        {
            if (proj.IsFinished) continue;
            if (FinishProjectSafe(proj)) count++;
        }
        return count > 0 ? $"Completed {count} research project(s)" : "All research already complete";
    }

    /// <summary>Finishes all blueprints and frames on the map (used by the Instant Build toggle).</summary>
    public static string CompleteAllConstruction(Map map)
    {
        if (map is null) return "No map loaded";

        var worker = map.mapPawns?.FreeColonists?.FirstOrDefault();
        if (worker is null) return "Need at least one colonist";

        int count = 0;

        foreach (var blueprint in map.listerThings.ThingsInGroup(ThingRequestGroup.Blueprint).OfType<Blueprint>().ToList())
        {
            try
            {
                if (blueprint.Spawned && blueprint.TryReplaceWithSolidThing(worker, out _, out _))
                    count++;
            }
            catch { }
        }

        foreach (var frame in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingFrame).OfType<Frame>().ToList())
        {
            try
            {
                if (!frame.Spawned) continue;
                frame.CompleteConstruction(worker);
                count++;
            }
            catch { }
        }

        return count > 0 ? $"Completed {count} construction(s)" : "Nothing to build";
    }

    /// <summary>Keeps every colony battery at 100% (used by the Unlimited Power toggle).</summary>
    public static void FillAllBatteries(Map map)
    {
        if (map?.listerBuildings is null) return;

        foreach (var b in map.listerBuildings.allBuildingsColonist)
        {
            var battery = b.TryGetComp<CompPowerBattery>();
            battery?.SetStoredEnergyPct(1f);
        }
    }

    /// <summary>Finishes all crafting bills currently being worked on and all unfinished items.</summary>
    public static string InstantCompleteCrafting(Map map)
    {
        if (map is null) return "No map loaded";

        int count = 0;

        // Zero out active DoBill job work.
        var workLeftField = AccessTools.Field(typeof(JobDriver_DoBill), "workLeft");
        if (workLeftField != null)
        {
            foreach (var p in map.mapPawns.FreeColonistsSpawned.ToList())
            {
                try
                {
                    if (p.jobs?.curDriver is JobDriver_DoBill driver && (float)workLeftField.GetValue(driver) > 0f)
                    {
                        workLeftField.SetValue(driver, 0f);
                        count++;
                    }
                }
                catch { }
            }
        }

        // Zero out remaining work on unfinished things (e.g. sculptures, weapons).
        foreach (var uft in map.listerThings.AllThings.OfType<UnfinishedThing>().ToList())
        {
            try
            {
                if (uft.workLeft > 0f)
                {
                    uft.workLeft = 0f;
                    count++;
                }
            }
            catch { }
        }

        return count > 0 ? $"Instantly completed {count} crafting job(s)" : "No crafting in progress";
    }

    public static string RepairAllBuildings()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var b in map.listerBuildings.allBuildingsColonist.ToList())
        {
            if (b.def.useHitPoints && b.HitPoints < b.MaxHitPoints)
            {
                b.HitPoints = b.MaxHitPoints;
                count++;
            }

            var breakdown = b.TryGetComp<CompBreakdownable>();
            if (breakdown != null && breakdown.BrokenDown)
            {
                breakdown.Notify_Repaired();
                count++;
            }
        }
        return count > 0 ? $"Repaired {count} building(s)" : "All buildings already intact";
    }

    public static string GrowAllCrops()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var plant in map.listerThings.ThingsInGroup(ThingRequestGroup.Plant).OfType<Plant>().ToList())
        {
            if (!plant.sown || plant.Growth >= 1f) continue;
            plant.Growth = 1f;
            plant.DirtyMapMesh(map);
            count++;
        }
        return count > 0 ? $"Grew {count} crop(s) to maturity" : "No growing crops found";
    }

    public static string CleanAllFilth()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        var filth = map.listerThings.ThingsInGroup(ThingRequestGroup.Filth).ToList();
        foreach (var f in filth)
        {
            try { if (!f.Destroyed) f.Destroy(DestroyMode.Vanish); }
            catch { }
        }

        return filth.Count > 0 ? $"Cleaned {filth.Count} filth" : "Map is already spotless";
    }

    /// <summary>Removes all thick (mountain) rock roofs from the map.</summary>
    public static string RemoveMountainRoofs()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var cell in map.AllCells)
        {
            var roof = map.roofGrid.RoofAt(cell);
            if (roof != null && roof.isThickRoof)
            {
                map.roofGrid.SetRoof(cell, null);
                count++;
            }
        }

        return count > 0 ? $"Removed mountain roof from {count} tile(s)" : "No mountain roofs on this map";
    }

    public static string SpawnRandomColonist()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        try
        {
            var pawn = PawnGenerator.GeneratePawn(PawnKindDefOf.Colonist, Faction.OfPlayer);
            var spot = DropCellFinder.TradeDropSpot(map);
            GenSpawn.Spawn(pawn, spot, map);
            Messages.Message($"{pawn.LabelShortCap} joined the colony!", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            return $"Spawned colonist: {pawn.LabelShortCap}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnRandomColonist failed: " + ex);
            return "Failed to spawn colonist";
        }
    }

    /// <summary>Spawns a colonist in their 20s with maxed skills, top traits and legendary gear.</summary>
    public static string SpawnGodColonist(Gender gender)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        try
        {
            var request = new PawnGenerationRequest(PawnKindDefOf.Colonist, Faction.OfPlayer,
                forceGenerateNewPawn: true, fixedGender: gender,
                fixedBiologicalAge: 25f, fixedChronologicalAge: 25f);
            var pawn = PawnGenerator.GeneratePawn(request);

            // Best traits.
            if (pawn.story?.traits != null)
            {
                pawn.story.traits.allTraits.Clear();
                foreach (var name in new[] { "Tough", "SpeedOffset", "Industriousness", "NaturalMood", "QuickSleeper", "NightOwl" })
                {
                    var def = DefDatabase<TraitDef>.GetNamedSilentFail(name);
                    if (def is null) continue;
                    int degree = def.degreeDatas != null && def.degreeDatas.Count > 0
                        ? def.degreeDatas.OrderByDescending(d => d.degree).First().degree : 0;
                    if (name == "NightOwl") continue; // skip - not a "best" trait
                    try { pawn.story.traits.allTraits.Add(new Trait(def, degree, forced: true)); } catch { }
                }
            }

            // Maxed skills with double passion.
            if (pawn.skills != null)
            {
                foreach (var rec in pawn.skills.skills)
                {
                    if (rec.TotallyDisabled) continue;
                    rec.Level = 20;
                    rec.passion = Passion.Major;
                    rec.xpSinceLastLevel = 0f;
                }
            }

            HealPawn(pawn);

            // Legendary gear: power armor set + charge rifle.
            pawn.apparel?.DestroyAll();
            pawn.equipment?.DestroyAllEquipment();
            GiveApparel(pawn, "Apparel_PowerArmor");
            GiveApparel(pawn, "Apparel_PowerArmorHelmet");
            GiveWeapon(pawn, "Gun_ChargeRifle");

            var spot = DropCellFinder.TradeDropSpot(map);
            GenSpawn.Spawn(pawn, spot, map);
            Messages.Message($"{pawn.LabelShortCap} the god colonist joined!", pawn, MessageTypeDefOf.PositiveEvent, historical: false);
            return $"Spawned god colonist: {pawn.LabelShortCap} ({gender})";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnGodColonist failed: " + ex);
            return "Failed to spawn god colonist";
        }
    }

    private static void GiveApparel(Pawn pawn, string defName)
    {
        try
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def is null || pawn.apparel is null) return;

            var apparel = (Apparel)ThingMaker.MakeThing(def, def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null);
            apparel.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);
            pawn.apparel.Wear(apparel, dropReplacedApparel: false);
        }
        catch { }
    }

    private static void GiveWeapon(Pawn pawn, string defName)
    {
        try
        {
            var def = DefDatabase<ThingDef>.GetNamedSilentFail(defName);
            if (def is null || pawn.equipment is null) return;

            var weapon = (ThingWithComps)ThingMaker.MakeThing(def);
            weapon.TryGetComp<CompQuality>()?.SetQuality(QualityCategory.Legendary, ArtGenerationContext.Colony);
            pawn.equipment.AddEquipment(weapon);
        }
        catch { }
    }

    private static Pawn GenerateOutsiderPawn()
    {
        var faction = Find.FactionManager?.AllFactions?
            .Where(f => !f.IsPlayer && !f.defeated && f.def.humanlikeFaction)
            .RandomElementWithFallback();
        var kind = faction?.def.pawnGroupMakers?
            .SelectMany(g => g.options)
            .Select(o => o.kind)
            .Where(k => k.RaceProps.Humanlike)
            .RandomElementWithFallback() ?? PawnKindDefOf.Villager;
        return PawnGenerator.GeneratePawn(kind, faction);
    }

    public static string SpawnRandomPrisoner()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        try
        {
            var pawn = GenerateOutsiderPawn();
            var spot = DropCellFinder.TradeDropSpot(map);
            GenSpawn.Spawn(pawn, spot, map);
            pawn.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Prisoner);
            return $"Spawned prisoner: {pawn.LabelShortCap}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnRandomPrisoner failed: " + ex);
            return "Failed to spawn prisoner";
        }
    }

    public static string SpawnRandomSlave()
    {
        if (!ModsConfig.IdeologyActive) return "Slavery requires the Ideology DLC";
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        try
        {
            var pawn = GenerateOutsiderPawn();
            var spot = DropCellFinder.TradeDropSpot(map);
            GenSpawn.Spawn(pawn, spot, map);
            pawn.guest?.SetGuestStatus(Faction.OfPlayer, GuestStatus.Slave);
            return $"Spawned slave: {pawn.LabelShortCap}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnRandomSlave failed: " + ex);
            return "Failed to spawn slave";
        }
    }

    public static List<PawnKindDef> PawnKindsForFaction(Faction faction)
    {
        try
        {
            var kinds = faction?.def.pawnGroupMakers?
                .SelectMany(g => g.options)
                .Select(o => o.kind)
                .Distinct()
                .OrderBy(k => k.label ?? k.defName)
                .ToList();
            return kinds ?? new List<PawnKindDef>();
        }
        catch { return new List<PawnKindDef>(); }
    }

    public static string SpawnPawnOfFaction(Faction faction, PawnKindDef kind)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";
        if (faction is null || kind is null) return "Nothing chosen";

        try
        {
            var pawn = PawnGenerator.GeneratePawn(kind, faction);
            var spot = DropCellFinder.TradeDropSpot(map);
            GenSpawn.Spawn(pawn, spot, map);
            return $"Spawned {pawn.LabelShortCap} ({faction.Name})";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnPawnOfFaction failed: " + ex);
            return "Failed to spawn pawn";
        }
    }

    public static string RecruitSelectedPrisoner()
    {
        var selected = Find.Selector?.SelectedObjects;
        if (selected is null || selected.Count == 0) return "Select a prisoner first";

        int count = 0;
        foreach (var obj in selected.ToList())
        {
            if (obj is not Pawn pawn || !pawn.IsPrisonerOfColony) continue;

            try
            {
                var recruiter = pawn.Map?.mapPawns?.FreeColonists?.FirstOrDefault();
                if (recruiter is null || !TryDoRecruit(recruiter, pawn))
                {
                    pawn.guest?.SetGuestStatus(null);
                    if (pawn.Faction != Faction.OfPlayer)
                        pawn.SetFaction(Faction.OfPlayer);
                }
                count++;
            }
            catch (Exception ex)
            {
                Log.Error("[FlexTool Cheats] Recruit failed: " + ex);
            }
        }

        return count > 0 ? $"Recruited {count} prisoner(s)" : "No prisoner selected - select a prisoner";
    }

    public static string InstantRecruitPawn(Pawn pawn)
    {
        if (pawn is null) return "Select a pawn first";
        if (pawn.Dead) return $"{pawn.LabelShortCap} is dead - revive them first";
        if (pawn.Faction == Faction.OfPlayer && !pawn.IsPrisonerOfColony && !pawn.IsSlaveOfColony)
            return "Pawn is already part of the colony";

        try
        {
            var recruiter = pawn.Map?.mapPawns?.FreeColonists?.FirstOrDefault();
            if (recruiter is null || !TryDoRecruit(recruiter, pawn))
            {
                pawn.guest?.SetGuestStatus(null);
                if (pawn.Faction != Faction.OfPlayer)
                    pawn.SetFaction(Faction.OfPlayer);
            }
            return $"Recruited {pawn.LabelShortCap} into the colony";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] Instant recruit failed: " + ex);
            return "Recruit failed - see log";
        }
    }

    private static bool TryDoRecruit(Pawn recruiter, Pawn prisoner)
    {
        try
        {
            var method = AccessTools.Method(typeof(InteractionWorker_RecruitAttempt), "DoRecruit",
                new[] { typeof(Pawn), typeof(Pawn), typeof(bool) });
            if (method is null) return false;

            method.Invoke(null, new object[] { recruiter, prisoner, true });
            return true;
        }
        catch { return false; }
    }

    /// <summary>Discovers all Anomaly codex entries and finishes anomaly knowledge research (reflection = safe without the DLC).</summary>
    public static string CompleteAnomalyFindings()
    {
        var codexGetter = AccessTools.PropertyGetter(typeof(Find), "EntityCodex");
        if (codexGetter is null) return "Anomaly features not available in this game version";

        object codex;
        try { codex = codexGetter.Invoke(null, null); }
        catch { codex = null; }
        if (codex is null) return "No game loaded or Anomaly not active";

        var entryType = AccessTools.TypeByName("RimWorld.EntityCodexEntryDef");
        var setDiscovered = entryType is null ? null : codex.GetType().GetMethods(AccessTools.all)
            .FirstOrDefault(m => m.Name == "SetDiscovered" && m.GetParameters().Length >= 1
                && m.GetParameters()[0].ParameterType == entryType);
        if (setDiscovered is null) return "Anomaly codex API not found";

        int discovered = 0;
        foreach (var def in GenDefDatabase.GetAllDefsInDatabaseForDef(entryType))
        {
            try
            {
                var ps = setDiscovered.GetParameters();
                var args = new object[ps.Length];
                args[0] = def;
                for (int i = 1; i < ps.Length; i++)
                    args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue : null;

                setDiscovered.Invoke(codex, args);
                discovered++;
            }
            catch { }
        }

        int research = 0;
        var knowledgeField = AccessTools.Field(typeof(ResearchProjectDef), "knowledgeCategory");
        if (knowledgeField != null)
        {
            foreach (var proj in DefDatabase<ResearchProjectDef>.AllDefsListForReading)
            {
                if (proj.IsFinished || knowledgeField.GetValue(proj) is null) continue;
                if (FinishProjectSafe(proj)) research++;
            }
        }

        return $"Discovered {discovered} codex entries, completed {research} anomaly research";
    }

    /// <summary>Returns the selected pawn, including the inner pawn of a selected corpse.</summary>
    public static Pawn SelectedPawn()
    {
        var selected = Find.Selector?.SelectedObjects;
        if (selected is null) return null;

        foreach (var obj in selected)
        {
            if (obj is Pawn pawn) return pawn;
            if (obj is Corpse corpse && corpse.InnerPawn != null) return corpse.InnerPawn;
        }
        return null;
    }

    // -- World cheats ------------------------------------------------------

    /// <summary>Jumps the game clock forward so the current map reaches the given hour.</summary>
    public static string SetHour(int hour)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        try
        {
            int curHour = GenLocalDate.HourOfDay(map);
            int deltaHours = ((hour - curHour) % 24 + 24) % 24;
            if (deltaHours == 0) return $"Already {hour}:00";

            Find.TickManager.DebugSetTicksGame(Find.TickManager.TicksGame + deltaHours * GenDate.TicksPerHour);
            return $"Time set to {hour}:00";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SetHour failed: " + ex);
            return "Failed to set time";
        }
    }

    /// <summary>Teleports every travelling player caravan straight to its destination tile.</summary>
    public static string TeleportAllCaravans()
    {
        var caravans = Find.WorldObjects?.Caravans?.Where(c => c.IsPlayerControlled).ToList();
        if (caravans is null || caravans.Count == 0) return "No player caravans on the world map";

        int count = 0;
        foreach (var caravan in caravans)
        {
            try
            {
                if (caravan.pather is null || !caravan.pather.Moving) continue;
                caravan.Tile = caravan.pather.Destination;
                caravan.pather.StopDead();
                count++;
            }
            catch { }
        }

        return count > 0 ? $"Teleported {count} caravan(s) to their destination" : "No caravans are travelling";
    }

    public static string AdjustFactionGoodwill(Faction faction, int amount)
    {
        if (faction is null) return "No faction";
        if (amount == 0) return $"{faction.Name} unchanged ({faction.PlayerGoodwill})";

        bool ok;
        try
        {
            ok = faction.TryAffectGoodwillWith(Faction.OfPlayer, amount,
                canSendMessage: true, canSendHostilityLetter: true, reason: null, lookTarget: null);
        }
        catch
        {
            var method = AccessTools.Method(typeof(Faction), "TryAffectGoodwillWith");
            if (method is null) return "Goodwill API not found";
            var ps = method.GetParameters();
            var args = new object[ps.Length];
            args[0] = Faction.OfPlayer;
            args[1] = amount;
            for (int i = 2; i < ps.Length; i++)
                args[i] = ps[i].HasDefaultValue ? ps[i].DefaultValue
                    : ps[i].ParameterType.IsValueType ? Activator.CreateInstance(ps[i].ParameterType) : null;
            ok = (bool)method.Invoke(faction, args);
        }

        return ok
            ? $"{faction.Name} goodwill {(amount > 0 ? "+" : "")}{amount} -> now {faction.PlayerGoodwill}"
            : $"Could not change goodwill with {faction.Name}";
    }

    public static string CallTradeCaravan(Faction faction, TraderKindDef traderKind)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";
        if (faction is null) return "No faction chosen";

        try
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.Misc, map);
            parms.faction = faction;
            parms.traderKind = traderKind;
            parms.forced = true;

            if (IncidentDefOf.TraderCaravanArrival.Worker.TryExecute(parms))
                return $"{traderKind?.label?.CapitalizeFirst() ?? "Trade"} caravan called from {faction.Name}";
            return $"Caravan from {faction.Name} could not arrive (map edge blocked?)";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] CallTradeCaravan failed: " + ex);
            return "Failed to call caravan";
        }
    }

    /// <summary>Makes every visiting NPC trade caravan pack up and leave the map.</summary>
    public static string SendTradeCaravansAway()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var lord in map.lordManager.lords.ToList())
        {
            if (lord.faction is null || lord.faction.IsPlayer || lord.faction.HostileTo(Faction.OfPlayer)) continue;
            if (lord.ownedPawns is null || lord.ownedPawns.Count == 0) continue;
            if (!lord.ownedPawns.Any(p => p.RaceProps.Humanlike && p.trader != null)) continue;

            try
            {
                foreach (var p in lord.ownedPawns.ToList())
                {
                    p.jobs?.StopAll();
                    if (!p.Spawned || p.Downed) continue;

                    var exit = RCellFinder.TryFindBestExitSpot(p, out var spot, TraverseMode.ByPawn)
                        ? spot : p.Position;
                    var job = JobMaker.MakeJob(JobDefOf.Goto, exit);
                    job.exitMapOnArrival = true;
                    job.locomotionUrgency = LocomotionUrgency.Jog;
                    p.jobs?.StartJob(job, JobCondition.InterruptForced);
                }
                map.lordManager.RemoveLord(lord);
                count++;
            }
            catch (Exception ex)
            {
                Log.Error("[FlexTool Cheats] SendTradeCaravansAway failed for a caravan: " + ex);
            }
        }

        return count > 0 ? $"Sent {count} trade caravan(s) away" : "No visiting trade caravans on this map";
    }

    public static string SetWeather(WeatherDef def)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";
        if (def is null) return "No weather chosen";

        map.weatherManager.TransitionTo(def);
        if (CheatsState.ForceWeather)
            CheatsState.ForcedWeather = def;
        return $"Weather changing to {def.label?.CapitalizeFirst() ?? def.defName}";
    }

    public static string EndAllConditions()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var cond in map.gameConditionManager.ActiveConditions.ToList())
        {
            try
            {
                if (!cond.Permanent) { cond.End(); count++; }
                else { map.gameConditionManager.ActiveConditions.Remove(cond); count++; }
            }
            catch { }
        }
        return count > 0 ? $"Ended {count} map condition(s)" : "No active conditions";
    }

    public static string RemoveEnemyStructures()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var b in map.listerThings.ThingsInGroup(ThingRequestGroup.BuildingArtificial).OfType<Building>().ToList())
        {
            if (b.Faction is null || b.Faction.IsPlayer || !b.Faction.HostileTo(Faction.OfPlayer)) continue;

            try
            {
                if (!b.Destroyed) { b.Destroy(DestroyMode.Vanish); count++; }
            }
            catch { }
        }
        return count > 0 ? $"Removed {count} enemy structure(s)" : "No enemy structures on this map";
    }

    public static string SpawnRaid(Faction faction)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";
        if (faction is null) return "No faction chosen";

        try
        {
            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.faction = faction;
            parms.forced = true;

            if (IncidentDefOf.RaidEnemy.Worker.TryExecute(parms))
                return $"Raid spawned from {faction.Name}";
            return $"Raid from {faction.Name} failed to execute";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnRaid failed: " + ex);
            return "Failed to spawn raid";
        }
    }

    public static string EndAllRaids()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
        {
            if (pawn.Faction is null || pawn.Faction.IsPlayer || !pawn.Faction.HostileTo(Faction.OfPlayer)) continue;
            if (pawn.IsPrisonerOfColony) continue;

            try
            {
                if (pawn.RaceProps.Humanlike)
                {
                    pawn.jobs?.StopAll();
                    if (!pawn.Downed && pawn.Spawned)
                    {
                        var exit = RCellFinder.TryFindBestExitSpot(pawn, out var spot, TraverseMode.ByPawn)
                            ? spot : pawn.Position;
                        var job = JobMaker.MakeJob(JobDefOf.Goto, exit);
                        job.exitMapOnArrival = true;
                        pawn.jobs?.StartJob(job, JobCondition.InterruptForced);
                        count++;
                    }
                    else
                    {
                        pawn.DeSpawn();
                        count++;
                    }
                }
                else
                {
                    pawn.DeSpawn();
                    count++;
                }
            }
            catch { }
        }

        foreach (var lord in map.lordManager.lords.ToList())
        {
            if (lord.faction != null && lord.faction.HostileTo(Faction.OfPlayer))
            {
                try { map.lordManager.RemoveLord(lord); } catch { }
            }
        }

        return count > 0 ? $"Cleared {count} hostile pawn(s)" : "No hostile pawns on this map";
    }

    public static string SpawnMechCluster()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        try
        {
            var def = DefDatabase<IncidentDef>.GetNamedSilentFail("MechCluster");
            if (def is null) return "Mech clusters need the Royalty DLC";

            var parms = StorytellerUtility.DefaultParmsNow(IncidentCategoryDefOf.ThreatBig, map);
            parms.forced = true;

            if (def.Worker.TryExecute(parms))
                return "Mech cluster incoming";
            return "Mech cluster failed to spawn";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnMechCluster failed: " + ex);
            return "Failed to spawn mech cluster";
        }
    }

    public static string DestroyAllMechs()
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";

        int count = 0;
        foreach (var pawn in map.mapPawns.AllPawnsSpawned.ToList())
        {
            if (!pawn.RaceProps.IsMechanoid) continue;
            if (pawn.Faction != null && pawn.Faction.IsPlayer) continue;

            try
            {
                pawn.Destroy(DestroyMode.KillFinalize);
                count++;
            }
            catch { }
        }
        return count > 0 ? $"Destroyed {count} hostile mech(s)" : "No hostile mechs on this map";
    }

    public static string TriggerIncidentDef(IncidentDef def)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";
        if (def is null) return "No event chosen";

        try
        {
            var target = def.TargetAllowed(map) ? (IIncidentTarget)map : Find.World;
            var parms = StorytellerUtility.DefaultParmsNow(def.category, target);
            parms.forced = true;

            if (def.Worker.CanFireNow(parms) || parms.forced)
            {
                if (def.Worker.TryExecute(parms))
                    return $"Triggered: {def.label?.CapitalizeFirst() ?? def.defName}";
            }
            return $"Could not trigger {def.label?.CapitalizeFirst() ?? def.defName}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] TriggerIncidentDef failed: " + ex);
            return $"Event failed: {def.label?.CapitalizeFirst() ?? def.defName}";
        }
    }

    // -- Resource cheats ---------------------------------------------------

    private static bool IsTextileOrLeather(ThingDef d)
    {
        return d.IsStuff && d.stuffProps?.categories != null
            && (d.stuffProps.categories.Contains(StuffCategoryDefOf.Fabric)
             || d.stuffProps.categories.Contains(StuffCategoryDefOf.Leathery));
    }

    public static List<ThingDef> ResourceDefs()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.CountAsResource && !d.IsApparel && !d.IsWeapon
                && !d.IsMedicine && !d.IsDrug && !d.IsIngestible
                && !IsTextileOrLeather(d))
            .OrderBy(d => d.label ?? d.defName)
            .ToList();
    }

    public static List<ThingDef> TextileDefs()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(IsTextileOrLeather)
            .OrderBy(d => d.label ?? d.defName)
            .ToList();
    }

    public static List<ThingDef> MedicineDefs()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.IsMedicine)
            .OrderBy(d => d.label ?? d.defName)
            .ToList();
    }

    public static List<ThingDef> FoodDefs()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.IsNutritionGivingIngestible && !d.IsDrug && !d.IsCorpse
                && d.ingestible.preferability >= FoodPreferability.RawBad)
            .OrderBy(d => d.label ?? d.defName)
            .ToList();
    }

    public static List<ThingDef> DrugDefs()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.IsDrug)
            .OrderBy(d => d.label ?? d.defName)
            .ToList();
    }

    public static List<ThingDef> WeaponDefs()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.IsWeapon && !d.CountAsResource && d.category == ThingCategory.Item)
            .OrderBy(d => d.label ?? d.defName)
            .ToList();
    }

    public static List<ThingDef> ApparelDefs()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.IsApparel)
            .OrderBy(d => d.label ?? d.defName)
            .ToList();
    }

    public static List<ThingDef> StructureDefs()
    {
        return DefDatabase<ThingDef>.AllDefsListForReading
            .Where(d => d.category == ThingCategory.Building && d.BuildableByPlayer)
            .OrderBy(d => d.label ?? d.defName)
            .ToList();
    }

    private static Thing MakeThing(ThingDef def)
    {
        var stuff = def.MadeFromStuff ? GenStuff.DefaultStuffFor(def) : null;
        return ThingMaker.MakeThing(def, stuff);
    }

    /// <summary>Spawns a stackable item near the trade drop spot, splitting into stacks as needed.</summary>
    public static string SpawnThing(ThingDef def, int amount)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";
        if (def is null || amount <= 0) return "Nothing to spawn";

        try
        {
            var spot = DropCellFinder.TradeDropSpot(map);
            int remaining = amount;
            while (remaining > 0)
            {
                var thing = MakeThing(def);
                thing.stackCount = Math.Min(remaining, Math.Max(1, def.stackLimit));
                remaining -= thing.stackCount;

                if (GenPlace.TryPlaceThing(thing, spot, map, ThingPlaceMode.Near, out var placed))
                    placed?.SetForbidden(false, warnOnFail: false);
            }
            return $"Spawned {amount}x {def.LabelCap}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnThing failed: " + ex);
            return $"Failed to spawn {def.LabelCap}";
        }
    }

    /// <summary>Spawns a single item/building with the chosen quality (if it supports quality).</summary>
    public static string SpawnThingWithQuality(ThingDef def, QualityCategory quality)
    {
        var map = Find.CurrentMap;
        if (map is null) return "No map loaded";
        if (def is null) return "Nothing to spawn";

        try
        {
            var thing = MakeThing(def);

            var comp = thing.TryGetComp<CompQuality>();
            comp?.SetQuality(quality, ArtGenerationContext.Colony);
            string qualityNote = comp != null ? $" ({QualityUtility.GetLabel(quality)})" : "";

            var spot = DropCellFinder.TradeDropSpot(map);

            if (thing.def.category == ThingCategory.Building)
            {
                if (thing.def.Minifiable)
                {
                    var minified = thing.TryMakeMinified();
                    if (GenPlace.TryPlaceThing(minified, spot, map, ThingPlaceMode.Near, out var placedMini))
                        placedMini?.SetForbidden(false, warnOnFail: false);
                    return $"Spawned {def.LabelCap}{qualityNote} (minified - ready to install)";
                }

                if (CellFinder.TryFindRandomCellNear(spot, map, 12,
                        c => c.Standable(map) && c.GetEdifice(map) is null, out var cell))
                {
                    thing.SetFactionDirect(Faction.OfPlayer);
                    GenSpawn.Spawn(thing, cell, map);
                    return $"Spawned {def.LabelCap}{qualityNote}";
                }
                return $"No clear spot to place {def.LabelCap}";
            }

            if (GenPlace.TryPlaceThing(thing, spot, map, ThingPlaceMode.Near, out var placed))
                placed?.SetForbidden(false, warnOnFail: false);
            return $"Spawned {def.LabelCap}{qualityNote}";
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Cheats] SpawnThingWithQuality failed: " + ex);
            return $"Failed to spawn {def.LabelCap}";
        }
    }
}
