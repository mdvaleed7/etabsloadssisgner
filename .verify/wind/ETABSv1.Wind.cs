// Extended stand-in for the ETABSv1 COM-interop API surface used by the
// EtabsWindAutomation code. OFF-ETABS compile verification only.
// Signatures mirror the real ETABS (ETABSv1) interfaces for the members used
// by WindPluginRunner / EtabsLoadApplier / EtabsModelExtractor.
using System;

namespace ETABSv1
{
    public enum eLoadPatternType { Dead = 1, SuperDead = 2, Live = 3, Quake = 5, Wind = 6, Other = 8 }
    public enum eItemType { Objects = 0, Group = 1, SelectedObjects = 2 }
    public enum eCNameType { LoadCase = 0, LoadCombo = 1 }
    public enum eUnits { lb_in_F = 1, lb_ft_F = 2, kip_in_F = 3, kip_ft_F = 4, kN_mm_C = 5, kN_m_C = 6, kgf_mm_C = 7, kgf_m_C = 8, N_mm_C = 9, N_m_C = 10, Ton_mm_C = 11, Ton_m_C = 12, kN_cm_C = 13, kgf_cm_C = 14, N_cm_C = 15, Ton_cm_C = 16 }

    public interface cLoadPatterns
    {
        int Add(string Name, eLoadPatternType MyType, double SelfWTMultiplier = 0, bool AddAnalysisCase = true);
        int GetNameList(ref int NumberNames, ref string[] MyName);
    }

    public interface cCaseStaticLinear
    {
        int SetCase(string Name);
        int SetLoads(string Name, int NumberLoads, ref string[] LoadType, ref string[] LoadName, ref double[] SF);
    }

    public interface cLoadCases
    {
        cCaseStaticLinear StaticLinear { get; }
        int GetNameList(ref int NumberNames, ref string[] MyName);
    }

    public interface cAreaObj
    {
        int SetLoadUniform(string Name, string LoadPat, double Value, int Dir, bool Replace,
                           string CSys = "Global", eItemType ItemType = eItemType.Objects);
        int SetModifiers(string Name, ref double[] Value, eItemType ItemType = eItemType.Objects);
    }

    public interface cFrameObj
    {
        int SetLoadDistributed(string Name, string LoadPat, int MyType, int Dir,
                               double Dist1, double Dist2, double Val1, double Val2,
                               string CSys = "Global", bool RelDist = true, bool Replace = true,
                               eItemType ItemType = eItemType.Objects);
        int SetModifiers(string Name, ref double[] Value, eItemType ItemType = eItemType.Objects);
    }

    public interface cPropArea
    {
        int SetModifiers(string Name, ref double[] Value);
    }

    public interface cPropFrame
    {
        int SetModifiers(string Name, ref double[] Value);
    }

    public interface cPointObj
    {
        int GetCoordCartesian(string Name, ref double X, ref double Y, ref double Z, string CSys = "Global");
        int GetNameListOnStory(string StoryName, ref int NumberNames, ref string[] MyName);
        int SetLoadForce(string Name, string LoadPat, ref double[] Value, bool Replace = false,
                         string CSys = "Global", eItemType ItemType = eItemType.Objects);
        int DeleteLoadForce(string Name, string LoadPat, eItemType ItemType = eItemType.Objects);
    }

    public interface cStory
    {
        int GetStories(ref int NumberStories, ref string[] StoryNames, ref double[] StoryElevations,
                       ref double[] StoryHeights, ref bool[] IsMasterStory, ref string[] SimilarToStory,
                       ref bool[] SpliceAbove, ref double[] SpliceHeight);
    }

    public interface cRespCombo
    {
        int Add(string Name, int ComboType);
        int Delete(string Name);
        int SetCaseList(string Name, ref eCNameType CNameType, string CName, double SF);
    }

    public interface cSapModel
    {
        cLoadPatterns LoadPatterns { get; }
        cLoadCases LoadCases { get; }
        cAreaObj AreaObj { get; }
        cFrameObj FrameObj { get; }
        cPropArea PropArea { get; }
        cPropFrame PropFrame { get; }
        cPointObj PointObj { get; }
        cStory Story { get; }
        cRespCombo RespCombo { get; }
        eUnits GetPresentUnits();
        int SetPresentUnits(eUnits Units);
        bool GetModelIsLocked();
        int SetModelIsLocked(bool LockIt);
    }
}
