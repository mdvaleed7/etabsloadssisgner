// Minimal stand-in for the ETABSv1 COM-interop API surface used by the plugin.
// This is ONLY for off-ETABS compile verification of the C# in the load classes.
// Signatures mirror the real ETABS 22 (ETABSv1) interfaces for the members used.
using System;

namespace ETABSv1
{
    public enum eLoadPatternType { Dead = 1, SuperDead = 2, Live = 3, Quake = 5, Wind = 6, Other = 8 }
    public enum eItemType { Objects = 0, Group = 1, SelectedObjects = 2 }
    public enum eCNameType { LoadCase = 0, LoadCombo = 1 }
    public enum eUnits { kN_m_C = 6, kN_mm_C = 5, N_mm_C = 9 }
    public enum eMatType { Steel = 1, Concrete = 2 }
    public enum eSlabType { Slab = 0 }
    public enum eShellType { ShellThin = 1 }

    public interface cLoadPatterns
    {
        int Add(string Name, eLoadPatternType MyType, double SelfWTMultiplier = 0, bool AddAnalysisCase = true);
        int GetNameList(ref int NumberNames, ref string[] MyName);
    }

    public interface cCaseModalEigen
    {
        int SetCase(string Name);
        int SetNumberModes(string Name, int MaxModes, int MinModes);
    }

    public interface cCaseResponseSpectrum
    {
        int SetCase(string Name);
        int SetLoads(string Name, int NumberLoads, ref string[] LoadName, ref string[] Func,
                     ref double[] SF, ref string[] CSys, ref double[] Ang);
        int SetModalCase(string Name, string ModalCase);
        int SetModalComb(string Name, int MyType, double F1, double F2, double Td, int StatusType = 1);
        int SetDirComb(string Name, int MyType, double SF = 0);
        int SetDampConstant(string Name, double Damp);
    }

    public interface cCaseStaticLinear
    {
        int SetCase(string Name);
        int SetLoads(string Name, int NumberLoads, ref string[] LoadType, ref string[] LoadName, ref double[] SF);
    }

    public interface cLoadCases
    {
        cCaseModalEigen ModalEigen { get; }
        cCaseResponseSpectrum ResponseSpectrum { get; }
        cCaseStaticLinear StaticLinear { get; }
        int GetNameList(ref int NumberNames, ref string[] MyName);
    }

    public interface cFunctionRS
    {
        int SetUser(string Name, int NumberItems, ref double[] Period, ref double[] Value, double DampRatio);
    }

    public interface cFunction { cFunctionRS FuncRS { get; } }

    public interface cAreaObj
    {
        int GetNameList(ref int NumberNames, ref string[] MyName);
        int GetPoints(string Name, ref int NumberPoints, ref string[] Point);
        int SetLoadUniform(string Name, string LoadPat, double Value, int Dir, bool Replace,
                           string CSys = "Global", eItemType ItemType = eItemType.Objects);
    }

    public interface cFrameObj
    {
        int GetNameList(ref int NumberNames, ref string[] MyName);
        int GetPoints(string Name, ref string Point1, ref string Point2);
        int SetLoadDistributed(string Name, string LoadPat, int MyType, int Dir,
                               double Dist1, double Dist2, double Val1, double Val2,
                               string CSys = "Global", bool RelDist = true, bool Replace = true,
                               eItemType ItemType = eItemType.Objects);
    }

    public interface cPointObj
    {
        int GetCoordCartesian(string Name, ref double X, ref double Y, ref double Z, string CSys = "Global");
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
        int GetNameList(ref int NumberNames, ref string[] MyName);
        int SetCaseList(string Name, ref eCNameType CNameType, string CName, double SF);
    }

    public interface cSourceMass
    {
        int SetMassSource(string Name, bool MassFromElements, bool MassFromMasses, bool MassFromLoads,
                          bool IsDefault, int NumberLoads, ref string[] LoadPat, ref double[] SF);
    }

    public interface cSapModel
    {
        cLoadPatterns LoadPatterns { get; }
        cLoadCases LoadCases { get; }
        cFunction Func { get; }
        cAreaObj AreaObj { get; }
        cFrameObj FrameObj { get; }
        cPointObj PointObj { get; }
        cStory Story { get; }
        cRespCombo RespCombo { get; }
        cSourceMass SourceMass { get; }
        eUnits GetPresentUnits();
        bool GetModelIsLocked();
        int SetModelIsLocked(bool LockIt);
    }
}
