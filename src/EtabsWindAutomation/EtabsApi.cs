using System.Text;

namespace EtabsWindAutomation;

internal static class EtabsApi
{
    public static void Check(int returnCode, string operation)
    {
        if (returnCode != 0)
        {
            throw new InvalidOperationException($"ETABS API call failed ({returnCode}): {operation}");
        }
    }

    public static void WarnOnFailure(StringBuilder log, int returnCode, string operation)
    {
        if (returnCode != 0)
        {
            log.AppendLine($"WARN: ETABS API call returned {returnCode}: {operation}");
        }
    }
}

