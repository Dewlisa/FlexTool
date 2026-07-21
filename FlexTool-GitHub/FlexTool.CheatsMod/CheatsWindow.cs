using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.Sound;

namespace FlexTool.CheatsMod;

/// <summary>
/// The tabbed cheats window: Main, Pawn, Resources, Colony, World.
/// Every tab is split into clear TOGGLES / ACTIONS sections.
/// </summary>
public class CheatsWindow : Window
{
    private enum Tab { Main, Pawn, Resources, Colony, World }

    private static readonly Tab[] Tabs = (Tab[])Enum.GetValues(typeof(Tab));
    private static readonly string[] TabNames = { "Main", "Pawn", "Resources", "Colony", "World" };

    public static readonly Color Accent = Color.white;
    private static readonly Color BetaRed = new Color(0.906f, 0.298f, 0.235f);
    private static readonly Color OnColor = new Color(0.3f, 0.9f, 0.4f);
    private static readonly Color OffColor = new Color(0.6f, 0.6f, 0.6f);

    private Tab _tab = Tab.Main;
    private Vector2 _scroll;
    private string _status = "";

    // Pawn tab edit buffers (synced when the selected pawn changes)
    private int _pawnId = -1;
    private string _firstBuf = "", _nickBuf = "", _lastBuf = "", _ageBuf = "", _chronoBuf = "";

    private static bool _isOpen;

    public static bool IsOpen => _isOpen;

    public override Vector2 InitialSize => new Vector2(560f, 620f);

    public CheatsWindow()
    {
        doCloseX = true;
        draggable = true;
        preventCameraMotion = false;
        absorbInputAroundWindow = false;
        closeOnClickedOutside = false;
        closeOnAccept = false;
    }

    public override void PostOpen()
    {
        base.PostOpen();
        _isOpen = true;
    }

    public override void PostClose()
    {
        base.PostClose();
        _isOpen = false;
    }

    public static void Toggle()
    {
        var existing = Find.WindowStack.WindowOfType<CheatsWindow>();
        if (existing != null)
            existing.Close();
        else
            Find.WindowStack.Add(new CheatsWindow());
    }

    public override void DoWindowContents(Rect inRect)
    {
        try
        {
            DoContents(inRect);
        }
        catch (Exception ex)
        {
            EndList();
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Log.ErrorOnce("[FlexTool Cheats] UI error: " + ex, 823194501);
        }
    }

    private void DoContents(Rect inRect)
    {
        var prev = GUI.color;

        // -- Header --
        Text.Font = GameFont.Medium;
        GUI.color = Accent;
        Widgets.Label(new Rect(inRect.x, inRect.y, 300f, 30f), "FlexTool Cheats");
        var titleSize = Text.CalcSize("FlexTool Cheats ");
        GUI.color = BetaRed;
        Widgets.Label(new Rect(inRect.x + titleSize.x, inRect.y, 100f, 30f), "Beta");
        GUI.color = new Color(1f, 1f, 1f, 0.25f);
        Widgets.DrawLineHorizontal(inRect.x, inRect.y + 34f, inRect.width);
        GUI.color = prev;
        Text.Font = GameFont.Small;

        // -- Tab bar --
        float tabW = (inRect.width - (Tabs.Length - 1) * 4f) / Tabs.Length;
        var tabBar = new Rect(inRect.x, inRect.y + 42f, inRect.width, 30f);
        for (int i = 0; i < Tabs.Length; i++)
        {
            var r = new Rect(tabBar.x + i * (tabW + 4f), tabBar.y, tabW, tabBar.height);
            bool selected = _tab == Tabs[i];

            if (selected)
                Widgets.DrawHighlightSelected(r);
            else if (Mouse.IsOver(r))
                Widgets.DrawHighlight(r);

            GUI.color = selected ? Accent : Color.white;
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(r, TabNames[i]);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prev;

            if (Widgets.ButtonInvisible(r))
            {
                _tab = Tabs[i];
                _scroll = Vector2.zero;
                SoundStarter.PlayOneShotOnCamera(SoundDefOf.Click, null);
            }
        }

        // -- Status bar --
        var statusRect = new Rect(inRect.x, inRect.yMax - 26f, inRect.width, 26f);
        Widgets.DrawBoxSolid(statusRect, new Color(1f, 1f, 1f, 0.05f));
        if (!string.IsNullOrEmpty(_status))
        {
            GUI.color = new Color(0.6f, 0.9f, 0.6f);
            Text.Anchor = TextAnchor.MiddleLeft;
            Widgets.Label(new Rect(statusRect.x + 6f, statusRect.y, statusRect.width - 12f, statusRect.height), _status);
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prev;
        }

        // -- Content --
        var content = new Rect(inRect.x, tabBar.yMax + 10f, inRect.width, statusRect.y - tabBar.yMax - 16f);
        if (_tab == Tab.Main)
            DrawMain(content);
        else if (_tab == Tab.Pawn)
            DrawPawn(content);
        else if (_tab == Tab.Resources)
            DrawResources(content);
        else if (_tab == Tab.Colony)
            DrawColony(content);
        else
            DrawWorld(content);
    }

    // -- Tab: Main ----------------------------------------------------

    private void DrawMain(Rect rect)
    {
        var list = BeginList(rect, rows: 12, sections: 2);

        DrawSectionLabel(list, "Toggles");

        DoToggle(list, "God Mode Colonists", ref CheatsState.GodMode,
            "Colonists become immune to damage and are constantly healed while enabled.");
        DoToggle(list, "Auto Fill All Needs", ref CheatsState.AutoFillNeeds,
            "Continuously keeps every colonist's needs (food, rest, mood...) at maximum.");
        DoToggle(list, "Auto Remove Bad Memories", ref CheatsState.AutoRemoveBadMemories,
            "Continuously clears negative thoughts and memories from all colonists.");

        DrawSectionLabel(list, "Actions");

        if (DrawButtonRow(list, "Heal All Colonists", "Removes injuries, diseases and bad hediffs from every colonist."))
            SetStatus(CheatActions.ForEachColonist(CheatActions.HealPawn, "Healed"));

        if (DrawButtonRow(list, "Fill All Needs", "Instantly fills every colonist's needs once."))
            SetStatus(CheatActions.ForEachColonist(CheatActions.FillNeeds, "Filled needs for"));

        if (DrawButtonRow(list, "Remove All Bad Memories", "Clears negative thoughts and memories from colonists once."))
            SetStatus(CheatActions.ForEachColonist(CheatActions.RemoveBadMoodEffects, "Removed bad memories from"));

        if (DrawButtonRow(list, "Revive Dead Selected Colonist", "Select a corpse on the map, then press to resurrect it."))
            SetStatus(CheatActions.ReviveSelected());

        if (DrawButtonRow(list, "End All Mental Breaks", "Stops any active mental break on the map."))
            SetStatus(CheatActions.RemoveAllMentalStates());

        if (DrawButtonRow(list, "Clear All Threats", "Removes all hostile pawns and threats from the current map."))
            SetStatus(CheatActions.ClearAllThreats());

        if (DrawDlcButtonRow(list, "Clear All Pollution", "Biotech", "Removes all pollution from the current map."))
            SetStatus(CheatActions.ClearAllPollution());

        if (DrawButtonRow(list, "Clear All Events", "Ends all active map conditions and clears queued incidents."))
            SetStatus(CheatActions.ClearAllEvents());

        EndList();
    }

    // -- Tab: Colony ----------------------------------------------------

    private void DrawColony(Rect rect)
    {
        var list = BeginList(rect, rows: 23, sections: 2);

        DrawSectionLabel(list, "Toggles");

        DoToggle(list, "Instant Build Mode", ref CheatsState.InstantBuild,
            "Blueprints and frames complete instantly while enabled.");
        DoToggle(list, "Unlimited Free Power", ref CheatsState.UnlimitedPower,
            "Every device is powered and batteries stay full - no generators needed.");
        DoToggle(list, "Grow All Crops", ref CheatsState.AutoGrowCrops,
            "Continuously grows all planted crops to maturity.");
        DoToggle(list, "Clean All Filth", ref CheatsState.AutoCleanFilth,
            "Continuously removes all dirt, blood and filth from the map.");
        DoToggle(list, "Repair All Buildings", ref CheatsState.AutoRepairBuildings,
            "Continuously keeps every building at full health.");
        DoToggle(list, "Instant Complete Crafting", ref CheatsState.InstantCraft,
            "Crafting bills being worked on finish instantly.");
        DoToggle(list, "Skills Don't Decay", ref CheatsState.SkillNoDecay,
            "High skills never lose XP over time while enabled.");
        DoToggle(list, "Never Breakup / Divorce", ref CheatsState.NeverBreakup,
            "Colonist couples never break up or divorce while enabled.");
        DoToggle(list, "Building Legendary", ref CheatsState.BuildingLegendary,
            "Everything built or crafted comes out Legendary quality.");

        DrawSectionLabel(list, "Actions");

        if (DrawButtonRow(list, "Finish All Research", "Completes every research project instantly."))
            SetStatus(CheatActions.FinishAllResearch());

        if (DrawDlcButtonRow(list, "Complete All Anomaly Findings", "Anomaly", "Unlocks all anomaly research findings."))
            SetStatus(CheatActions.CompleteAnomalyFindings());

        if (DrawButtonRow(list, "Instant Complete All Crafting", "Finishes all bills currently being worked on once."))
            SetStatus(CheatActions.InstantCompleteCrafting(Find.CurrentMap));

        if (DrawButtonRow(list, "Repair All Buildings", "Restores every building on the map to full health once."))
            SetStatus(CheatActions.RepairAllBuildings());

        if (DrawButtonRow(list, "Grow All Crops", "Instantly grows all planted crops to maturity once."))
            SetStatus(CheatActions.GrowAllCrops());

        if (DrawButtonRow(list, "Remove All Filth", "Removes all dirt, blood and filth from the map once."))
            SetStatus(CheatActions.CleanAllFilth());

        if (DrawButtonRow(list, "Remove All Mountains", "Removes every mountain (thick rock) roof on the map."))
            SetStatus(CheatActions.RemoveMountainRoofs());

        if (DrawButtonRow(list, "Spawn Random Colonist", "Drops a new random colonist near the center of the map."))
            SetStatus(CheatActions.SpawnRandomColonist());

        if (DrawButtonRow(list, "Spawn God Colonist...", "Spawns a colonist in their 20s with maxed skills, best traits and legendary gear."))
            ShowGodColonistMenu();

        if (DrawButtonRow(list, "Spawn Random Prisoner", "Spawns a random pawn already captured as your prisoner."))
            SetStatus(CheatActions.SpawnRandomPrisoner());

        if (DrawDlcButtonRow(list, "Spawn Random Slave", "Ideology", "Spawns a random pawn already enslaved to your colony."))
            SetStatus(CheatActions.SpawnRandomSlave());

        if (DrawButtonRow(list, "Spawn Pawn From Faction...", "Pick a faction and a pawn kind to spawn on the map."))
            ShowFactionPawnMenu();

        EndList();
    }

    private void ShowGodColonistMenu()
    {
        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
        {
            new FloatMenuOption("Male", () => SetStatus(CheatActions.SpawnGodColonist(Gender.Male))),
            new FloatMenuOption("Female", () => SetStatus(CheatActions.SpawnGodColonist(Gender.Female))),
        }));
    }

    private void ShowFactionPawnMenu()
    {
        var factions = Find.FactionManager?.AllFactionsVisible?.Where(f => !f.IsPlayer && !f.defeated).ToList();
        if (factions is null || factions.Count == 0) { SetStatus("No factions found"); return; }

        var options = factions
            .Select(f => new FloatMenuOption(f.Name, () =>
            {
                var kinds = CheatActions.PawnKindsForFaction(f);
                if (kinds.Count == 0) { SetStatus($"No pawn kinds for {f.Name}"); return; }
                var kindOptions = kinds
                    .Select(k => new FloatMenuOption(k.label?.CapitalizeFirst() ?? k.defName,
                        () => SetStatus(CheatActions.SpawnPawnOfFaction(f, k))))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(kindOptions));
            }))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    // -- Tab: Pawn ------------------------------------------------------

    private void DrawPawn(Rect rect)
    {
        var pawn = CheatActions.SelectedPawn();
        if (pawn is null)
        {
            DrawPlaceholder(rect, "Select a pawn or corpse on the map to edit them.");
            return;
        }

        SyncPawnBuffers(pawn);

        bool isColonyPawn = pawn.Faction == Faction.OfPlayer && !pawn.IsPrisonerOfColony && !pawn.IsSlaveOfColony;
        var list = BeginList(rect, rows: isColonyPawn ? 42 : 44, sections: 9);

        if (!isColonyPawn || pawn.Dead)
        {
            DrawSectionLabel(list, "Quick Actions", white: true);
            if (pawn.Dead && DrawButtonRow(list, $"Revive {pawn.LabelShortCap}", "Resurrects this dead pawn."))
                SetStatus(PawnCheats.RevivePawn(pawn));
            if (!isColonyPawn && DrawButtonRow(list, $"Instant Recruit {pawn.LabelShortCap}", "Instantly makes this pawn join your colony."))
                SetStatus(CheatActions.InstantRecruitPawn(pawn));
        }

        DrawSectionLabel(list, $"Identity - {pawn.LabelShortCap}", white: true);

        DrawTextFieldRow(list, "First Name", ref _firstBuf);
        DrawTextFieldRow(list, "Nickname", ref _nickBuf);
        DrawTextFieldRow(list, "Last Name", ref _lastBuf);
        DrawTextFieldRow(list, "Age (bio)", ref _ageBuf);
        DrawTextFieldRow(list, "Age (chrono)", ref _chronoBuf);

        if (DrawButtonRow(list, "Apply Name & Ages"))
            SetStatus(PawnCheats.ApplyIdentity(pawn, _firstBuf, _nickBuf, _lastBuf, _ageBuf, _chronoBuf));

        if (DrawButtonRow(list, $"Childhood: {pawn.story?.Childhood?.title?.CapitalizeFirst() ?? "None"}"))
            ShowBackstoryMenu(pawn, BackstorySlot.Childhood);

        if (DrawButtonRow(list, $"Adulthood: {pawn.story?.Adulthood?.title?.CapitalizeFirst() ?? "None"}"))
            ShowBackstoryMenu(pawn, BackstorySlot.Adulthood);

        if (DrawButtonRow(list, $"Gender: {pawn.gender}"))
            SetStatus(PawnCheats.ToggleGender(pawn));

        DrawSectionLabel(list, "Appearance", white: true);

        if (DrawButtonRow(list, $"Hair: {pawn.story?.hairDef?.label?.CapitalizeFirst() ?? "?"}"))
            ShowHairMenu(pawn);

        if (DrawDlcButtonRow(list, $"Beard: {pawn.style?.beardDef?.label?.CapitalizeFirst() ?? "None"}", "Ideology"))
            ShowBeardMenu(pawn);

        if (DrawButtonRow(list, $"Body Type: {pawn.story?.bodyType?.defName ?? "?"}"))
            ShowBodyTypeMenu(pawn);

        if (DrawButtonRow(list, $"Head Type: {pawn.story?.headType?.defName ?? "?"}"))
            ShowHeadTypeMenu(pawn);

        if (DrawDlcButtonRow(list, "Add Tattoo...", "Ideology"))
            ShowTattooMenu(pawn);

        if (DrawDlcButtonRow(list, "Remove Tattoos", "Ideology"))
            SetStatus(PawnCheats.RemoveTattoos(pawn));

        DrawSectionLabel(list, "Health", white: true);

        if (DrawButtonRow(list, "Heal Pawn"))
        {
            CheatActions.HealPawn(pawn);
            SetStatus($"Healed {pawn.LabelShortCap}");
        }

        if (DrawButtonRow(list, "Revive Pawn"))
            SetStatus(PawnCheats.RevivePawn(pawn));

        if (DrawButtonRow(list, "Down Pawn"))
            SetStatus(PawnCheats.DownPawn(pawn));

        if (DrawButtonRow(list, "Anesthetize Pawn"))
            SetStatus(PawnCheats.AnesthetizePawn(pawn));

        if (DrawButtonRow(list, "Kill Pawn"))
            SetStatus(PawnCheats.KillPawn(pawn));

        DrawSectionLabel(list, "Skills", white: true);

        if (DrawButtonRow(list, "Max All Skills"))
            SetStatus(PawnCheats.MaxAllSkills(pawn));

        if (DrawButtonRow(list, "Set All Double Flame"))
            SetStatus(PawnCheats.SetAllPassions(pawn, Passion.Major));

        if (DrawButtonRow(list, "Set Skill Level..."))
            ShowSkillLevelMenu(pawn);

        if (DrawButtonRow(list, "Set Skill Passion..."))
            ShowSkillPassionMenu(pawn);

        DrawSectionLabel(list, "Traits", white: true);

        if (DrawButtonRow(list, "Add Trait... (stacking allowed)"))
            ShowAddTraitMenu(pawn);

        if (DrawButtonRow(list, "Remove Trait..."))
            ShowRemoveTraitMenu(pawn);

        DrawSectionLabel(list, "Abilities & Status", white: true);

        if (DrawDlcButtonRow(list, $"Add Psylink Level (now {pawn.GetPsylinkLevel()})", "Royalty"))
            SetStatus(PawnCheats.AddPsylinkLevel(pawn));

        if (DrawDlcButtonRow(list, "Learn All Psycasts", "Royalty"))
            SetStatus(PawnCheats.LearnAllPsycasts(pawn));

        if (DrawDlcButtonRow(list, "Change Title...", "Royalty"))
            ShowTitleMenu(pawn);

        if (DrawDlcButtonRow(list, $"Ideology: {pawn.Ideo?.name ?? "None"}", "Ideology"))
            ShowIdeoMenu(pawn);

        if (DrawDlcButtonRow(list, PawnCheats.IsMutant(pawn) ? "Change Back From Mutant" : "Transform Into Mutant...", "Anomaly"))
        {
            if (PawnCheats.IsMutant(pawn))
                SetStatus(PawnCheats.RevertMutant(pawn));
            else
                ShowMutantMenu(pawn);
        }

        if (DrawDlcButtonRow(list, "Make Slave", "Ideology"))
            SetStatus(PawnCheats.MakeSlave(pawn));

        if (DrawButtonRow(list, "Make Prisoner"))
            SetStatus(PawnCheats.MakePrisoner(pawn));

        DrawSectionLabel(list, "Relationships", white: true);

        if (DrawButtonRow(list, "Force Lover...", "Pick a colonist to become this pawn's lover."))
            ShowRelationMenu(pawn, married: false);

        if (DrawButtonRow(list, "Force Marriage...", "Pick a colonist to become this pawn's spouse."))
            ShowRelationMenu(pawn, married: true);

        if (DrawButtonRow(list, "Force Breakup / Divorce", "Removes all lover, fiance and spouse relations."))
            SetStatus(PawnCheats.ForceBreakup(pawn));

        DrawSectionLabel(list, "Needs, Memory & Value", white: true);

        if (DrawButtonRow(list, "Edit Needs..."))
            ShowNeedsMenu(pawn);

        if (DrawButtonRow(list, "Edit Memories..."))
            ShowMemoriesMenu(pawn);

        if (DrawButtonRow(list, "Boost Sell Value", "Maxes skills and passions and heals the pawn to raise their market value."))
            SetStatus(PawnCheats.BoostSellValue(pawn));

        EndList();
    }

    private void SyncPawnBuffers(Pawn pawn)
    {
        if (_pawnId == pawn.thingIDNumber) return;
        _pawnId = pawn.thingIDNumber;

        var triple = pawn.Name as NameTriple;
        _firstBuf = triple?.First ?? pawn.Name?.ToStringShort ?? "";
        _nickBuf = triple?.Nick ?? "";
        _lastBuf = triple?.Last ?? "";
        _ageBuf = pawn.ageTracker.AgeBiologicalYears.ToString();
        _chronoBuf = pawn.ageTracker.AgeChronologicalYears.ToString();
    }

    private void ShowBackstoryMenu(Pawn pawn, BackstorySlot slot)
    {
        if (pawn.story is null) { SetStatus("Pawn has no story tracker"); return; }

        var options = DefDatabase<BackstoryDef>.AllDefsListForReading
            .Where(b => b.slot == slot)
            .OrderBy(b => b.title ?? b.defName)
            .Select(b => new FloatMenuOption(b.title?.CapitalizeFirst() ?? b.defName,
                () => SetStatus(PawnCheats.SetBackstory(pawn, slot, b))))
            .ToList();
        if (options.Count == 0) { SetStatus("No backstories found"); return; }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowHairMenu(Pawn pawn)
    {
        if (pawn.story is null) { SetStatus("Pawn has no story tracker"); return; }

        var options = DefDatabase<HairDef>.AllDefsListForReading
            .OrderBy(d => d.label ?? d.defName)
            .Select(def => new FloatMenuOption(def.label?.CapitalizeFirst() ?? def.defName,
                () => SetStatus(PawnCheats.SetHair(pawn, def))))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowBeardMenu(Pawn pawn)
    {
        if (pawn.style is null) { SetStatus("Pawn has no style tracker"); return; }

        var options = DefDatabase<BeardDef>.AllDefsListForReading
            .OrderBy(d => d.label ?? d.defName)
            .Select(def => new FloatMenuOption(def.label?.CapitalizeFirst() ?? def.defName,
                () => SetStatus(PawnCheats.SetBeard(pawn, def))))
            .ToList();
        if (options.Count == 0) { SetStatus("No beards found"); return; }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowTattooMenu(Pawn pawn)
    {
        if (pawn.style is null) { SetStatus("Pawn has no style tracker"); return; }

        var options = DefDatabase<TattooDef>.AllDefsListForReading
            .OrderBy(d => d.label ?? d.defName)
            .Select(def => new FloatMenuOption($"{def.label?.CapitalizeFirst() ?? def.defName} ({def.tattooType})",
                () => SetStatus(PawnCheats.SetTattoo(pawn, def))))
            .ToList();
        if (options.Count == 0) { SetStatus("No tattoos found"); return; }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowBodyTypeMenu(Pawn pawn)
    {
        if (pawn.story is null) { SetStatus("Pawn has no story tracker"); return; }

        var options = DefDatabase<BodyTypeDef>.AllDefsListForReading
            .OrderBy(d => d.defName)
            .Select(def => new FloatMenuOption(def.defName, () => SetStatus(PawnCheats.SetBodyType(pawn, def))))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowHeadTypeMenu(Pawn pawn)
    {
        if (pawn.story is null) { SetStatus("Pawn has no story tracker"); return; }

        var options = DefDatabase<HeadTypeDef>.AllDefsListForReading
            .OrderBy(d => d.defName)
            .Select(def => new FloatMenuOption(
                def.gender == Gender.None ? def.defName : $"{def.defName} ({def.gender})",
                () => SetStatus(PawnCheats.SetHeadType(pawn, def))))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowSkillLevelMenu(Pawn pawn)
    {
        if (pawn.skills is null) { SetStatus("Pawn has no skills"); return; }

        var skillOptions = pawn.skills.skills
            .Select(rec => new FloatMenuOption($"{rec.def.skillLabel.CapitalizeFirst()} - level {rec.Level}", () =>
            {
                var levels = new List<FloatMenuOption>();
                for (int lvl = 0; lvl <= 20; lvl++)
                {
                    int captured = lvl;
                    levels.Add(new FloatMenuOption($"Level {captured}",
                        () => SetStatus(PawnCheats.SetSkillLevel(rec, captured))));
                }
                Find.WindowStack.Add(new FloatMenu(levels));
            }))
            .ToList();

        Find.WindowStack.Add(new FloatMenu(skillOptions));
    }

    private void ShowSkillPassionMenu(Pawn pawn)
    {
        if (pawn.skills is null) { SetStatus("Pawn has no skills"); return; }

        var skillOptions = pawn.skills.skills
            .Select(rec => new FloatMenuOption($"{rec.def.skillLabel.CapitalizeFirst()} - {PawnCheats.PassionLabel(rec.passion)}", () =>
            {
                var passions = new List<FloatMenuOption>
                {
                    new FloatMenuOption("None", () => SetStatus(PawnCheats.SetSkillPassion(rec, Passion.None))),
                    new FloatMenuOption("Minor (one flame)", () => SetStatus(PawnCheats.SetSkillPassion(rec, Passion.Minor))),
                    new FloatMenuOption("Major (double flame)", () => SetStatus(PawnCheats.SetSkillPassion(rec, Passion.Major))),
                };
                Find.WindowStack.Add(new FloatMenu(passions));
            }))
            .ToList();

        Find.WindowStack.Add(new FloatMenu(skillOptions));
    }

    private void ShowAddTraitMenu(Pawn pawn)
    {
        if (pawn.story?.traits is null) { SetStatus("Pawn has no traits tracker"); return; }

        var options = new List<FloatMenuOption>();
        foreach (var def in DefDatabase<TraitDef>.AllDefsListForReading)
        {
            foreach (var dd in def.degreeDatas)
            {
                var d = dd;
                options.Add(new FloatMenuOption(d.label.CapitalizeFirst(),
                    () => SetStatus(PawnCheats.AddTrait(pawn, def, d.degree))));
            }
        }
        options.SortBy(o => o.Label);
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowRemoveTraitMenu(Pawn pawn)
    {
        var traits = pawn.story?.traits?.allTraits;
        if (traits is null || traits.Count == 0) { SetStatus("Pawn has no traits"); return; }

        var options = traits.ToList()
            .Select(t => new FloatMenuOption(t.LabelCap, () => SetStatus(PawnCheats.RemoveTrait(pawn, t))))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowTitleMenu(Pawn pawn)
    {
        if (!ModsConfig.RoyaltyActive) { SetStatus("Royalty DLC not active"); return; }

        var factions = Find.FactionManager?.AllFactionsVisible?
            .Where(f => !f.IsPlayer && f.def.HasRoyalTitles)
            .ToList();
        if (factions is null || factions.Count == 0) { SetStatus("No factions with royal titles"); return; }

        var options = factions
            .Select(f => new FloatMenuOption(f.Name, () =>
            {
                var titles = f.def.RoyalTitlesAllInSeniorityOrderForReading
                    .Select(t => new FloatMenuOption(t.GetLabelCapFor(pawn),
                        () => SetStatus(PawnCheats.SetTitle(pawn, f, t))))
                    .ToList();
                titles.Insert(0, new FloatMenuOption("(No title)", () => SetStatus(PawnCheats.SetTitle(pawn, f, null))));
                Find.WindowStack.Add(new FloatMenu(titles));
            }))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowIdeoMenu(Pawn pawn)
    {
        if (!ModsConfig.IdeologyActive) { SetStatus("Ideology DLC not active"); return; }
        if (pawn.ideo is null) { SetStatus("Pawn has no ideology tracker"); return; }

        var options = Find.IdeoManager.IdeosListForReading
            .Select(ideo => new FloatMenuOption(ideo.name, () => SetStatus(PawnCheats.SetIdeology(pawn, ideo))))
            .ToList();

        if (options.Count == 0) { SetStatus("No ideologies in this game"); return; }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowMutantMenu(Pawn pawn)
    {
        var options = PawnCheats.MutantOptions(pawn, SetStatus);
        if (options.Count == 0) { SetStatus("No mutant types available"); return; }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowRelationMenu(Pawn pawn, bool married)
    {
        var candidates = Find.CurrentMap?.mapPawns?.FreeColonists?
            .Where(p => p != pawn && p.RaceProps.Humanlike)
            .ToList();
        if (candidates is null || candidates.Count == 0) { SetStatus("No other colonists available"); return; }

        var options = candidates
            .Select(other => new FloatMenuOption(other.LabelShortCap,
                () => SetStatus(PawnCheats.ForceRelation(pawn, other, married))))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowNeedsMenu(Pawn pawn)
    {
        var needs = pawn.needs?.AllNeeds;
        if (needs is null || needs.Count == 0) { SetStatus("Pawn has no needs"); return; }

        var options = needs
            .Select(need => new FloatMenuOption($"{need.LabelCap} - {need.CurLevelPercentage:P0}", () =>
            {
                var levels = new List<FloatMenuOption>();
                foreach (int pct in new[] { 0, 25, 50, 75, 100 })
                {
                    int captured = pct;
                    levels.Add(new FloatMenuOption($"{captured}%",
                        () => SetStatus(PawnCheats.SetNeed(need, captured / 100f))));
                }
                Find.WindowStack.Add(new FloatMenu(levels));
            }))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowMemoriesMenu(Pawn pawn)
    {
        var memories = pawn.needs?.mood?.thoughts?.memories?.Memories;
        if (memories is null || memories.Count == 0) { SetStatus("Pawn has no memories"); return; }

        var options = memories.ToList()
            .Select(m => new FloatMenuOption($"{m.LabelCap} ({m.MoodOffset():+0;-0;0})",
                () => SetStatus(PawnCheats.RemoveMemory(pawn, m))))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    // -- Tab: Resources ---------------------------------------------------

    private void DrawResources(Rect rect)
    {
        var list = BeginList(rect, rows: 9, sections: 2);

        DrawSectionLabel(list, "Items (pick amount) - includes mod items");

        if (DrawButtonRow(list, "Spawn Resource...", "Pick a resource and an amount to drop at map center."))
            ShowSpawnAmountMenu(CheatActions.ResourceDefs(), "resource");

        if (DrawButtonRow(list, "Spawn Textile / Leather...", "Pick a textile or leather and an amount to spawn."))
            ShowSpawnAmountMenu(CheatActions.TextileDefs(), "textile");

        if (DrawButtonRow(list, "Spawn Medicine...", "Pick a medicine type and an amount to spawn."))
            ShowSpawnAmountMenu(CheatActions.MedicineDefs(), "medicine");

        if (DrawButtonRow(list, "Spawn Food...", "Pick a food item and an amount to spawn."))
            ShowSpawnAmountMenu(CheatActions.FoodDefs(), "food");

        if (DrawButtonRow(list, "Spawn Drug...", "Pick a drug and an amount to spawn."))
            ShowSpawnAmountMenu(CheatActions.DrugDefs(), "drug");

        DrawSectionLabel(list, "Gear & Buildings (pick quality)");

        if (DrawButtonRow(list, "Spawn Weapon...", "Pick a weapon and its quality to spawn."))
            ShowSpawnQualityMenu(CheatActions.WeaponDefs(), "weapon");

        if (DrawButtonRow(list, "Spawn Apparel...", "Pick apparel and its quality to spawn."))
            ShowSpawnQualityMenu(CheatActions.ApparelDefs(), "apparel");

        if (DrawButtonRow(list, "Spawn Structure...", "Pick a structure/building and its quality to spawn."))
            ShowSpawnQualityMenu(CheatActions.StructureDefs(), "structure");

        EndList();
    }

    private static readonly int[] SpawnAmounts = { 1, 5, 10, 25, 50, 100, 500, 1000 };

    private void ShowSpawnAmountMenu(List<ThingDef> defs, string kind)
    {
        if (defs is null || defs.Count == 0) { SetStatus($"No {kind}s found"); return; }

        var options = defs
            .Select(def => new FloatMenuOption(def.LabelCap, () =>
            {
                var amounts = SpawnAmounts
                    .Select(n => new FloatMenuOption($"x{n}", () => SetStatus(CheatActions.SpawnThing(def, n))))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(amounts));
            }))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowSpawnQualityMenu(List<ThingDef> defs, string kind)
    {
        if (defs is null || defs.Count == 0) { SetStatus($"No {kind}s found"); return; }

        var options = defs
            .Select(def => new FloatMenuOption(def.LabelCap, () =>
            {
                var qualities = ((QualityCategory[])Enum.GetValues(typeof(QualityCategory)))
                    .Select(q => new FloatMenuOption(QualityUtility.GetLabel(q).CapitalizeFirst(),
                        () => SetStatus(CheatActions.SpawnThingWithQuality(def, q))))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(qualities));
            }))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    // -- Tab: World -----------------------------------------------------

    private void DrawWorld(Rect rect)
    {
        var list = BeginList(rect, rows: 21, sections: 2);

        DrawSectionLabel(list, "Toggles");

        DoToggle(list, "Weather Clear", ref CheatsState.WeatherClear,
            "Keeps the weather permanently clear while enabled.");

        if (DrawToggleRow(list, "Always Day", CheatsState.AlwaysDay, "Forces permanent daylight while enabled."))
        {
            CheatsState.AlwaysDay = !CheatsState.AlwaysDay;
            if (CheatsState.AlwaysDay) CheatsState.AlwaysNight = false;
            PlayToggleSound(CheatsState.AlwaysDay);
            SetStatus(CheatsState.AlwaysDay ? "Always Day enabled" : "Always Day disabled");
        }

        if (DrawToggleRow(list, "Always Night", CheatsState.AlwaysNight, "Forces permanent night while enabled."))
        {
            CheatsState.AlwaysNight = !CheatsState.AlwaysNight;
            if (CheatsState.AlwaysNight) CheatsState.AlwaysDay = false;
            PlayToggleSound(CheatsState.AlwaysNight);
            SetStatus(CheatsState.AlwaysNight ? "Always Night enabled" : "Always Night disabled");
        }

        DoToggle(list, "Fast Caravans", ref CheatsState.FastCaravans,
            "Caravans travel across the world map almost instantly.");
        DoToggle(list, "No Raids or Bad Events", ref CheatsState.NoBadEvents,
            "Blocks raids and negative random events. Manually triggered events still fire.");
        DoToggle(list, "Fish Never Go Down", ref CheatsState.FishNeverDeplete,
            CheatsState.FishPatchApplied
                ? "Fish populations never drop when pawns are fishing."
                : "Requires the Odyssey DLC (fishing not found in this game version).");

        if (DrawToggleRow(list, "Force Weather", CheatsState.ForceWeather, "Locks the weather to the current one."))
        {
            if (CheatsState.ForceWeather)
            {
                CheatsState.ForceWeather = false;
                CheatsState.ForcedWeather = null;
                PlayToggleSound(false);
                SetStatus("Weather unlocked");
            }
            else
            {
                var cur = Find.CurrentMap?.weatherManager?.curWeather;
                if (cur is null)
                {
                    SetStatus("No map loaded");
                }
                else
                {
                    CheatsState.ForceWeather = true;
                    CheatsState.ForcedWeather = cur;
                    PlayToggleSound(true);
                    SetStatus($"Weather locked to {cur.label?.CapitalizeFirst() ?? cur.defName}");
                }
            }
        }

        DrawSectionLabel(list, "Actions");

        if (DrawButtonRow(list, "Set Day / Night...", "Jump the clock to a chosen time of day."))
            ShowTimeMenu();

        if (DrawButtonRow(list, "Instant Teleport All Caravans", "Moves every travelling caravan straight to its destination."))
            SetStatus(CheatActions.TeleportAllCaravans());

        if (DrawButtonRow(list, "Faction Relations..."))
            ShowFactionRelationsMenu();

        if (DrawButtonRow(list, "Call Trade Caravan..."))
            ShowTradeCaravanMenu();

        if (DrawButtonRow(list, "Return All Trade Caravans"))
            SetStatus(CheatActions.SendTradeCaravansAway());

        string curWeather = Find.CurrentMap?.weatherManager?.curWeather?.label?.CapitalizeFirst() ?? "?";
        if (DrawButtonRow(list, $"Forced Weather Condition: {curWeather}", "Pick a weather to transition to (locks if Force Weather is on)."))
            ShowWeatherMenu();

        if (DrawButtonRow(list, "Spawn Raid..."))
            ShowRaidMenu();

        if (DrawDlcButtonRow(list, "Spawn Mech Cluster", "Royalty"))
            SetStatus(CheatActions.SpawnMechCluster());

        if (DrawButtonRow(list, "Remove All Raids / Mechs", "Clears hostile raiders and mechanoids from the map."))
        {
            var a = CheatActions.EndAllRaids();
            var b = CheatActions.DestroyAllMechs();
            SetStatus($"{a}; {b}");
        }

        if (DrawButtonRow(list, "Remove All Enemy Structures"))
            SetStatus(CheatActions.RemoveEnemyStructures());

        if (DrawButtonRow(list, "End All Conditions"))
            SetStatus(CheatActions.EndAllConditions());

        if (DrawButtonRow(list, "Trigger Event..."))
            ShowEventMenu();

        EndList();
    }

    private void ShowTimeMenu()
    {
        Find.WindowStack.Add(new FloatMenu(new List<FloatMenuOption>
        {
            new FloatMenuOption("Dawn (6:00)", () => SetStatus(CheatActions.SetHour(6))),
            new FloatMenuOption("Noon (12:00)", () => SetStatus(CheatActions.SetHour(12))),
            new FloatMenuOption("Dusk (18:00)", () => SetStatus(CheatActions.SetHour(18))),
            new FloatMenuOption("Midnight (0:00)", () => SetStatus(CheatActions.SetHour(0))),
        }));
    }

    private void ShowFactionRelationsMenu()
    {
        var factions = Find.FactionManager?.AllFactionsVisibleInViewOrder?.Where(f => !f.IsPlayer).ToList();
        if (factions is null || factions.Count == 0) { SetStatus("No factions found"); return; }

        var options = factions
            .Select(f => new FloatMenuOption($"{f.Name} - {f.PlayerGoodwill}", () =>
            {
                var adjustments = new List<FloatMenuOption>
                {
                    new FloatMenuOption("+10 Goodwill", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, 10))),
                    new FloatMenuOption("+25 Goodwill", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, 25))),
                    new FloatMenuOption("+50 Goodwill", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, 50))),
                    new FloatMenuOption("-10 Goodwill", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, -10))),
                    new FloatMenuOption("-25 Goodwill", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, -25))),
                    new FloatMenuOption("-50 Goodwill", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, -50))),
                    new FloatMenuOption("Make Ally", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, 100 - f.PlayerGoodwill))),
                    new FloatMenuOption("Make Neutral", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, -f.PlayerGoodwill))),
                    new FloatMenuOption("Make Hostile", () => SetStatus(CheatActions.AdjustFactionGoodwill(f, -100 - f.PlayerGoodwill))),
                };
                Find.WindowStack.Add(new FloatMenu(adjustments));
            }))
            .ToList();

        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowTradeCaravanMenu()
    {
        var factions = Find.FactionManager?.AllFactionsVisible?
            .Where(f => !f.IsPlayer && !f.defeated && !f.temporary
                && !f.HostileTo(Faction.OfPlayer)
                && f.def.humanlikeFaction
                && f.def.caravanTraderKinds != null && f.def.caravanTraderKinds.Count > 0)
            .ToList();
        if (factions is null || factions.Count == 0) { SetStatus("No friendly trading factions"); return; }

        var options = factions
            .Select(f => new FloatMenuOption($"{f.Name} - {f.PlayerGoodwill}", () =>
            {
                var kinds = f.def.caravanTraderKinds
                    .Select(kind => new FloatMenuOption(kind.label?.CapitalizeFirst() ?? kind.defName,
                        () => SetStatus(CheatActions.CallTradeCaravan(f, kind))))
                    .ToList();
                Find.WindowStack.Add(new FloatMenu(kinds));
            }))
            .ToList();

        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowWeatherMenu()
    {
        var options = DefDatabase<WeatherDef>.AllDefsListForReading
            .OrderBy(d => d.label ?? d.defName)
            .Select(def => new FloatMenuOption(def.label?.CapitalizeFirst() ?? def.defName,
                () => SetStatus(CheatActions.SetWeather(def))))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowRaidMenu()
    {
        var factions = Find.FactionManager?.AllFactions?
            .Where(f => !f.IsPlayer && !f.defeated && f.HostileTo(Faction.OfPlayer))
            .ToList();
        if (factions is null || factions.Count == 0) { SetStatus("No hostile factions to raid with"); return; }

        var options = factions
            .Select(f => new FloatMenuOption(f.Name, () => SetStatus(CheatActions.SpawnRaid(f))))
            .ToList();
        Find.WindowStack.Add(new FloatMenu(options));
    }

    private void ShowEventMenu()
    {
        var options = DefDatabase<IncidentDef>.AllDefsListForReading
            .OrderBy(d => d.label ?? d.defName)
            .Select(def => new FloatMenuOption(def.label?.CapitalizeFirst() ?? def.defName,
                () => SetStatus(CheatActions.TriggerIncidentDef(def))))
            .ToList();
        if (options.Count == 0) { SetStatus("No incidents found"); return; }
        Find.WindowStack.Add(new FloatMenu(options));
    }

    // -- Placeholder ------------------------------------------------------

    private static void DrawPlaceholder(Rect rect, string message = "Nothing here yet.")
    {
        var prev = GUI.color;
        GUI.color = new Color(1f, 1f, 1f, 0.35f);
        Text.Anchor = TextAnchor.MiddleCenter;
        Widgets.Label(rect, message);
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = prev;
    }

    // -- List helpers ---------------------------------------------------

    private const float RowHeight = 34f;
    private const float RowGap = 6f;
    private const float SectionHeight = 22f;
    private Rect _viewRect;
    private float _y;
    private bool _scrollOpen;

    private Rect BeginList(Rect outRect, int rows, int sections)
    {
        float viewHeight = rows * (RowHeight + RowGap) + sections * (SectionHeight + RowGap);
        _viewRect = new Rect(0f, 0f, outRect.width - 20f, Math.Max(viewHeight, outRect.height));
        Widgets.BeginScrollView(outRect, ref _scroll, _viewRect);
        _scrollOpen = true;
        _y = 0f;
        return _viewRect;
    }

    private void EndList()
    {
        if (!_scrollOpen) return;
        Widgets.EndScrollView();
        _scrollOpen = false;
    }

    private Rect NextRow(Rect list, float height)
    {
        var r = new Rect(list.x, list.y + _y, list.width, height);
        _y += height + RowGap;
        return r;
    }

    private void DrawSectionLabel(Rect list, string label, bool white = false)
    {
        var r = NextRow(list, SectionHeight);
        var prev = GUI.color;
        GUI.color = white ? Color.white : new Color(1f, 1f, 1f, 0.5f);
        Text.Anchor = TextAnchor.LowerLeft;
        Widgets.Label(r, label.ToUpperInvariant());
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = white ? new Color(1f, 1f, 1f, 0.35f) : new Color(1f, 1f, 1f, 0.15f);
        Widgets.DrawLineHorizontal(r.x, r.yMax + 1f, r.width);
        GUI.color = prev;
    }

    private bool DrawButtonRow(Rect list, string label, string tooltip = null)
    {
        var r = NextRow(list, RowHeight);
        if (!string.IsNullOrEmpty(tooltip) && Mouse.IsOver(r))
            TooltipHandler.TipRegion(r, tooltip);
        return Widgets.ButtonText(r, label);
    }

    private static bool DlcActive(string dlc)
    {
        try
        {
            switch (dlc)
            {
                case "Royalty": return ModsConfig.RoyaltyActive;
                case "Ideology": return ModsConfig.IdeologyActive;
                case "Biotech": return ModsConfig.BiotechActive;
                case "Anomaly": return ModsConfig.AnomalyActive;
                default: return true;
            }
        }
        catch { return false; }
    }

    /// <summary>Draws a normal button, or a red locked one when the required DLC is missing.</summary>
    private bool DrawDlcButtonRow(Rect list, string label, string dlc, string tooltip = null)
    {
        if (DlcActive(dlc))
            return DrawButtonRow(list, label, tooltip);

        var r = NextRow(list, RowHeight);
        if (Mouse.IsOver(r))
            TooltipHandler.TipRegion(r, $"Requires the {dlc} DLC to be installed and active.");
        var prev = GUI.color;
        GUI.color = new Color(1f, 0.3f, 0.3f);
        Widgets.ButtonText(r, $"{label} - requires {dlc} DLC", drawBackground: true, doMouseoverSound: false, active: false);
        GUI.color = prev;
        return false;
    }

    private void DrawTextFieldRow(Rect list, string label, ref string buffer)
    {
        var r = NextRow(list, RowHeight);
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(r.x, r.y, 110f, r.height), label);
        Text.Anchor = TextAnchor.UpperLeft;
        buffer = Widgets.TextField(new Rect(r.x + 116f, r.y + 4f, r.width - 116f, r.height - 8f), buffer ?? "");
    }

    private bool DrawToggleRow(Rect list, string label, bool active, string tooltip = null)
    {
        var r = NextRow(list, RowHeight);

        Widgets.DrawOptionBackground(r, active);
        if (Mouse.IsOver(r))
        {
            Widgets.DrawHighlight(r);
            if (!string.IsNullOrEmpty(tooltip))
                TooltipHandler.TipRegion(r, tooltip);
        }

        var prev = GUI.color;
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(r.x + 10f, r.y, r.width - 70f, r.height), label);

        GUI.color = active ? OnColor : OffColor;
        Text.Anchor = TextAnchor.MiddleRight;
        Widgets.Label(new Rect(r.xMax - 50f, r.y, 40f, r.height), active ? "ON" : "OFF");
        Text.Anchor = TextAnchor.UpperLeft;
        GUI.color = prev;

        return Widgets.ButtonInvisible(r);
    }

    /// <summary>Standard toggle handling: flips the flag, plays a sound and reports status.</summary>
    private void DoToggle(Rect list, string label, ref bool flag, string tooltip = null)
    {
        if (!DrawToggleRow(list, label, flag, tooltip)) return;
        flag = !flag;
        PlayToggleSound(flag);
        SetStatus(flag ? $"{label} enabled" : $"{label} disabled");
    }

    private static void PlayToggleSound(bool on) =>
        SoundStarter.PlayOneShotOnCamera(on ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff, null);

    private void SetStatus(string status) => _status = status;
}
