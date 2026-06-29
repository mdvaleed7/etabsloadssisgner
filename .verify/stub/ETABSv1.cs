// Minimal stand-in for the ETABSv1 COM-interop API surface used by the plugin.
// This is ONLY for off-ETABS compile verification of the C# in the load classes.
// Signatures mirror the real ETABS 18-23 (ETABSv1) interfaces for the members used.
//
// NOTE: the real DLL is a COM interop assembly. The EtabsApi compatibility layer
// invokes several of these members by REFLECTION (to tolerate v18..v23 naming
// drift), so this stub only needs to be shape-compatible for the strongly-typed
// call sites; reflection paths resolve at runtime against the real DLL.
using System;

namespace ETABSv1
{
    public enum eLoadPatternType { Dead = 1, SuperDead = 2, Live = 3, ReduceLive = 4, Quake = 5, Wind = 6, Snow = 7, Other = 8 }
    public enum eItemType { Objects = 0, Group = 1, SelectedObjects = 2 }
    public enum eItemTypeElm { ObjectElm = 0, Element = 1, GroupElm = 2, SelectionElm = 3 }
    public enum eCNameType { LoadCase = 0, LoadCombo = 1 }
    public enum eUnits { lb_in_F = 1, lb_ft_F = 2, kip_in_F = 3, kip_ft_F = 4, kN_mm_C = 5, kN_m_C = 6, kgf_mm_C = 7, kgf_m_C = 8, N_mm_C = 9, N_m_C = 10, Ton_mm_C = 11, Ton_m_C = 12, kN_cm_C = 13, kgf_cm_C = 14, N_cm_C = 15, Ton_cm_C = 16 }
    public enum eMatType { Steel = 1, Concrete = 2, NoDesign = 3, Aluminum = 4, ColdFormed = 5, Rebar = 6, Tendon = 7, Masonry = 8 }
    public enum eSlabType { Slab = 0, Drop = 1, Stiff = 2, Ribbed = 3, Waffle = 4, Mat = 5, Footing = 6 }
    public enum eShellType { ShellThin = 1, ShellThick = 2, Membrane = 3, Layered = 4 }

    public interface cLoadPatterns
    {
        int Add(string Name, eLoadPatternType MyType, double SelfWTMultiplier = 0, bool AddAnalysisCase = true);
        int Delete(string Name);
        int GetNameList(ref int NumberNames, ref string[] MyName);
        int GetLoadType(string Name, ref eLoadPatternType MyType);
        int GetSelfWTMultiplier(string Name, ref double SelfWTMultiplier);
        int SetSelfWTMultiplier(string Name, double SelfWTMultiplier);
        cAutoSeismic AutoSeismic { get; }
    }

    public interface cAutoSeismic
    {
        int SetIS1893_2016(string Name, int DirFlag, double Eccen, int PeriodFlag, double UserT,
                           double ZoneFactor, int SoilTypeIS, double ImpFactor, double ResponseReduction);
        int GetIS1893_2016(string Name, ref int DirFlag, ref double Eccen, ref int PeriodFlag, ref double UserT,
                           ref double ZoneFactor, ref int SoilTypeIS, ref double ImpFactor, ref double ResponseReduction);
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
        int GetLoads(string Name, ref int NumberLoads, ref string[] LoadName, ref string[] Func,
                     ref double[] SF, ref string[] CSys, ref double[] Ang);
        int SetModalCase(string Name, string ModalCase);
        int SetModalComb(string Name, int MyType, double F1, double F2, double Td, int StatusType = 1);
        int SetModalComb_1(string Name, int MyType, double F1, double F2, double Td, int StatusType);
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
        int GetProperty(string Name, ref string PropName);
        int SetProperty(string Name, string PropName, eItemType ItemType = eItemType.Objects);
        int GetSelected(string Name, ref bool Selected);
        int SetLoadUniform(string Name, string LoadPat, double Value, int Dir, bool Replace,
                           string CSys = "Global", eItemType ItemType = eItemType.Objects);
        int GetLoadUniform(string Name, ref int NumberItems, ref string[] AreaName, ref string[] LoadPat,
                           ref string[] CSys, ref int[] Dir, ref double[] Value,
                           eItemType ItemType = eItemType.Objects);
        int DeleteLoadUniform(string Name, string LoadPat, eItemType ItemType = eItemType.Objects);
    }

    public interface cFrameObj
    {
        int GetNameList(ref int NumberNames, ref string[] MyName);
        int GetPoints(string Name, ref string Point1, ref string Point2);
        int GetSection(string Name, ref string PropName, ref string SAuto);
        int GetSelected(string Name, ref bool Selected);
        int SetLoadDistributed(string Name, string LoadPat, int MyType, int Dir,
                               double Dist1, double Dist2, double Val1, double Val2,
                               string CSys = "Global", bool RelDist = true, bool Replace = true,
                               eItemType ItemType = eItemType.Objects);
        int DeleteLoadDistributed(string Name, string LoadPat, eItemType ItemType = eItemType.Objects);
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
        int GetNameList(ref int NumberNames, ref string[] MyName);
    }

    public interface cDiaphragm
    {
        int GetNameList(ref int NumberNames, ref string[] MyName);
    }

    public interface cRespCombo
    {
        int Add(string Name, int ComboType);
        int Delete(string Name);
        int GetNameList(ref int NumberNames, ref string[] MyName);
        int SetCaseList(string Name, ref eCNameType CNameType, string CName, double SF);
        int GetCaseList(string Name, ref int NumberItems, ref eCNameType[] CNameType, ref string[] CName, ref double[] SF);
    }

    public interface cSourceMass
    {
        int SetMassSource(string Name, bool MassFromElements, bool MassFromMasses, bool MassFromLoads,
                          bool IsDefault, int NumberLoads, ref string[] LoadPat, ref double[] SF);
    }

    public interface cPropMaterial
    {
        int GetNameList(ref int NumberNames, ref string[] MyName);
        int GetTypeOAPI(string Name, ref eMatType MatType, ref int SymType);
        int GetOConcrete_1(string Name, ref double Fc, ref bool IsLightweight, ref double FcsFactor,
                           ref int SSType, ref int SSHysType, ref double StrainAtFc,
                           ref double StrainUltimate, ref double FinalSlope,
                           ref double FrictionAngle, ref double DilatationalAngle);
    }

    public interface cPropArea
    {
        int GetSlab(string Name, ref eSlabType SlabType, ref eShellType ShellType, ref string MatProp,
                    ref double Thickness, ref int Color, ref string Notes, ref string GUID);
        int SetSlab(string Name, eSlabType SlabType, eShellType ShellType, string MatProp, double Thickness,
                    int Color = -1, string Notes = "", string GUID = "");
    }

    public interface cPropFrame
    {
        int GetRectangle(string Name, ref string FileName, ref string MatProp, ref double T3, ref double T2,
                         ref int Color, ref string Notes, ref string GUID);
    }

    public interface cView { int RefreshView(int Window = 0, bool Zoom = true); }

    public interface cAnalyze
    {
        int RunAnalysis();
        int SetRunCaseFlag(string Name, bool Run, bool All = false);
    }

    public interface cAnalysisResultsSetup
    {
        int DeselectAllCasesAndCombosForOutput();
        int SetCaseSelectedForOutput(string Name, bool Selected = true);
        int SetComboSelectedForOutput(string Name, bool Selected = true);
    }

    public interface cAnalysisResults
    {
        cAnalysisResultsSetup Setup { get; }
        int BaseReact(ref int NumberResults, ref string[] LoadCase, ref string[] StepType, ref double[] StepNum,
                      ref double[] Fx, ref double[] Fy, ref double[] Fz, ref double[] Mx, ref double[] My, ref double[] Mz,
                      ref double gx, ref double gy, ref double gz);
    }

    public interface cSelect
    {
        int GetSelected(ref int NumberItems, ref int[] ObjectType, ref string[] ObjectName);
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
        cDiaphragm Diaphragm { get; }
        cRespCombo RespCombo { get; }
        cSourceMass SourceMass { get; }
        cPropMaterial PropMaterial { get; }
        cPropArea PropArea { get; }
        cPropFrame PropFrame { get; }
        cView View { get; }
        cAnalyze Analyze { get; }
        cAnalysisResults Results { get; }
        cSelect SelectObj { get; }
        eUnits GetPresentUnits();
        int SetPresentUnits(eUnits Units);
        bool GetModelIsLocked();
        int SetModelIsLocked(bool LockIt);
    }
}
