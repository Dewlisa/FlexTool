using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FlexTool.KeepItTogetherMod;

/// <summary>
/// Injects the "Relations" main tab button at startup so no Def XML is needed.
/// </summary>
[StaticConstructorOnStartup]
public static class KeepItTogetherInit
{
    static KeepItTogetherInit()
    {
        try
        {
            var def = new MainButtonDef
            {
                defName = "FlexTool_RelationshipHealth",
                label = "relations",
                description = "Relationship Health: shows how close every colonist couple is to a breaking point and lets you force reconciliation.",
                tabWindowClass = typeof(MainTabWindow_RelationshipHealth),
                workerClass = typeof(MainButtonWorker_ToggleTab),
                order = 45, // between Social-style tabs and the right-side tabs
                validWithoutMap = false,
            };
            def.PostLoad();
            DefDatabase<MainButtonDef>.Add(def);
            Log.Message("[FlexTool Keep It Together] Initialized - Relationship Health tab added.");
        }
        catch (Exception ex)
        {
            Log.Error("[FlexTool Keep It Together] Failed to initialize: " + ex);
        }
    }
}

/// <summary>A colonist couple and its computed relationship health.</summary>
public readonly struct Couple
{
    public readonly Pawn A;
    public readonly Pawn B;
    public readonly PawnRelationDef Relation;

    public Couple(Pawn a, Pawn b, PawnRelationDef relation)
    {
        // Stable ordering so the pair key is identical regardless of who is found first.
        if (a.thingIDNumber <= b.thingIDNumber) { A = a; B = b; }
        else { A = b; B = a; }
        Relation = relation;
    }

    public string Key => A.thingIDNumber + "_" + B.thingIDNumber;

    /// <summary>0..1 health based on the couple's mutual opinion (-100..100 averaged).</summary>
    public float Health
    {
        get
        {
            float aToB = A.relations?.OpinionOf(B) ?? 0;
            float bToA = B.relations?.OpinionOf(A) ?? 0;
            float avg = (aToB + bToA) / 2f;
            return Mathf.Clamp01((avg + 100f) / 200f);
        }
    }

    /// <summary>Finds every lover/fiance/spouse pair among free colonists on all maps.</summary>
    public static List<Couple> FindAll()
    {
        var result = new List<Couple>();
        var seen = new HashSet<string>();

        foreach (var map in Find.Maps)
        {
            foreach (var pawn in map.mapPawns.FreeColonists)
            {
                if (pawn.relations is null) continue;

                foreach (var rel in pawn.relations.DirectRelations)
                {
                    if (rel.def != PawnRelationDefOf.Lover
                        && rel.def != PawnRelationDefOf.Fiance
                        && rel.def != PawnRelationDefOf.Spouse) continue;

                    var other = rel.otherPawn;
                    if (other is null || other.Dead || !other.IsFreeColonist) continue;

                    var couple = new Couple(pawn, other, rel.def);
                    if (seen.Add(couple.Key))
                        result.Add(couple);
                }
            }
        }

        return result;
    }
}

/// <summary>
/// Stores which couples have Forced Reconciliation enabled (saved with the game)
/// and periodically pushes reconciling couples together during recreation.
/// </summary>
public class KeepItTogetherComponent : GameComponent
{
    /// <summary>Health below this fraction counts as the danger zone.</summary>
    public const float DangerThreshold = 0.4f;

    /// <summary>Health at which forced reconciliation automatically switches off.</summary>
    public const float RecoveredThreshold = 0.55f;

    private List<string> _forcedCouples = new List<string>();

    public KeepItTogetherComponent(Game game)
    {
    }

    public static KeepItTogetherComponent Get() =>
        Current.Game?.GetComponent<KeepItTogetherComponent>();

    public bool IsForced(string key) => _forcedCouples.Contains(key);

    public void SetForced(string key, bool on)
    {
        if (on && !_forcedCouples.Contains(key)) _forcedCouples.Add(key);
        else if (!on) _forcedCouples.Remove(key);
    }

    public override void ExposeData()
    {
        base.ExposeData();
        Scribe_Collections.Look(ref _forcedCouples, "flexToolForcedCouples", LookMode.Value);
        _forcedCouples ??= new List<string>();
    }

    public override void GameComponentTick()
    {
        try
        {
            if (_forcedCouples.Count == 0) return;
            if (Find.TickManager.TicksGame % 1200 != 0) return; // roughly every half in-game hour

            var couples = Couple.FindAll();
            for (int i = _forcedCouples.Count - 1; i >= 0; i--)
            {
                string key = _forcedCouples[i];
                var couple = couples.FirstOrDefault(c => c.Key == key);
                if (couple.A is null)
                {
                    _forcedCouples.RemoveAt(i); // couple no longer exists
                    continue;
                }

                if (couple.Health >= RecoveredThreshold)
                {
                    _forcedCouples.RemoveAt(i);
                    Messages.Message(
                        $"{couple.A.LabelShortCap} and {couple.B.LabelShortCap} are out of the danger zone - forced reconciliation ended.",
                        couple.A, MessageTypeDefOf.PositiveEvent, historical: false);
                    continue;
                }

                TryReconcile(couple.A, couple.B);
            }
        }
        catch { }
    }

    /// <summary>
    /// During free time, walks the couple toward each other; once close,
    /// forces a deep talk so the opinion memories rebuild their bond.
    /// </summary>
    private static void TryReconcile(Pawn a, Pawn b)
    {
        if (!CanParticipate(a) || !CanParticipate(b)) return;
        if (a.Map != b.Map) return;

        if (a.Position.DistanceTo(b.Position) <= 10f)
        {
            try { a.interactions?.TryInteractWith(b, InteractionDefOf.DeepTalk); }
            catch { }
            return;
        }

        SendTo(a, b);
        SendTo(b, a);
    }

    private static bool CanParticipate(Pawn p)
    {
        if (p is null || p.Dead || !p.Spawned || p.Downed || p.Drafted) return false;
        if (!p.Awake() || p.InMentalState) return false;

        // Only claim their free time: never interrupt work or sleep schedule.
        var assignment = p.timetable?.CurrentAssignment;
        if (assignment == TimeAssignmentDefOf.Work || assignment == TimeAssignmentDefOf.Sleep) return false;

        return true;
    }

    private static void SendTo(Pawn walker, Pawn target)
    {
        try
        {
            if (walker.CurJobDef == JobDefOf.Goto) return; // already on the way
            if (walker.CurJob != null && !walker.jobs.IsCurrentJobPlayerInterruptible()) return;

            var job = JobMaker.MakeJob(JobDefOf.Goto, target.Position);
            job.locomotionUrgency = LocomotionUrgency.Jog;
            walker.jobs.StartJob(job, JobCondition.InterruptForced);
        }
        catch { }
    }
}

/// <summary>
/// The Relationship Health tab: one row per couple with a health bar and,
/// when the bar is in the danger zone, a Forced Reconciliation toggle.
/// </summary>
public class MainTabWindow_RelationshipHealth : MainTabWindow
{
    private const float RowHeight = 46f;
    private const float RowGap = 6f;

    private Vector2 _scroll;

    public override Vector2 RequestedTabSize => new Vector2(620f, 420f);

    public override void DoWindowContents(Rect inRect)
    {
        try
        {
            DrawContents(inRect);
        }
        catch (Exception ex)
        {
            Text.Anchor = TextAnchor.UpperLeft;
            Text.Font = GameFont.Small;
            GUI.color = Color.white;
            Log.ErrorOnce("[FlexTool Keep It Together] UI error: " + ex, 736450912);
        }
    }

    private void DrawContents(Rect inRect)
    {
        var prev = GUI.color;

        Text.Font = GameFont.Medium;
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 32f), "Relationship Health");
        Text.Font = GameFont.Small;
        GUI.color = new Color(1f, 1f, 1f, 0.6f);
        Widgets.Label(new Rect(inRect.x, inRect.y + 32f, inRect.width, 24f),
            "Couples in the danger zone unlock Forced Reconciliation: they will spend free time together until things recover.");
        GUI.color = prev;

        var listRect = new Rect(inRect.x, inRect.y + 62f, inRect.width, inRect.height - 62f);
        var couples = Couple.FindAll();
        if (couples.Count == 0)
        {
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(listRect, "No colonist couples found.");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prev;
            return;
        }

        couples.SortBy(c => c.Health); // most at-risk first

        var comp = KeepItTogetherComponent.Get();
        float viewHeight = couples.Count * (RowHeight + RowGap);
        var viewRect = new Rect(0f, 0f, listRect.width - 20f, Mathf.Max(viewHeight, listRect.height));
        Widgets.BeginScrollView(listRect, ref _scroll, viewRect);

        float y = 0f;
        foreach (var couple in couples)
        {
            DrawCoupleRow(new Rect(0f, y, viewRect.width, RowHeight), couple, comp);
            y += RowHeight + RowGap;
        }

        Widgets.EndScrollView();
        GUI.color = prev;
    }

    private static void DrawCoupleRow(Rect r, Couple couple, KeepItTogetherComponent comp)
    {
        var prev = GUI.color;
        float health = couple.Health;
        bool danger = health < KeepItTogetherComponent.DangerThreshold;
        bool forced = comp != null && comp.IsForced(couple.Key);

        Widgets.DrawBoxSolid(r, new Color(1f, 1f, 1f, 0.05f));
        if (Mouse.IsOver(r))
        {
            Widgets.DrawHighlight(r);
            TooltipHandler.TipRegion(r,
                $"{couple.A.LabelShortCap}'s opinion of {couple.B.LabelShortCap}: {couple.A.relations?.OpinionOf(couple.B) ?? 0}\n"
                + $"{couple.B.LabelShortCap}'s opinion of {couple.A.LabelShortCap}: {couple.B.relations?.OpinionOf(couple.A) ?? 0}");
        }

        // Names + relation type.
        Text.Anchor = TextAnchor.MiddleLeft;
        Widgets.Label(new Rect(r.x + 8f, r.y, 230f, r.height),
            $"{couple.A.LabelShortCap} + {couple.B.LabelShortCap} ({couple.Relation.GetGenderSpecificLabel(couple.A)})");
        Text.Anchor = TextAnchor.UpperLeft;

        // Health bar.
        var barRect = new Rect(r.x + 246f, r.y + 12f, r.width - 246f - 170f, r.height - 24f);
        var fillTex = health < KeepItTogetherComponent.DangerThreshold ? BarTextures.Red
            : health < 0.6f ? BarTextures.Yellow
            : BarTextures.Green;
        Widgets.FillableBar(barRect, health, fillTex, BarTextures.Background, doBorder: true);
        Text.Anchor = TextAnchor.MiddleCenter;
        Text.Font = GameFont.Tiny;
        Widgets.Label(barRect, $"{health:P0}");
        Text.Font = GameFont.Small;
        Text.Anchor = TextAnchor.UpperLeft;

        // Forced Reconciliation toggle (unlocked only in the danger zone, or already running).
        var toggleRect = new Rect(r.xMax - 162f, r.y + 8f, 154f, r.height - 16f);
        if (danger || forced)
        {
            GUI.color = forced ? new Color(0.3f, 0.9f, 0.4f) : Color.white;
            if (Widgets.ButtonText(toggleRect, forced ? "Reconciling... (ON)" : "Force Reconciliation"))
            {
                comp?.SetForced(couple.Key, !forced);
                SoundStarter.PlayToggleSound(!forced);
            }
            GUI.color = prev;
            if (Mouse.IsOver(toggleRect))
                TooltipHandler.TipRegion(toggleRect,
                    "Forces this couple to spend their recreation hours together having deep talks until their relationship recovers.");
        }
        else
        {
            GUI.color = new Color(1f, 1f, 1f, 0.35f);
            Text.Anchor = TextAnchor.MiddleCenter;
            Widgets.Label(toggleRect, "Stable");
            Text.Anchor = TextAnchor.UpperLeft;
            GUI.color = prev;
        }
    }
}

internal static class SoundStarter
{
    public static void PlayToggleSound(bool on)
    {
        try
        {
            Verse.Sound.SoundStarter.PlayOneShotOnCamera(
                on ? SoundDefOf.Checkbox_TurnedOn : SoundDefOf.Checkbox_TurnedOff, null);
        }
        catch { }
    }
}

/// <summary>
/// Cached solid-color bar textures. Creating a new texture every frame
/// (per row) leaks GPU memory; these are created once and reused.
/// </summary>
[StaticConstructorOnStartup]
internal static class BarTextures
{
    public static readonly Texture2D Red = SolidColorMaterials.NewSolidColorTexture(new Color(0.85f, 0.25f, 0.2f));
    public static readonly Texture2D Yellow = SolidColorMaterials.NewSolidColorTexture(new Color(0.9f, 0.75f, 0.2f));
    public static readonly Texture2D Green = SolidColorMaterials.NewSolidColorTexture(new Color(0.3f, 0.8f, 0.35f));
    public static readonly Texture2D Background = SolidColorMaterials.NewSolidColorTexture(new Color(0.15f, 0.15f, 0.15f));
}
