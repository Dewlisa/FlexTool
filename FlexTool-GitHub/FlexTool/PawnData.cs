namespace FlexTool;

public class SkillData
{
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int Passion { get; set; } // 0=None, 1=Minor, 2=Major
}

public class HealthCondition
{
    public string Condition { get; set; } = "";
    public string BodyPart { get; set; } = "";
}

public class GearItem
{
    public string Name { get; set; } = "";
    public string Kind { get; set; } = ""; // Apparel / Weapon / Inventory
    public int HitPoints { get; set; } = -1;
    public string Quality { get; set; } = "";
}

public class PawnData
{
    public string FirstName { get; set; } = "";
    public string Nickname { get; set; } = "";
    public string LastName { get; set; } = "";
    public string Gender { get; set; } = "Female";
    public int BioAge { get; set; } = 25;
    public int ChronoAge { get; set; } = 25;
    public string Childhood { get; set; } = "Vatgrown Soldier";
    public string Adulthood { get; set; } = "Marine";

    public string BodyType { get; set; } = "Thin";
    public string HeadType { get; set; } = "Average_Normal";
    public string Hair { get; set; } = "Shaved";
    public string HairColor { get; set; } = "Brown";
    public string SkinColor { get; set; } = "Light";
    public string Beard { get; set; } = "None";

    public List<SkillData> Skills { get; set; } = [];
    public List<string> Traits { get; set; } = [];
    public List<HealthCondition> HealthConditions { get; set; } = [];
    public List<GearItem> Gear { get; set; } = [];

    public static List<PawnData> CreateSampleColony() => [];
}
